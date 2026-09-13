using Microsoft.EntityFrameworkCore;
using WALLEve.Data;
using WALLEve.Models.Esi.Markets;
using WALLEve.Models.Holdings;
using WALLEve.Models.Stockpiles;
using WALLEve.Services.Stockpiles.Interfaces;

namespace WALLEve.Services.Stockpiles;

/// <summary>
/// Orchestriert die Stockpile-Berechnung (Issue #43): lädt die Ziele des Owners
/// (mit Archivierungs-Option) und den jüngsten abgeschlossenen Holding-Snapshot
/// als physische Basis, wandelt Orders in reine Order-Zeilen und delegiert die
/// eigentliche, deterministische Ableitung an <see cref="StockpileCalculator"/>.
/// Fehlender Snapshot ("physical-source-missing") und fehlende/unkomplette
/// Orders ("orders-source-missing") werden als Partial-Zustand durchgereicht.
/// Keine Live-ESI-Aufrufe — Orders kommen vom Aufrufer.
/// </summary>
public class StockpileCalculationService : IStockpileCalculationService
{
    private readonly WalletDbContext _db;
    private readonly IStockpileService _stockpiles;

    public StockpileCalculationService(WalletDbContext db, IStockpileService stockpiles)
    {
        _db = db;
        _stockpiles = stockpiles;
    }

    public async Task<IReadOnlyList<StockpileCalculationLine>> CalculateAsync(
        OwnerType ownerType,
        int ownerId,
        bool includeArchived = false,
        IReadOnlyList<MarketOrder>? orders = null,
        bool ordersAvailable = false,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var targets = await _stockpiles.GetAllAsync(ownerType, ownerId, includeArchived, ct);
        if (targets.Count == 0)
        {
            return Array.Empty<StockpileCalculationLine>();
        }

        // Physische Basis: jüngster abgeschlossener Snapshot des Owners (Owner-Isolation).
        var snapshot = await _db.HoldingSnapshots
            .Include(s => s.Items)
            .Where(s => s.OwnerType == ownerType
                        && s.OwnerId == ownerId
                        && s.SyncRun.Status == "completed")
            .OrderByDescending(s => s.SyncedAt)
            .FirstOrDefaultAsync(ct);

        var assets = new List<StockpileCalculator.AssetLine>();
        if (snapshot is not null)
        {
            foreach (var item in snapshot.Items)
            {
                assets.Add(new StockpileCalculator.AssetLine(item.ItemId, item.TypeId, item.LocationId, item.Quantity));
            }
        }

        var orderLines = new List<StockpileCalculator.OrderLine>();
        if (orders is not null)
        {
            foreach (var order in orders.Where(o => o.VolumeRemain > 0))
            {
                orderLines.Add(new StockpileCalculator.OrderLine(order.TypeId, order.LocationId, order.IsBuyOrder, order.VolumeRemain));
            }
        }

        return StockpileCalculator.Calculate(
            targets,
            assets,
            orderLines,
            physicalSourceAvailable: snapshot is not null,
            ordersSourceAvailable: ordersAvailable);
    }
}