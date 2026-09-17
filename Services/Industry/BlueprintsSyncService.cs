using Microsoft.EntityFrameworkCore;
using WALLEve.Data;
using WALLEve.Models.Esi.Character;
using WALLEve.Models.Industry;
using WALLEve.Services.Esi.Interfaces;
using WALLEve.Services.Industry.Interfaces;

namespace WALLEve.Services.Industry;

/// <summary>
/// Idempotente Synchronisation der Character-Blueprints (#49).
/// Der ESI-Schlüssel item_id wird je Character auf (CharacterId, ItemId)
/// gespiegelt; eine erneute Synchronisation ersetzt nur die mutablen Felder
/// (Ort, Menge, ME/TE, Runs, Kopierstatus) statt neue Zeilen anzulegen. Der
/// BPO-Sentinel (Runs = -1) und die BPC-Runs sowie ME/TE und Ort werden als
/// Rohwerte erhalten — nie auseinander abgeleitet. Fehlt ein Blueprint in der
/// ESI-Antwort (verkauft/zerstört), wird er nicht gelöscht — die Historie
/// bleibt erhalten.
/// </summary>
public class BlueprintsSyncService : IBlueprintsSyncService
{
    private readonly WalletDbContext _db;
    private readonly IEsiApiService _esiApi;
    private readonly ILogger<BlueprintsSyncService> _logger;

    public BlueprintsSyncService(
        WalletDbContext db,
        IEsiApiService esiApi,
        ILogger<BlueprintsSyncService> logger)
    {
        _db = db;
        _esiApi = esiApi;
        _logger = logger;
    }

    public async Task<BlueprintsSyncResult> SynchronizeAsync(int characterId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var blueprints = await _esiApi.GetCharacterBlueprintsAsync(characterId, ct);
        if (blueprints == null)
        {
            // M0-Vertrag: null = Fehler/Abbruch (keine Teildaten) — der
            // persistierte Blueprint-Bestand bleibt unverändert.
            _logger.LogWarning("Blueprint-Sync für Character {CharacterId}: ESI lieferte kein vollständiges Ergebnis", characterId);
            return new BlueprintsSyncResult
            {
                Success = false,
                Error = "ESI lieferte keine vollständigen Blueprints (Fehler oder Abbruch).",
                Total = 0
            };
        }

        var existing = await _db.BlueprintEntries
            .Where(e => e.CharacterId == characterId)
            .ToListAsync(ct);
        var byItemId = existing.ToDictionary(e => e.ItemId);

        var now = DateTime.UtcNow;
        var inserted = 0;
        var updated = 0;

        foreach (var bp in blueprints)
        {
            if (byItemId.TryGetValue(bp.ItemId, out var stored))
            {
                // Ort/ME/TE/Runs können sich real ändern (Research, Umzug,
                // Copy-Verbrauch) — sie ersetzen die alten Werte. Idempotenz:
                // eine erfolgreiche Wiederholung mit unveränderten Daten ändert nichts.
                if (ApplyChanges(stored, bp, now))
                {
                    updated++;
                }
            }
            else
            {
                _db.BlueprintEntries.Add(MapNew(characterId, bp, now));
                inserted++;
            }
        }

        // Ein einzelner SaveChanges ist atomar: keine Teildaten bei Abbruch.
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Blueprint-Sync für Character {CharacterId} abgeschlossen: {Inserted} neu, {Updated} aktualisiert, {Total} Blueprints von ESI",
            characterId, inserted, updated, blueprints.Count);

        return new BlueprintsSyncResult
        {
            Success = true,
            Inserted = inserted,
            Updated = updated,
            Total = blueprints.Count
        };
    }

    private static BlueprintEntry MapNew(int characterId, CharacterBlueprint bp, DateTime now)
        => new()
        {
            CharacterId = characterId,
            ItemId = bp.ItemId,
            TypeId = bp.TypeId,
            LocationId = bp.LocationId,
            LocationFlag = bp.LocationFlag,
            Quantity = bp.Quantity,
            MaterialEfficiency = bp.MaterialEfficiency,
            TimeEfficiency = bp.TimeEfficiency,
            Runs = bp.Runs,
            IsBlueprintCopy = bp.IsBlueprintCopy,
            UpdatedAt = now
        };

    /// <summary>
    /// Aktualisiert nur die mutablen Felder eines Bestands-Blueprints. Der
    /// ESI-Schlüssel (CharacterId, ItemId) bleibt unverändert; Runs (inkl.
    /// BPO-Sentinel -1) und IsBlueprintCopy werden unabhängig voneinander
    /// als Rohwerte übernommen — keine Ableitung zwischen den Feldern.
    /// Liefert true, wenn sich mindestens ein Feld geändert hat.
    /// </summary>
    private static bool ApplyChanges(BlueprintEntry stored, CharacterBlueprint bp, DateTime now)
    {
        var changed = false;

        if (stored.LocationId != bp.LocationId)
        {
            stored.LocationId = bp.LocationId;
            changed = true;
        }

        if (stored.LocationFlag != bp.LocationFlag)
        {
            stored.LocationFlag = bp.LocationFlag;
            changed = true;
        }

        if (stored.Quantity != bp.Quantity)
        {
            stored.Quantity = bp.Quantity;
            changed = true;
        }

        if (stored.MaterialEfficiency != bp.MaterialEfficiency)
        {
            stored.MaterialEfficiency = bp.MaterialEfficiency;
            changed = true;
        }

        if (stored.TimeEfficiency != bp.TimeEfficiency)
        {
            stored.TimeEfficiency = bp.TimeEfficiency;
            changed = true;
        }

        if (stored.Runs != bp.Runs)
        {
            stored.Runs = bp.Runs;
            changed = true;
        }

        if (stored.IsBlueprintCopy != bp.IsBlueprintCopy)
        {
            stored.IsBlueprintCopy = bp.IsBlueprintCopy;
            changed = true;
        }

        if (changed)
        {
            stored.UpdatedAt = now;
        }

        return changed;
    }
}