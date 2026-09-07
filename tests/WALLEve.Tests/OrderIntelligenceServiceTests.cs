using WALLEve.Services.Market;
using WALLEve.Services.Market.Interfaces;

namespace WALLEve.Tests;

/// <summary>
/// Tests für die Orderbuch-Logik: EVE-Sortierung (Sell aufsteigend, Buy absteigend),
/// Position der eigenen Order, Unterbietungs-Erkennung (gleiche Location).
/// </summary>
public class OrderIntelligenceServiceTests
{
    private static OrderBookLine Sell(long id, double price, long loc = 1) => new()
    {
        OrderId = id, Price = price, IsBuyOrder = false,
        LocationId = loc, IsSameLocation = loc == 1,
        VolumeRemain = 1000,
        Issued = new DateTime(2026, 1, 1).AddDays(id) // id 1 = älteste
    };

    private static OrderBookLine Buy(long id, double price, long loc = 1) => new()
    {
        OrderId = id, Price = price, IsBuyOrder = true,
        LocationId = loc, IsSameLocation = loc == 1,
        VolumeRemain = 1000,
        Issued = new DateTime(2026, 1, 1).AddDays(id)
    };

    private static OrderBookLine OwnSell(double price, long loc = 1) => new()
    {
        OrderId = 999, IsOwn = true, Price = price, IsBuyOrder = false,
        LocationId = loc, IsSameLocation = true, VolumeRemain = 500,
        Issued = new DateTime(2026, 6, 1) // eigene Order: Mitte des Jahres
    };

    private static OrderBookLine OwnBuy(double price, long loc = 1) => new()
    {
        OrderId = 999, IsOwn = true, Price = price, IsBuyOrder = true,
        LocationId = loc, IsSameLocation = true, VolumeRemain = 500,
        Issued = new DateTime(2026, 6, 1)
    };

    private static OrderIntelligenceService CreateService() => new(
        null!,
        null!,
        new FeeCalculatorService(),
        null!,
        Microsoft.Extensions.Logging.Abstractions.NullLogger<OrderIntelligenceService>.Instance);

    // ------------------------------------------------------------------
    // Sortierung
    // ------------------------------------------------------------------

    [Fact]
    public void BuildOrderBook_SellOrdersSortedAscendingByPrice()
    {
        var service = CreateService();
        var orders = new[] { Sell(1, 120), Sell(2, 100), Sell(3, 110) };

        var ctx = service.BuildOrderBook(orders, OwnSell(105));

        Assert.Equal(new[] { 100.0, 105.0, 110.0, 120.0 }, ctx.SellSide.Select(o => o.Price)); // eigene (105) einsortiert
        Assert.Equal(1, ctx.SellSide[0].Position);
        Assert.Equal(2, ctx.SellSide[1].Position); // eigene Order an Position 2
        Assert.Equal(4, ctx.SellSide[3].Position);
    }

    [Fact]
    public void BuildOrderBook_BuyOrdersSortedDescendingByPrice()
    {
        var service = CreateService();
        var orders = new[] { Buy(1, 120), Buy(2, 100), Buy(3, 110) };

        var ctx = service.BuildOrderBook(orders, OwnBuy(105));

        Assert.Equal(new[] { 120.0, 110.0, 105.0, 100.0 }, ctx.BuySide.Select(o => o.Price));
    }

    // ------------------------------------------------------------------
    // Tie-Breaking: gleicher Preis → ältere Order zuerst (FIFO)
    // ------------------------------------------------------------------

    [Fact]
    public void SellOrder_SamePrice_OlderOrderComesFirst()
    {
        var service = CreateService();
        // Fremde Order 2: gleicher Preis (100) wie eigene, aber JÜNGER (id=2 vs own 2026-06)
        var orders = new[] { Sell(2, 100), Sell(1, 100) }; // id 1 = älter, id 2 = jünger

        var ctx = service.BuildOrderBook(orders, OwnSell(100));

        // Reihenfolge bei Preis 100: älteste zuerst → id1 (Jan), id2 (Jan+2Tage), own (Juni)
        Assert.Equal(new[] { 100.0, 100.0, 100.0 }, ctx.SellSide.Select(o => o.Price));
        Assert.False(ctx.SellSide[0].IsOwn); // id 1
        Assert.False(ctx.SellSide[1].IsOwn); // id 2
        Assert.True(ctx.SellSide[2].IsOwn);  // eigene ist die jüngste → Position 3
        Assert.Equal(3, ctx.OwnPosition);
    }

    [Fact]
    public void SellOrder_SamePrice_OwnOrderOlder_ComesFirst()
    {
        var service = CreateService();
        // Fremde Order 5: gleicher Preis (100), aber JÜNGER als eigene
        var olderOwn = new OrderBookLine
        {
            OrderId = 999, IsOwn = true, Price = 100, IsBuyOrder = false,
            LocationId = 1, IsSameLocation = true, VolumeRemain = 500,
            Issued = new DateTime(2025, 12, 1) // älter als fremde (2026-01-06)
        };
        var orders = new[] { Sell(5, 100) }; // fremde Issued = 2026-01-06

        var ctx = service.BuildOrderBook(orders, olderOwn);

        Assert.True(ctx.SellSide[0].IsOwn);  // eigene zuerst (älter)
        Assert.Equal(1, ctx.OwnPosition);
        Assert.Equal(2, ctx.SellSide[1].Position);
    }

    // ------------------------------------------------------------------
    // Position & Unterbietung
    // ------------------------------------------------------------------

    [Fact]
    public void SellOrder_WithCheaperCompetitors_ReportsPositionAndCount()
    {
        var service = CreateService();
        // 2 günstigere an gleicher Location, 1 teurer
        var orders = new[] { Sell(1, 100), Sell(2, 102), Sell(3, 110, loc: 2) };

        var ctx = service.BuildOrderBook(orders, OwnSell(105));

        Assert.Equal(3, ctx.OwnPosition);            // Position 3 in der Gesamtschlange
        Assert.Equal(2, ctx.CompetingOrdersAhead);   // 2 günstigere an gleicher Location
        Assert.False(ctx.IsLowestSellAtLocation);
    }

    [Fact]
    public void SellOrder_LowestAtLocation_IsBest()
    {
        var service = CreateService();
        var orders = new[] { Sell(1, 100), Sell(2, 105, loc: 2), Sell(3, 110, loc: 3) };

        var ctx = service.BuildOrderBook(orders, OwnSell(98));

        Assert.Equal(1, ctx.OwnPosition);
        Assert.Equal(0, ctx.CompetingOrdersAhead);
        Assert.True(ctx.IsLowestSellAtLocation);
    }

    [Fact]
    public void BuyOrder_WithHigherBidders_ReportsPosition()
    {
        var service = CreateService();
        var orders = new[] { Buy(1, 110), Buy(2, 120) };

        var ctx = service.BuildOrderBook(orders, OwnBuy(115));

        Assert.Equal(2, ctx.OwnPosition);            // höchster ist 120, dann ich
        Assert.Equal(1, ctx.CompetingOrdersAhead);   // einer bietet mehr
        Assert.False(ctx.IsHighestBuyAtLocation);
    }

    [Fact]
    public void BuyOrder_HighestBidder_IsBest()
    {
        var service = CreateService();
        var orders = new[] { Buy(1, 110), Buy(2, 100) };

        var ctx = service.BuildOrderBook(orders, OwnBuy(125));

        Assert.Equal(1, ctx.OwnPosition);
        Assert.Equal(0, ctx.CompetingOrdersAhead);
        Assert.True(ctx.IsHighestBuyAtLocation);
    }

    // ------------------------------------------------------------------
    // Same-Location-Berechnung
    // ------------------------------------------------------------------

    [Fact]
    public void CompetingOrders_OnlyCountSameLocation()
    {
        var service = CreateService();
        // 1 günstiger an gleicher Location, 1 günstiger an ANDERER Location (zählt nicht)
        var orders = new[] { Sell(1, 100), Sell(2, 102, loc: 5) };

        var ctx = service.BuildOrderBook(orders, OwnSell(103));

        Assert.Equal(1, ctx.CompetingOrdersAhead);
        Assert.False(ctx.IsLowestSellAtLocation); // anderer günstiger an gleicher Loc existiert
    }

    [Fact]
    public void EmptyOrderBook_Sell_OwnOrderIsBest()
    {
        var service = CreateService();
        var ctx = service.BuildOrderBook(Array.Empty<OrderBookLine>(), OwnSell(100));

        Assert.Equal(1, ctx.OwnPosition);
        Assert.Equal(0, ctx.CompetingOrdersAhead);
        Assert.True(ctx.IsLowestSellAtLocation);
    }

    // ------------------------------------------------------------------
    // Preisänderungs-Simulation
    // ------------------------------------------------------------------

    [Fact]
    public void Simulate_PriceDecreaseToBest_StillProfit()
    {
        var service = CreateService();
        // Fremde Sell-Orders bei 110/112, eigene 115; Einkauf 90 → Break-even ~103,6 ISK
        var ctx = service.BuildOrderBook(new[] { Sell(1, 110), Sell(2, 112) }, OwnSell(115));
        ctx.OwnOrderId = 999; ctx.OwnPrice = 115; ctx.OwnRemaining = 500; ctx.OwnLocationId = 1;
        ctx.CostBasisPerUnit = 90;

        var sim = service.SimulatePriceChange(ctx, 109.99, null);

        Assert.Equal(1, sim.NewPosition);        // jetzt günstigster Anbieter
        Assert.True(sim.WouldBeBest);
        // Relist-Fee ohne Skills: (1-0.50)*0.03*109.99*500 = 824.925
        Assert.Equal(824.925, sim.ModifyFee, 3);
        Assert.True(sim.NetProfitAfterChange > 0); // 109,99 > Break-even 103,6 → Profit
        Assert.True(sim.BreakEvenPrice < 109.99);
    }

    [Fact]
    public void Simulate_PriceBelowBreakEven_ShowsLoss()
    {
        var service = CreateService();
        var ctx = service.BuildOrderBook(new[] { Sell(1, 100), Sell(2, 110) }, OwnSell(105));
        ctx.OwnOrderId = 999; ctx.OwnPrice = 105; ctx.OwnRemaining = 500; ctx.OwnLocationId = 1;
        ctx.CostBasisPerUnit = 90;

        var sim = service.SimulatePriceChange(ctx, 1.0, null);

        Assert.Equal(1, sim.NewPosition);        // Position 1, aber ...
        Assert.NotNull(sim.NetProfitAfterChange);
        Assert.True(sim.NetProfitAfterChange < 0); // ... Verlust
        Assert.Contains("Verlust", sim.Summary);
    }

    [Fact]
    public void Simulate_NoCostBasis_NoProfitCalculation()
    {
        var service = CreateService();
        var ctx = service.BuildOrderBook(new[] { Sell(1, 100) }, OwnSell(105));
        ctx.OwnOrderId = 999; ctx.OwnPrice = 105; ctx.OwnRemaining = 500; ctx.OwnLocationId = 1;
        ctx.CostBasisPerUnit = null;

        var sim = service.SimulatePriceChange(ctx, 95, null);

        Assert.Null(sim.NetProfitAfterChange);
        Assert.Null(sim.BreakEvenPrice);
        Assert.Contains("Cost Basis", sim.Summary);
    }

    [Fact]
    public void Simulate_BuyOrder_HigherBid_MovesAhead()
    {
        var service = CreateService();
        var ctx = service.BuildOrderBook(new[] { Buy(1, 100), Buy(2, 120) }, OwnBuy(105));
        ctx.OwnOrderId = 999; ctx.OwnPrice = 105; ctx.OwnRemaining = 500; ctx.OwnLocationId = 1;
        ctx.OwnIsBuyOrder = true; // BuildOrderBook setzt das Feld nicht — nur GetOrderBookAsync
        ctx.CostBasisPerUnit = null;

        var sim = service.SimulatePriceChange(ctx, 121, null);

        Assert.Equal(1, sim.NewPosition);        // überbietet den höchsten Käufer
        Assert.True(sim.WouldBeBest);
        Assert.True(sim.ModifyFee > 0);          // Preiserhöhung kostet
        Assert.Contains("Käufer", sim.Summary);
    }
}