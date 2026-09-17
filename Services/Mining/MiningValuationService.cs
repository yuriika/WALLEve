using Microsoft.EntityFrameworkCore;
using WALLEve.Data;
using WALLEve.Models.Mining;
using WALLEve.Services.Mining.Interfaces;
using WALLEve.Services.Sde.Interfaces;

namespace WALLEve.Services.Mining;

/// <summary>
/// Auswertung des persönlichen Mining-Ledgers (#48): Mengen nach Zeitraum,
/// Erztyp und verfügbarer Systemdimension gruppieren und am Markt einer
/// wählbaren Region bewerten („nearest/comparison valuation samt Quelle/Alter“).
/// Die Bewertung stammt aus dem neuesten MarketSnapshot der Region; fehlende
/// Preise oder nicht auflösbare Namen werden als unbekannt erhalten (null, nie
/// 0 oder erfundene Werte). Der Service ist strikt lesend: Er bucht nie Bestand
/// oder eine kostenlose Kostenbasis — die Abbaumenge ist Aktivität, kein Bestand.
/// </summary>
public class MiningValuationService : IMiningValuationService
{
    private readonly WalletDbContext _db;
    private readonly ISdeUniverseService _sde;
    private readonly ILogger<MiningValuationService> _logger;

    public MiningValuationService(
        WalletDbContext db,
        ISdeUniverseService sde,
        ILogger<MiningValuationService> logger)
    {
        _db = db;
        _sde = sde;
        _logger = logger;
    }

    public async Task<MiningValuationReport> GetReportAsync(
        int characterId,
        MiningValuationFilter filter,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // Nur Ledger-Zeilen des Charakters (Owner-Isolation) im Zeitraum
        // (From inklusiv, To exklusiv — Datumsgrenzen normalisiert).
        var entries = await _db.MiningLedgerEntries
            .AsNoTracking()
            .Where(e => e.CharacterId == characterId
                     && e.Date >= filter.From
                     && e.Date < filter.To)
            .ToListAsync(ct);

        if (entries.Count == 0)
        {
            return new MiningValuationReport
            {
                Rows = Array.Empty<MiningValuationRow>(),
                TotalQuantity = 0,
                TotalValue = null,
                KnownCount = 0,
                UnknownCount = 0,
                RegionId = filter.RegionId,
                RegionName = await _sde.GetRegionNameAsync(filter.RegionId),
                From = filter.From,
                To = filter.To,
                GroupBySolarSystem = filter.GroupBySolarSystem
            };
        }

        // Gruppierung: je Erztyp, optional zusätzlich je Abbau-System (#48).
        var groups = entries
            .GroupBy(e => filter.GroupBySolarSystem
                ? (TypeId: e.TypeId, SystemId: (int?)e.SolarSystemId)
                : (TypeId: e.TypeId, SystemId: (int?)null))
            .OrderByDescending(g => g.Sum(e => e.Quantity))
            .ToList();

        var typeIds = groups.Select(g => g.Key.TypeId).Distinct().ToList();
        var systemIds = groups
            .Select(g => g.Key.SystemId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        // Neuester Snapshot je Type in der gewählten Region — ein Lookup je
        // Ladevorgang statt eines pro Ledger-Zeile (kein N+1).
        var snapshots = await _db.MarketSnapshots
            .AsNoTracking()
            .Where(s => s.RegionId == filter.RegionId && typeIds.Contains(s.TypeId))
            .GroupBy(s => s.TypeId)
            .Select(g => g.OrderByDescending(s => s.Timestamp).First())
            .ToDictionaryAsync(s => s.TypeId, ct);

        var rows = new List<MiningValuationRow>(groups.Count);
        foreach (var group in groups)
        {
            var typeId = group.Key.TypeId;
            var systemId = group.Key.SystemId;
            var quantity = group.Sum(e => e.Quantity);

            snapshots.TryGetValue(typeId, out var snapshot);
            var unitPrice = snapshot?.BestSellPrice;

            rows.Add(new MiningValuationRow
            {
                TypeId = typeId,
                TypeName = await _sde.GetTypeNameAsync(typeId),
                SolarSystemId = systemId,
                SystemName = systemId.HasValue
                    ? (await _sde.GetSolarSystemAsync(systemId.Value))?.Name
                    : null,
                Quantity = quantity,
                UnitPrice = unitPrice,
                Value = unitPrice.HasValue ? quantity * unitPrice.Value : null,
                QuoteTimestamp = snapshot?.Timestamp
            });
        }

        var known = rows.Where(r => r.HasValuation).ToList();
        var totalValue = known.Count > 0 ? known.Sum(r => r.Value) : (double?)null;

        return new MiningValuationReport
        {
            Rows = rows,
            TotalQuantity = rows.Sum(r => r.Quantity),
            TotalValue = totalValue,
            KnownCount = known.Count,
            UnknownCount = rows.Count - known.Count,
            RegionId = filter.RegionId,
            RegionName = await _sde.GetRegionNameAsync(filter.RegionId),
            From = filter.From,
            To = filter.To,
            GroupBySolarSystem = filter.GroupBySolarSystem
        };
    }
}