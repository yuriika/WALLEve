using Microsoft.EntityFrameworkCore;
using WALLEve.Data;
using WALLEve.Models.Esi.Markets;
using WALLEve.Models.Holdings;
using WALLEve.Models.Stockpiles;
using WALLEve.Services.Stockpiles.Interfaces;

namespace WALLEve.Services.Stockpiles;

/// <summary>
/// Übersichts-Service für die Stockpile-UI (Issue #53). Kombiniert die
/// Bestandsberechnung (#43) mit den Quellen-Freshness-Angaben des Owners:
/// Zeitpunkt des jüngsten abgeschlossenen Holding-Snapshots sowie die
/// Verfügbarkeit der Order-Quelle. Beide Werte werden immer mitgeliefert,
/// damit die UI fehlende Quellen kenntlich macht, statt Nullbestand zu zeigen.
/// Keine Live-ESI-Aufrufe; Orders kommen vom Aufrufer.
/// </summary>
public class StockpileOverviewService : IStockpileOverviewService
{
    private readonly WalletDbContext _db;
    private readonly IStockpileCalculationService _calculation;

    public StockpileOverviewService(WalletDbContext db, IStockpileCalculationService calculation)
    {
        _db = db;
        _calculation = calculation;
    }

    public async Task<StockpileOverview> GetOverviewAsync(
        OwnerType ownerType,
        int ownerId,
        bool includeArchived = false,
        IReadOnlyList<MarketOrder>? orders = null,
        bool ordersAvailable = false,
        DateTime? ordersSyncedAt = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // Freshness der physischen Quelle: jüngster abgeschlossener Snapshot DIESES Owners.
        var syncedAt = await _db.HoldingSnapshots
            .AsNoTracking()
            .Where(s => s.OwnerType == ownerType
                        && s.OwnerId == ownerId
                        && s.SyncRun.Status == "completed")
            .OrderByDescending(s => s.SyncedAt)
            .Select(s => (DateTime?)s.SyncedAt)
            .FirstOrDefaultAsync(ct);

        var lines = await _calculation.CalculateAsync(
            ownerType,
            ownerId,
            includeArchived,
            orders,
            ordersAvailable,
            ct);

        return new StockpileOverview
        {
            OwnerType = ownerType,
            OwnerId = ownerId,
            Lines = lines,
            PhysicalSourceSyncedAt = syncedAt,
            PhysicalSourceAvailable = syncedAt.HasValue,
            OrdersSourceAvailable = ordersAvailable,
            OrdersSourceSyncedAt = ordersAvailable ? ordersSyncedAt : null
        };
    }
}
