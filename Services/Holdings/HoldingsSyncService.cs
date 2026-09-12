using Microsoft.EntityFrameworkCore;
using WALLEve.Data;
using WALLEve.Models.Esi.Character;
using WALLEve.Models.Holdings;
using WALLEve.Services.Esi.Interfaces;
using WALLEve.Services.Holdings.Interfaces;

namespace WALLEve.Services.Holdings;

/// <summary>
/// Atomare Synchronisation vollständiger Character-Snapshots.
/// Der M0-ESI-Vertrag liefert null bei Fehlern/Abbruch (keine Teildaten) und
/// eine leere Liste bei gültig leerem Gesamtergebnis; beide Fälle werden hier
/// strikt getrennt, damit nur ein Gesamterfolg den publizierten Bestand ersetzt.
/// </summary>
public class HoldingsSyncService : IHoldingsSyncService
{
    private const string StatusRunning = "running";
    private const string StatusCompleted = "completed";
    private const string StatusFailed = "failed";

    private readonly WalletDbContext _db;
    private readonly IEsiApiService _esiApi;
    private readonly IPortfolioSnapshotService _portfolio;
    private readonly ILogger<HoldingsSyncService> _logger;

    public HoldingsSyncService(
        WalletDbContext db,
        IEsiApiService esiApi,
        IPortfolioSnapshotService portfolio,
        ILogger<HoldingsSyncService> logger)
    {
        _db = db;
        _esiApi = esiApi;
        _portfolio = portfolio;
        _logger = logger;
    }

    public async Task<HoldingSyncRun> SynchronizeAsync(int characterId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var run = new HoldingSyncRun
        {
            OwnerType = OwnerType.Character,
            OwnerId = characterId,
            StartedAt = DateTime.UtcNow,
            Status = StatusRunning
        };
        _db.HoldingSyncRuns.Add(run);
        await _db.SaveChangesAsync(ct);

        List<CharacterAsset>? assets;
        try
        {
            assets = await _esiApi.GetCharacterAssetsAsync(characterId);
        }
        catch (OperationCanceledException)
        {
            // Abbruch: Lauf als fehlgeschlagen protokollieren, nichts publizieren.
            await FailAsync(run, "Synchronisation abgebrochen.");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Asset-Sync für Character {CharacterId} fehlgeschlagen", characterId);
            await FailAsync(run, $"Synchronisation fehlgeschlagen: {ex.Message}");
            throw;
        }

        try
        {
            ct.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException)
        {
            await FailAsync(run, "Synchronisation abgebrochen.");
            throw;
        }

        if (assets == null)
        {
            // ESI lieferte kein vollständiges Ergebnis (Fehler/Abbruch im Vertrag):
            // kein Snapshot — der publizierte Bestand bleibt unverändert.
            await FailAsync(run, "ESI lieferte keine vollständigen Assets (Fehler oder Abbruch).");
            return run;
        }

        // Gesamterfolg — auch gültig leer ersetzt den Bestand korrekt.
        var snapshot = new HoldingSnapshot
        {
            SyncRunId = run.Id,
            OwnerType = OwnerType.Character,
            OwnerId = characterId,
            SyncedAt = DateTime.UtcNow,
            Source = $"esi/characters/{characterId}/assets",
            Items = assets.Select(a => new HoldingItem
            {
                ItemId = a.ItemId,
                TypeId = a.TypeId,
                Quantity = a.Quantity,
                IsSingleton = a.IsSingleton,
                LocationId = a.LocationId,
                LocationFlag = a.LocationFlag,
                ParentItemId = a.ParentItemId
            }).ToList()
        };
        _db.HoldingSnapshots.Add(snapshot);
        run.CompletedAt = DateTime.UtcNow;
        run.Status = StatusCompleted;
        await _db.SaveChangesAsync(ct);

        // Historischen Portfolio-Punkt an den vollständigen Sync hängen (#51):
        // Nur der Gesamterfolg (auch gültig leer) erzeugt Historie. Schlägt die
        // Erfassung fehl, ist der Lauf fehlgeschlagen — kein Punkt ohne Zähler.
        try
        {
            await _portfolio.CaptureAsync(snapshot.Id, ct);
        }
        catch (OperationCanceledException)
        {
            await FailAsync(run, "Synchronisation abgebrochen.");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Portfolio-Erfassung für Character {CharacterId} fehlgeschlagen", characterId);
            await FailAsync(run, $"Portfolio-Erfassung fehlgeschlagen: {ex.Message}");
            throw;
        }

        _logger.LogInformation("Asset-Sync für Character {CharacterId} abgeschlossen: {Count} Items publiziert",
            characterId, snapshot.Items.Count);
        return run;
    }

    public async Task<HoldingSnapshot?> GetLatestSnapshotAsync(int characterId, CancellationToken ct = default)
        => await _db.HoldingSnapshots
            .Include(s => s.Items)
            .Where(s => s.OwnerType == OwnerType.Character
                        && s.OwnerId == characterId
                        && s.SyncRun.Status == StatusCompleted)
            .OrderByDescending(s => s.SyncedAt)
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// Markiert den Lauf fehlgeschlagen ohne Snapshot. Speichert bewusst ohne
    /// CancellationToken: Auch bei Abbruch muss der Lauf-Status erhalten bleiben.
    /// </summary>
    private async Task FailAsync(HoldingSyncRun run, string error)
    {
        run.Status = StatusFailed;
        run.Error = error;
        await _db.SaveChangesAsync(CancellationToken.None);
    }
}