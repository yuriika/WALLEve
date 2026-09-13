using Microsoft.EntityFrameworkCore;
using WALLEve.Data;
using WALLEve.Models.Esi.Markets;
using WALLEve.Models.Holdings;
using WALLEve.Models.Stockpiles;
using WALLEve.Services.Sde.Interfaces;
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
    private readonly WALLEve.Services.Market.Interfaces.IHubSelectionService _hubSelection;
    private readonly ISdeUniverseService _sdeUniverse;

    public StockpileOverviewService(
        WalletDbContext db,
        IStockpileCalculationService calculation,
        WALLEve.Services.Market.Interfaces.IHubSelectionService hubSelection,
        ISdeUniverseService sdeUniverse)
    {
        _db = db;
        _calculation = calculation;
        _hubSelection = hubSelection;
        _sdeUniverse = sdeUniverse;
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

    public async Task<StockpileMarketContext> GetShortageMarketContextAsync(
        OwnerType ownerType,
        int ownerId,
        bool includeArchived = false,
        IReadOnlyList<MarketOrder>? orders = null,
        bool ordersAvailable = false,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var lines = await _calculation.CalculateAsync(
            ownerType,
            ownerId,
            includeArchived,
            orders,
            ordersAvailable,
            ct);

        // Vergleichsmarkt: Quelle für die Bewertungs-Quotes. Kein konfigurierter
        // Vergleichsmarkt bleibt ein sichtbarer „unbekannt"-Fall (nie 0 ISK).
        var comparison = await _hubSelection.GetComparisonMarketAsync(ct);
        if (comparison is null)
        {
            return new StockpileMarketContext();
        }

        // Nur TypeIds mit belastbarer Fehlmenge werden bewertet; Zeilen ohne
        // ableitbare Fehlmenge sind keine Bewertungsobjekte (#43).
        var shortageTypeIds = lines
            .Where(l => l.Shortage is > 0)
            .Select(l => l.TypeId)
            .Distinct()
            .ToList();

        var quotes = new Dictionary<int, StockpileMarketQuote>();
        if (shortageTypeIds.Count > 0)
        {
            // Neuester Snapshot je Type IM Vergleichsmarkt: Quotes sind an die
            // Region des Vergleichsmarkts gebunden, nie ein fremder Regionspreis.
            var snapshots = await _db.MarketSnapshots
                .AsNoTracking()
                .Where(s => s.RegionId == comparison.RegionId && shortageTypeIds.Contains(s.TypeId))
                .GroupBy(s => s.TypeId)
                .Select(g => g.OrderByDescending(s => s.Timestamp).First())
                .ToListAsync(ct);

            foreach (var snapshot in snapshots)
            {
                quotes[snapshot.TypeId] = new StockpileMarketQuote
                {
                    TypeId = snapshot.TypeId,
                    BestSellPrice = snapshot.BestSellPrice,
                    BestBuyPrice = snapshot.BestBuyPrice,
                    QuoteTimestamp = snapshot.Timestamp
                };
            }
        }

        // Nächstgelegener aktiver Hub je auflösbarer Ziel-Location: exakte
        // Sprungdistanz vom System der Location. Container/Strukturen ohne
        // auflösbares System bleiben unbekannt (kein erfundener 0-Sprung).
        var hubsByLocation = new Dictionary<long, StockpileLocationHub>();
        foreach (var locationId in lines
                     .Where(l => l.Shortage is > 0 && l.LocationId.HasValue)
                     .Select(l => l.LocationId!.Value)
                     .Distinct())
        {
            var systemId = await _sdeUniverse.GetSolarSystemIdForLocationAsync(locationId);
            if (!systemId.HasValue)
            {
                continue;
            }

            var selection = await _hubSelection.SelectNearestActiveHubAsync(systemId.Value, ct);
            hubsByLocation[locationId] = new StockpileLocationHub
            {
                SystemResolved = true,
                GraphAvailable = selection.GraphAvailable,
                HubName = selection.Selected?.Name,
                JumpDistance = selection.Selected?.JumpDistance
            };
        }

        return new StockpileMarketContext
        {
            ComparisonMarketName = comparison.Name,
            ComparisonMarketRegionId = comparison.RegionId,
            Quotes = quotes,
            HubsByLocation = hubsByLocation
        };
    }
}
