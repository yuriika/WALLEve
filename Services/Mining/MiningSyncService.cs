using System.Globalization;
using Microsoft.EntityFrameworkCore;
using WALLEve.Data;
using WALLEve.Models.Esi.Character;
using WALLEve.Models.Mining;
using WALLEve.Services.Esi.Interfaces;
using WALLEve.Services.Mining.Interfaces;

namespace WALLEve.Services.Mining;

/// <summary>
/// Idempotente Synchronisation des persönlichen Mining-Ledgers (#39).
/// Der ESI-Schlüssel (date, type_id, solar_system_id) wird auf
/// (CharacterId, Date, TypeId, SolarSystemId) gespiegelt; eine erneute
/// Synchronisation ersetzt die kumulierte Tagesmenge (Korrektur), statt sie
/// aufzuaddieren. Fehlt eine Zeile in der ESI-Antwort, wird sie nicht
/// gelöscht — Historie außerhalb des API-Fensters bleibt erhalten.
/// </summary>
public class MiningSyncService : IMiningSyncService
{
    private readonly WalletDbContext _db;
    private readonly IEsiApiService _esiApi;
    private readonly ILogger<MiningSyncService> _logger;

    public MiningSyncService(
        WalletDbContext db,
        IEsiApiService esiApi,
        ILogger<MiningSyncService> logger)
    {
        _db = db;
        _esiApi = esiApi;
        _logger = logger;
    }

    public async Task<MiningSyncResult> SynchronizeAsync(int characterId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var ledger = await _esiApi.GetCharacterMiningLedgerAsync(characterId, ct);
        if (ledger == null)
        {
            // M0-Vertrag: null = Fehler/Abbruch (keine Teildaten) — der
            // publizierte Ledger-Zustand bleibt unverändert.
            _logger.LogWarning("Mining-Ledger-Sync für Character {CharacterId}: ESI lieferte kein vollständiges Ergebnis", characterId);
            return new MiningSyncResult
            {
                Success = false,
                Error = "ESI lieferte kein vollständiges Mining-Ledger (Fehler oder Abbruch).",
                Total = 0
            };
        }

        var existing = await _db.MiningLedgerEntries
            .Where(e => e.CharacterId == characterId)
            .ToListAsync(ct);
        var byKey = existing.ToDictionary(e => (e.Date, e.TypeId, e.SolarSystemId));

        var now = DateTime.UtcNow;
        var inserted = 0;
        var updated = 0;

        foreach (var entry in ledger)
        {
            var date = DateOnly.ParseExact(entry.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture)
                .ToDateTime(TimeOnly.MinValue);

            if (byKey.TryGetValue((date, entry.TypeId, entry.SolarSystemId), out var stored))
            {
                // Korrigierte Tagesmenge ersetzt den alten Wert — Idempotenz:
                // eine erfolgreiche Wiederholung verändert die Menge nicht erneut.
                if (stored.Quantity != entry.Quantity)
                {
                    stored.Quantity = entry.Quantity;
                    stored.UpdatedAt = now;
                    updated++;
                }
            }
            else
            {
                _db.MiningLedgerEntries.Add(new MiningLedgerEntry
                {
                    CharacterId = characterId,
                    Date = date,
                    TypeId = entry.TypeId,
                    SolarSystemId = entry.SolarSystemId,
                    Quantity = entry.Quantity,
                    UpdatedAt = now
                });
                inserted++;
            }
        }

        // Ein einzelner SaveChanges ist atomar: keine Teildaten bei Abbruch.
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Mining-Ledger-Sync für Character {CharacterId} abgeschlossen: {Inserted} neu, {Updated} korrigiert, {Total} Zeilen von ESI",
            characterId, inserted, updated, ledger.Count);

        return new MiningSyncResult
        {
            Success = true,
            Inserted = inserted,
            Updated = updated,
            Total = ledger.Count
        };
    }
}