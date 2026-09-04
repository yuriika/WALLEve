using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using WALLEve.Data;
using WALLEve.Models.Esi.Character;
using WALLEve.Services.Esi.Interfaces;
using WALLEve.Services.Market.Interfaces;
using WALLEve.Services.Sde.Interfaces;

namespace WALLEve.Services.Market;

public class InventoryService : IInventoryService
{
    private readonly IEsiApiService _esiApi;
    private readonly ISdeUniverseService _sde;
    private readonly IFeeCalculatorService _feeCalculator;
    private readonly WalletDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly ILogger<InventoryService> _logger;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);
    private const string InventoryCachePrefix = "inventory_";
    private const string OverviewCachePrefix = "overview_";

    public InventoryService(
        IEsiApiService esiApi,
        ISdeUniverseService sde,
        IFeeCalculatorService feeCalculator,
        WalletDbContext db,
        IMemoryCache cache,
        ILogger<InventoryService> logger)
    {
        _esiApi = esiApi;
        _sde = sde;
        _feeCalculator = feeCalculator;
        _db = db;
        _cache = cache;
        _logger = logger;
    }

    public async Task<List<InventoryItem>> GetInventoryAsync(int characterId)
    {
        var cacheKey = InventoryCachePrefix + characterId;
        if (_cache.TryGetValue<List<InventoryItem>>(cacheKey, out var cached))
            return cached!;

        var items = await LoadInventoryAsync(characterId);

        _cache.Set(cacheKey, items, CacheDuration);
        return items;
    }

    public async Task<PortfolioOverview> GetPortfolioOverviewAsync(int characterId)
    {
        var cacheKey = OverviewCachePrefix + characterId;
        if (_cache.TryGetValue<PortfolioOverview>(cacheKey, out var cached))
            return cached!;

        var items = await GetInventoryAsync(characterId);
        var overview = new PortfolioOverview
        {
            TotalItemTypes = items.Count, TotalQuantity = items.Sum(i => i.TotalQuantity),
            TotalMarketValue = items.Sum(i => i.CurrentMarketValue),
            TotalCostBasis = items.Any(i => i.TotalCostBasis.HasValue) ? items.Sum(i => i.TotalCostBasis ?? 0) : null,
            ItemCountWithCostBasis = items.Count(i => i.CostBasisPerUnit.HasValue),
            SellRecommendations = items.Count(i => i.Recommendation == "sell"),
            HoldRecommendations = items.Count(i => i.Recommendation == "hold"),
            WatchRecommendations = items.Count(i => i.Recommendation == "watch")
        };

        _cache.Set(cacheKey, overview, CacheDuration);
        return overview;
    }

    private async Task<List<InventoryItem>> LoadInventoryAsync(int characterId)
    {
        var assets = await _esiApi.GetCharacterAssetsAsync(characterId);
        if (!assets.Any()) return new List<InventoryItem>();

        var skills = await _esiApi.GetCharacterSkillsAsync();
        var marketPrices = await _esiApi.GetMarketPricesAsync();
        var priceLookup = marketPrices?.ToDictionary(p => p.TypeId, p => p) ?? new();
        var sdeAvailable = await _sde.IsDatabaseAvailableAsync();

        var grouped = assets.GroupBy(a => a.TypeId).ToList();
        var items = new List<InventoryItem>();

        // Batch-load all snapshots for items in inventory
        var typeIds = grouped.Select(g => g.Key).ToList();
        var latestSnapshots = await _db.MarketSnapshots
            .Where(s => typeIds.Contains(s.TypeId))
            .GroupBy(s => s.TypeId)
            .Select(g => g.OrderByDescending(s => s.Timestamp).First())
            .ToDictionaryAsync(s => s.TypeId, s => s);

        foreach (var group in grouped)
        {
            var typeId = group.Key;
            var totalQty = group.Sum(a => a.Quantity);
            var primaryAsset = group.OrderByDescending(a => a.Quantity).First();

            string typeName = $"Type {typeId}";
            if (sdeAvailable)
            {
                var name = await _sde.GetTypeNameAsync(typeId);
                if (name != null) typeName = name;
            }

            double? bestBuy = null, bestSell = null, avgPrice = null;
            string? buySource = null, sellSource = null;

            // 1. Try real snapshot data (best quality)
            if (latestSnapshots.TryGetValue(typeId, out var snapshot))
            {
                bestBuy = snapshot.BestBuyPrice;
                bestSell = snapshot.BestSellPrice;
                buySource = "snapshot";
                sellSource = "snapshot";
            }

            // 2. Fallback: ESI MarketPrices (adjusted_price = global reference price)
            if (priceLookup.TryGetValue(typeId, out var mp))
            {
                avgPrice = mp.AveragePrice ?? mp.AdjustedPrice;
                if (!bestSell.HasValue && mp.AdjustedPrice.HasValue)
                {
                    bestSell = mp.AdjustedPrice;
                    sellSource = "reference";
                }
                if (!bestBuy.HasValue && mp.AdjustedPrice.HasValue)
                {
                    // Estimate buy price as ~95% of adjusted sell price (rough spread)
                    bestBuy = mp.AdjustedPrice * 0.95;
                    buySource = "reference";
                }
            }

            // 3. Worst case: no price data at all
            if (!bestSell.HasValue)
            {
                items.Add(new InventoryItem
                {
                    TypeId = typeId, TypeName = typeName, TotalQuantity = totalQty,
                    PrimaryLocation = primaryAsset.LocationType, LocationFlag = primaryAsset.LocationFlag,
                    BestBuyPrice = null, BestSellPrice = null, AveragePrice = avgPrice,
                    OpportunityScore = 0, Recommendation = "watch",
                    RecommendationReason = "Keine Marktpreise verfügbar (weder ESI-Referenz noch Snapshot).",
                    RawAssets = group.ToList()
                });
                continue;
            }

            double? spread = null;
            if (bestBuy.HasValue && bestSell.HasValue && bestBuy.Value > 0)
                spread = ((bestSell.Value - bestBuy.Value) / bestBuy.Value) * 100;

            // Cost Basis aus Wallet-Transaktionen
            var costBasis = await CalculateCostBasisAsync(characterId, typeId);

            // Fee-Berechnung
            double? estimatedNetProceeds = null, netProfit = null, netRoi = null;
            if (bestSell.HasValue)
            {
                var sellResult = _feeCalculator.CalculateSellProceeds(bestSell.Value, totalQty, skills);
                estimatedNetProceeds = sellResult.NetAmount;
                if (costBasis.HasValue)
                {
                    netProfit = estimatedNetProceeds - (costBasis.Value * totalQty);
                    var totalCost = costBasis.Value * totalQty;
                    netRoi = totalCost > 0 ? (netProfit.Value / totalCost) * 100 : 0;
                }
            }

            var (score, rec, reason) = CalculateOpportunityScore(spread, netRoi, avgPrice, bestSell.Value, bestBuy);

            items.Add(new InventoryItem
            {
                TypeId = typeId, TypeName = typeName, TotalQuantity = totalQty,
                PrimaryLocation = primaryAsset.LocationType, LocationFlag = primaryAsset.LocationFlag,
                BestBuyPrice = bestBuy, BestSellPrice = bestSell, AveragePrice = avgPrice, SpreadPercent = spread,
                CostBasisPerUnit = costBasis,
                EstimatedNetProceeds = estimatedNetProceeds, NetProfitAfterFees = netProfit, NetRoiAfterFees = netRoi,
                OpportunityScore = score, Recommendation = rec, RecommendationReason = reason,
                BuyPriceSource = buySource, SellPriceSource = sellSource,
                RawAssets = group.ToList()
            });
        }

        _logger.LogInformation("Inventory: {Count} item types, {TotalQty} total units, {WithPrice} with prices",
            items.Count, items.Sum(i => i.TotalQuantity), items.Count(i => i.BestSellPrice.HasValue));
        return items;
    }

    public async Task<List<InventoryItem>> GetPrioritizedItemsAsync(int characterId, InventorySortMode sortMode = InventorySortMode.Opportunity)
    {
        var items = await GetInventoryAsync(characterId);
        return sortMode switch
        {
            InventorySortMode.Opportunity => items.OrderByDescending(i => i.OpportunityScore ?? 0).ThenByDescending(i => i.CurrentMarketValue).ToList(),
            InventorySortMode.MarketValue => items.OrderByDescending(i => i.CurrentMarketValue).ToList(),
            InventorySortMode.Profit => items.OrderByDescending(i => i.NetProfitAfterFees ?? 0).ToList(),
            InventorySortMode.Roi => items.OrderByDescending(i => i.NetRoiAfterFees ?? 0).ToList(),
            InventorySortMode.Quantity => items.OrderByDescending(i => i.TotalQuantity).ToList(),
            InventorySortMode.Name => items.OrderBy(i => i.TypeName).ToList(),
            _ => items.OrderByDescending(i => i.OpportunityScore ?? 0).ToList()
        };
    }

    private async Task<double?> CalculateCostBasisAsync(int characterId, int typeId)
    {
        try
        {
            var transactions = await _esiApi.GetAllWalletTransactionsPagesAsync(characterId);
            if (transactions == null || !transactions.Any()) return null;
            var buyTransactions = transactions
                .Where(t => t.TypeId == typeId && t.IsBuy)
                .OrderByDescending(t => t.Date).Take(10).ToList();
            if (!buyTransactions.Any()) return null;
            var totalQty = buyTransactions.Sum(t => (double)t.Quantity);
            var totalCost = buyTransactions.Sum(t => t.UnitPrice * t.Quantity);
            return totalQty > 0 ? totalCost / totalQty : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not calculate cost basis for TypeId {TypeId}", typeId);
            return null;
        }
    }

    private static (double Score, string Rec, string Reason) CalculateOpportunityScore(
        double? spread, double? netRoi, double? avgPrice, double bestSell, double? bestBuy)
    {
        if (!bestBuy.HasValue)
            return (0, "watch", "Keine aktuellen Marktdaten.");

        double score = 0;
        if (spread.HasValue) score += spread.Value > 10 ? 30 : spread.Value > 5 ? 20 : spread.Value > 2 ? 10 : 5;
        if (netRoi.HasValue) score += netRoi.Value > 20 ? 40 : netRoi.Value > 10 ? 30 : netRoi.Value > 5 ? 20 : netRoi.Value > 0 ? 10 : netRoi.Value < -10 ? -20 : 0;
        if (spread.HasValue) score += spread.Value < 2 ? 20 : spread.Value < 5 ? 10 : 5;
        if (avgPrice.HasValue)
        {
            var dev = Math.Abs(bestSell - avgPrice.Value) / avgPrice.Value * 100;
            score += dev < 5 ? 10 : dev < 15 ? 5 : 0;
        }
        score = Math.Clamp(score, 0, 100);
        if (score >= 60 && netRoi > 0) return (score, "sell", $"Gute Marge ({netRoi:F1}% ROI) bei ausreichender Liquidität. Verkauf empfohlen.");
        if (score >= 40 && netRoi > 0) return (score, "watch", $"Mäßige Marge ({netRoi:F1}% ROI). Beobachten oder auf besseren Preis warten.");
        if (netRoi < 0) return (score, "hold", $"Im Minus ({netRoi:F1}% ROI). Halten oder nur bei Kapitalbedarf verkaufen.");
        return (score, "hold", "Keine klare Opportunität. Bestand halten.");
    }
}