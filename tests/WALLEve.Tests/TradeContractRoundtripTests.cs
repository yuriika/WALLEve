using Microsoft.EntityFrameworkCore;
using WALLEve.Models.Trading;

namespace WALLEve.Tests;

/// <summary>
/// Issue #37, Akzeptanzkriterium 1: Der Roundtrip (Speichern → Laden) erhält
/// decimal-Beträge und Evidence ohne Informationsverlust. Verträge sind
/// unveränderlich — nach dem Laden müssen alle Eingaben/Quellen exakt
/// wiederhergestellt sein (inkl. Nachkommastellen des decimal-Typs).
/// </summary>
public class TradeContractRoundtripTests
{
    private static readonly DateTime Now = new(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc);
    private const string EvidenceText = "Ortsgebunden (Jita IV - Moon 4): 500 × Item — Verkauf bei 120,25 ISK netto 58.300 ISK (ROI 18,3%).";

    [Fact]
    public async Task InventorySell_Roundtrip_PreservesDecimalsAndEvidence()
    {
        await using var db = TestDb.Create();

        var original = TradeContractFactory.CreateInventorySell(
            tradingOpportunityId: 1, characterId: 90073315, typeId: 34, algorithmVersion: "inventory-sell-v1",
            createdAt: Now, unitCostBasis: 90.123456789m, sellPricePerUnit: 120.987654321m, quantity: 500,
            sellLocationId: 60008494, sellLocationLabel: "Jita IV - Moon 4", brokerFee: 2500.12345m, salesTax: 1200.98765m,
            estimatedNetProceeds: 58300.123456m, breakEvenPrice: 95.123456789m, marketSnapshotId: 42, costBasisSource: "transaction",
            estimatedProfit: 8300.123456m, requiredCapital: 45250.987654m, netRoiPercent: 18.3456789m,
            evidence: EvidenceText);

        db.TradeContracts.Add(original);
        await db.SaveChangesAsync();

        var copy = await db.TradeContracts.SingleAsync(c => c.TradingOpportunityId == 1);
        Assert.Equal(original.Id, copy.Id);
        Assert.Equal(original.TradingOpportunityId, copy.TradingOpportunityId);
        Assert.Equal(original.CharacterId, copy.CharacterId);
        Assert.Equal(original.TypeId, copy.TypeId);
        Assert.Equal(original.AlgorithmVersion, copy.AlgorithmVersion);
        Assert.Equal(original.CreatedAt, copy.CreatedAt);
        Assert.Equal(TradeKind.InventorySell, copy.Kind);
        Assert.True(copy.IsActionable);
        Assert.False(copy.IsLegacy);

        // Decimal-Beträge ohne Informationsverlust (volle Nachkommastellen).
        Assert.Equal(90.123456789m, copy.UnitCostBasis);
        Assert.Equal(120.987654321m, copy.SellPricePerUnit);
        Assert.Equal(2500.12345m, copy.BrokerFee);
        Assert.Equal(1200.98765m, copy.SalesTax);
        Assert.Equal(58300.123456m, copy.EstimatedNetProceeds);
        Assert.Equal(95.123456789m, copy.BreakEvenPrice);
        Assert.Equal(8300.123456m, copy.EstimatedProfit);
        Assert.Equal(45250.987654m, copy.RequiredCapital);
        Assert.Equal(18.3456789m, copy.NetRoiPercent);

        // Quelle und Evidenz exakt erhalten.
        Assert.Equal(42, copy.MarketSnapshotId);
        Assert.Equal("transaction", copy.CostBasisSource);
        Assert.Equal(500, copy.Quantity);
        Assert.Equal(60008494, copy.SellLocationId);
        Assert.Equal("Jita IV - Moon 4", copy.SellLocationLabel);
        Assert.Equal(EvidenceText, copy.Evidence);
    }

    [Fact]
    public async Task RouteTrade_Roundtrip_PreservesRouteJsonAndDecimals()
    {
        await using var db = TestDb.Create();

        const string routeJson = "{\"systems\":[30000142,30000143,30005310],\"highsec\":2,\"lowsec\":1,\"nullsec\":0}";
        var original = TradeContractFactory.CreateRouteTrade(
            2, 90073315, 34, "route-trade-v1", Now, quantity: 100,
            startSystemId: 30000142, endSystemId: 30005310, jumpCount: 3,
            buyLocationId: 60008494, sellLocationId: 61001254,
            buyPricePerUnit: 80.5m, sellPricePerUnit: 150.25m,
            buyMarketSnapshotId: 42, sellMarketSnapshotId: 44,
            brokerRatePercent: 3.5m, salesTaxPercent: 0m,
            routeJson, estimatedNetProceeds: 6900.75m, estimatedProfit: 6900.75m,
            requiredCapital: 8050m, netRoiPercent: 85.7m,
            evidence: "Route Jita → Amarr.");

        db.TradeContracts.Add(original);
        await db.SaveChangesAsync();

        var copy = await db.TradeContracts.SingleAsync(c => c.TradingOpportunityId == 2);
        Assert.Equal(TradeKind.RouteTrade, copy.Kind);
        Assert.True(copy.IsActionable);
        Assert.Equal(80.5m, copy.BuyPricePerUnit);
        Assert.Equal(150.25m, copy.SellPricePerUnit);
        Assert.Equal(6900.75m, copy.EstimatedNetProceeds);
        Assert.Equal(3, copy.JumpCount);
        Assert.Equal(routeJson, copy.RouteJson);
        Assert.Equal("Route Jita → Amarr.", copy.Evidence);
    }

    [Fact]
    public async Task NonActionableContract_Roundtrip_KeepsReason()
    {
        await using var db = TestDb.Create();

        var original = TradeContractFactory.CreateInventorySell(
            3, 90073315, 34, "inventory-sell-v1", Now, unitCostBasis: 90m, sellPricePerUnit: 120m, quantity: 500,
            sellLocationId: 60008494, sellLocationLabel: "Jita IV - Moon 4", brokerFee: 0m, salesTax: 0m,
            estimatedNetProceeds: 0m, breakEvenPrice: 0m, marketSnapshotId: null, costBasisSource: null,
            estimatedProfit: 0m, requiredCapital: 0m, netRoiPercent: 0m, evidence: "Evidenz");

        Assert.False(original.IsActionable);
        db.TradeContracts.Add(original);
        await db.SaveChangesAsync();

        var copy = await db.TradeContracts.SingleAsync(c => c.TradingOpportunityId == 3);
        Assert.False(copy.IsActionable);
        Assert.NotNull(copy.NotActionableReason);
        Assert.Contains("MarketSnapshotId", copy.NotActionableReason);
        Assert.Contains("CostBasisSource", copy.NotActionableReason);
    }
}