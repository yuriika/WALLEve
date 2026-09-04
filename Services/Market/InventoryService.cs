using Microsoft.EntityFrameworkCore;
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
    private readonly ILogger<InventoryService> _logger;

    public InventoryService(
        IEsiApiService esiApi,
        ISdeUniverseService sde,
        IFeeCalculatorService feeCalculator,
        WalletDbContext db,
        ILogger<InventoryService> logger)
    {
        _esiApi = esiApi;
        _sde = sde;
        _feeCalculator = feeCalculator;
        _db = db;
        _logger = logger;
    }

    public async Task<List<InventoryItem>> GetInventoryAsync(int characterId)
    {
        var assets = await _esiApi.GetCharacterAssetsAsync(characterId);
        if (!assets.Any()) return new List<InventoryItem>();

        var skills = await _esiApi.GetCharacterSkillsAsync();
        var marketPrices = await _esiApi.GetMarketPricesAsync();
        var priceLookup = marketPrices?.ToDictionary(p => p.TypeId, p => p) ?? new();
        var sdeAvailable = await _sde.IsDatabaseAvailableAsync();

        var grouped = assets.GroupBy(a => a.TypeId).ToList();
        var items = new List<InventoryItem>();

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
            if (priceLookup.TryGetValue(typeId, out var mp))
                avgPrice = mp.AveragePrice ?? mp.AdjustedPrice;

            var latestSnapshot = await _db.MarketSnapshots
                .Where(s => s.TypeId == typeId)
                .OrderByDescending(s => s.Timestamp)
                .FirstOrDefaultAsync();
            if (latestSnapshot != null)
            {
                bestBuy = latestSnapshot.BestBuyPrice;
                bestSell = latestSnapshot.BestSellPrice;
            }

            double? spread = null;
            if (bestBuy.HasValue && bestSell.HasValue && bestBuy.Value > 0)
                spread = ((bestSell.Value - bestBuy.Value) / bestBuy.Value) * 100;

            var costBasis = await CalculateCostBasisAsync(characterId, typeId);

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

            var (score, rec, reason) = CalculateOpportunityScore(spread, netRoi, avgPrice, bestSell, bestBuy);

            items.Add(new InventoryItem
            {
                TypeId = typeId, TypeName = typeName, TotalQuantity = totalQty,
                PrimaryLocation = primaryAsset.LocationType, LocationFlag = primaryAsset.LocationFlag,
                BestBuyPrice = bestBuy, BestSellPrice = bestSell, AveragePrice = avgPrice, SpreadPercent = spread,
                CostBasisPerUnit = costBasis,
                EstimatedNetProceeds = estimatedNetProceeds, NetProfitAfterFees = netProfit, NetRoiAfterFees = netRoi,
                OpportunityScore = score, Recommendation = rec, RecommendationReason = reason,
                RawAssets = group.ToList()
            });
        }

        _logger.LogInformation("Inventory: {Count} item types, {TotalQty} total units",
            items.Count, items.Sum(i => i.TotalQuantity));
        return items;
    }

    public async Task<PortfolioOverview> GetPortfolioOverviewAsync(int characterId)
    {
        var items = await GetInventoryAsync(characterId);
        return new PortfolioOverview
        {
            TotalItemTypes = items.Count, TotalQuantity = items.Sum(i => i.TotalQuantity),
            TotalMarketValue = items.Sum(i => i.CurrentMarketValue),
            TotalCostBasis = items.Any(i => i.TotalCostBasis.HasValue) ? items.Sum(i => i.TotalCostBasis ?? 0) : null,
            ItemCountWithCostBasis = items.Count(i => i.CostBasisPerUnit.HasValue),
            SellRecommendations = items.Count(i => i.Recommendation == "sell"),
            HoldRecommendations = items.Count(i => i.Recommendation == "hold"),
            WatchRecommendations = items.Count(i => i.Recommendation == "watch")
        };
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
                .Where(t => t.TypeId == typeId && t.IsBuy == true)
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
        double? spread, double? netRoi, double? avgPrice, double? bestSell, double? bestBuy)
    {
        if (!bestSell.HasValue || !bestBuy.HasValue)
            return (0, "watch", "Keine aktuellen Marktdaten.");
        double score = 0;
        if (spread.HasValue) score += spread.Value > 10 ? 30 : spread.Value > 5 ? 20 : spread.Value > 2 ? 10 : 5;
        if (netRoi.HasValue) score += netRoi.Value > 20 ? 40 : netRoi.Value > 10 ? 30 : netRoi.Value > 5 ? 20 : netRoi.Value > 0 ? 10 : netRoi.Value < -10 ? -20 : 0;
        if (spread.HasValue) score += spread.Value < 2 ? 20 : spread.Value < 5 ? 10 : 5;
        if (avgPrice.HasValue && bestSell.HasValue)
        {
            var dev = Math.Abs(bestSell.Value - avgPrice.Value) / avgPrice.Value * 100;
            score += dev < 5 ? 10 : dev < 15 ? 5 : 0;
        }
        score = Math.Clamp(score, 0, 100);
        if (score >= 60 && netRoi > 0) return (score, "sell", $"Gute Marge ({netRoi:F1}% ROI) bei ausreichender Liquidität. Verkauf empfohlen.");
        if (score >= 40 && netRoi > 0) return (score, "watch", $"Mäßige Marge ({netRoi:F1}% ROI). Beobachten oder auf besseren Preis warten.");
        if (netRoi < 0) return (score, "hold", $"Im Minus ({netRoi:F1}% ROI). Halten oder nur bei Kapitalbedarf verkaufen.");
        return (score, "hold", "Keine klare Opportunität. Bestand halten.");
    }
}