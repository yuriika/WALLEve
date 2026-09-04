using WALLEve.Models.Esi.Character;

namespace WALLEve.Services.Market.Interfaces;

public interface IInventoryService
{
    Task<List<InventoryItem>> GetInventoryAsync(int characterId);
    Task<PortfolioOverview> GetPortfolioOverviewAsync(int characterId);
    Task<List<InventoryItem>> GetPrioritizedItemsAsync(int characterId, InventorySortMode sortMode = InventorySortMode.Opportunity);
}

public class InventoryItem
{
    public int TypeId { get; set; }
    public string TypeName { get; set; } = string.Empty;
    public int TotalQuantity { get; set; }
    public string PrimaryLocation { get; set; } = string.Empty;
    public string LocationFlag { get; set; } = string.Empty;
    public double? BestBuyPrice { get; set; }
    public double? BestSellPrice { get; set; }
    public double? AveragePrice { get; set; }
    public double? SpreadPercent { get; set; }
    public double? CostBasisPerUnit { get; set; }
    public bool IsMined { get; set; }
    public bool IsManufactured { get; set; }
    public double CurrentMarketValue => (BestSellPrice ?? 0) * TotalQuantity;
    public double? TotalCostBasis => CostBasisPerUnit.HasValue ? CostBasisPerUnit.Value * TotalQuantity : null;
    public double? UnrealizedProfit => TotalCostBasis.HasValue ? CurrentMarketValue - TotalCostBasis.Value : null;
    public double? UnrealizedRoiPercent => TotalCostBasis.HasValue && TotalCostBasis.Value > 0 ? (UnrealizedProfit!.Value / TotalCostBasis.Value) * 100 : null;
    public double? EstimatedNetProceeds { get; set; }
    public double? NetProfitAfterFees { get; set; }
    public double? NetRoiAfterFees { get; set; }
    public double? OpportunityScore { get; set; }
    public string Recommendation { get; set; } = string.Empty;
    public string RecommendationReason { get; set; } = string.Empty;
    public List<CharacterAsset> RawAssets { get; set; } = new();
}

public class PortfolioOverview
{
    public int TotalItemTypes { get; set; }
    public long TotalQuantity { get; set; }
    public double TotalMarketValue { get; set; }
    public double? TotalCostBasis { get; set; }
    public int ItemCountWithCostBasis { get; set; }
    public int SellRecommendations { get; set; }
    public int HoldRecommendations { get; set; }
    public int WatchRecommendations { get; set; }
}

public enum InventorySortMode
{
    Opportunity,
    MarketValue,
    Profit,
    Roi,
    Quantity,
    Name
}