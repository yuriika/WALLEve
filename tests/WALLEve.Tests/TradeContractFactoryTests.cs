using WALLEve.Models.Trading;

namespace WALLEve.Tests;

/// <summary>
/// Issue #37, Akzeptanzkriterium 2: Fehlende Pflichtfelder/Quellen ergeben einen
/// NICHT ausführbaren Vertrag (IsActionable = false mit Begründung) statt
/// scheinbar gültiger Default-Nullwerte. Die Factory ist reine C#-Logik ohne DB.
/// </summary>
public class TradeContractFactoryTests
{
    private const string Algorithm = "inventory-sell-v1";
    private static readonly DateTime Now = new(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void InventorySell_Create_AllInputsPresent_IsActionable()
    {
        var contract = TradeContractFactory.CreateInventorySell(
            tradingOpportunityId: 1, characterId: 90073315, typeId: 34, algorithmVersion: Algorithm,
            createdAt: Now, unitCostBasis: 90.5m, sellPricePerUnit: 120.25m, quantity: 500,
            sellLocationId: 60008494, sellLocationLabel: "Jita IV - Moon 4", brokerFee: 2500.5m, salesTax: 1200m,
            estimatedNetProceeds: 58300m, breakEvenPrice: 95.75m, marketSnapshotId: 42, costBasisSource: "transaction",
            estimatedProfit: 8300m, requiredCapital: 45250m, netRoiPercent: 18.3m,
            evidence: "Ortsgebunden (Jita IV - Moon 4): 500 × Item.");

        Assert.True(contract.IsActionable);
        Assert.Null(contract.NotActionableReason);
        Assert.False(contract.IsLegacy);
        Assert.Equal(TradeKind.InventorySell, contract.Kind);
    }

    [Theory]
    [InlineData(0d, 120.25d, "UnitCostBasis")]
    [InlineData(90.5d, 0d, "SellPricePerUnit")]
    public void InventorySell_Create_MissingPrice_NotActionable(double unitCostBasis, double sellPrice, string expectedMissing)
    {
        var contract = TradeContractFactory.CreateInventorySell(
            1, 90073315, 34, Algorithm, Now, (decimal)unitCostBasis, (decimal)sellPrice, 500,
            60008494, "Jita IV - Moon 4", 0m, 0m, 0m, 0m, 42, "transaction", 0m, 0m, 0m,
            "Evidenz");

        Assert.False(contract.IsActionable);
        Assert.NotNull(contract.NotActionableReason);
        Assert.Contains(expectedMissing, contract.NotActionableReason);
    }

    [Fact]
    public void InventorySell_Create_MissingSnapshotSource_NotActionable()
    {
        var contract = TradeContractFactory.CreateInventorySell(
            1, 90073315, 34, Algorithm, Now, 90.5m, 120.25m, 500,
            60008494, "Jita IV - Moon 4", 0m, 0m, 0m, 0m, marketSnapshotId: null, costBasisSource: "transaction",
            0m, 0m, 0m, "Evidenz");

        Assert.False(contract.IsActionable);
        Assert.Contains("MarketSnapshotId", contract.NotActionableReason);
    }

    [Fact]
    public void InventorySell_Create_MissingCostBasisSource_NotActionable()
    {
        var contract = TradeContractFactory.CreateInventorySell(
            1, 90073315, 34, Algorithm, Now, 90.5m, 120.25m, 500,
            60008494, "Jita IV - Moon 4", 0m, 0m, 0m, 0m, 42, costBasisSource: null,
            0m, 0m, 0m, "Evidenz");

        Assert.False(contract.IsActionable);
        Assert.Contains("CostBasisSource", contract.NotActionableReason);
    }

    [Fact]
    public void OrderAdjustment_Create_Complete_IsActionable()
    {
        var contract = TradeContractFactory.CreateOrderAdjustment(
            1, 90073315, 34, Algorithm, Now, orderId: 5587, quantity: 250, locationId: 60008494,
            currentPricePerUnit: 100m, suggestedPricePerUnit: 110m, marketSnapshotId: 42,
            brokerRatePercent: 3.5m, relistDiscountPercent: 1.2m, modifyFee: 8750m,
            estimatedExtraNetProceeds: 2193m, estimatedProfit: 2193m, requiredCapital: 25000m,
            netRoiPercent: 8.8m, evidence: "Order 5587: 110 ISK statt 100 ISK.");

        Assert.True(contract.IsActionable);
        Assert.Null(contract.NotActionableReason);
    }

    [Fact]
    public void OrderAdjustment_Create_MissingOrderId_NotActionable()
    {
        var contract = TradeContractFactory.CreateOrderAdjustment(
            1, 90073315, 34, Algorithm, Now, orderId: 0, quantity: 250, locationId: 60008494,
            currentPricePerUnit: 100m, suggestedPricePerUnit: 110m, marketSnapshotId: 42,
            brokerRatePercent: 3.5m, relistDiscountPercent: 1.2m, modifyFee: 8750m,
            estimatedExtraNetProceeds: 2193m, estimatedProfit: 2193m, requiredCapital: 25000m,
            netRoiPercent: 8.8m, evidence: "Evidenz");

        Assert.False(contract.IsActionable);
        Assert.Contains("OrderId", contract.NotActionableReason);
    }

    [Fact]
    public void StationTrade_Create_MissingBuyPrice_NotActionable()
    {
        var contract = TradeContractFactory.CreateStationTrade(
            1, 90073315, 34, Algorithm, Now, quantity: 100,
            buyLocationId: 60008494, sellLocationId: 60003760,
            buyRegionId: 10000002, sellRegionId: 10000002,
            buyPricePerUnit: 0m, sellPricePerUnit: 130m,
            buyMarketSnapshotId: 42, sellMarketSnapshotId: 43,
            brokerRatePercent: 3.5m, salesTaxPercent: 0m, jumpDistance: 1,
            estimatedNetProceeds: 12300m, estimatedProfit: 3100m, requiredCapital: 9000m,
            netRoiPercent: 34.4m, evidence: "Jita → Perimeter.");

        Assert.False(contract.IsActionable);
        Assert.Contains("BuyPricePerUnit", contract.NotActionableReason);
    }

    [Fact]
    public void RouteTrade_Create_MissingRoute_NotActionable()
    {
        var contract = TradeContractFactory.CreateRouteTrade(
            1, 90073315, 34, Algorithm, Now, quantity: 100,
            startSystemId: 30000142, endSystemId: 30005310, jumpCount: 12,
            buyLocationId: 60008494, sellLocationId: 61001254,
            buyPricePerUnit: 80m, sellPricePerUnit: 150m,
            buyMarketSnapshotId: 42, sellMarketSnapshotId: 44,
            brokerRatePercent: 3.5m, salesTaxPercent: 0m,
            routeJson: "", estimatedNetProceeds: 6900m, estimatedProfit: 6900m,
            requiredCapital: 8000m, netRoiPercent: 86.3m, evidence: "Route Jita → Amarr.");

        Assert.False(contract.IsActionable);
        Assert.Contains("RouteJson", contract.NotActionableReason);
    }
}