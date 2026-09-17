using Microsoft.EntityFrameworkCore;
using WALLEve.Data;
using WALLEve.Models.Esi.Markets;
using WALLEve.Models.Holdings;
using WALLEve.Models.Industry;
using WALLEve.Services.Industry.Interfaces;
using WALLEve.Services.Stockpiles;

namespace WALLEve.Services.Industry;

/// <summary>
/// Orchestriert den Bedarfs-/Holdings-Abgleich (Issue #62): lädt den jüngsten
/// abgeschlossenen Holding-Snapshot des Characters als physische Basis,
/// wandelt Orders in reine Order-Zeilen und delegiert die deterministische
/// Ableitung an <see cref="MaterialDemandMatcher"/>. Fehlender Snapshot
/// ("physical-source-missing") und fehlende/unkomplette Orders
/// ("orders-source-missing") werden als Partial-Zustand durchgereicht.
/// Keine Live-ESI-Aufrufe — Orders kommen vom Aufrufer.
/// </summary>
public class MaterialDemandService : IMaterialDemandService
{
    private readonly WalletDbContext _db;

    public MaterialDemandService(WalletDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<MaterialDemandMatch>> MatchDemandAsync(
        int characterId,
        IReadOnlyList<MaterialDemandRequest> requests,
        IReadOnlyList<MarketOrder>? orders = null,
        bool ordersAvailable = false,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (requests.Count == 0)
        {
            return Array.Empty<MaterialDemandMatch>();
        }

        // Physische Basis: jüngster abgeschlossener Snapshot des Owners (Owner-Isolation).
        var snapshot = await _db.HoldingSnapshots
            .Include(s => s.Items)
            .Where(s => s.OwnerType == OwnerType.Character
                        && s.OwnerId == characterId
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

        return MaterialDemandMatcher.Match(
            requests,
            assets,
            orderLines,
            physicalSourceAvailable: snapshot is not null,
            ordersSourceAvailable: ordersAvailable);
    }
}