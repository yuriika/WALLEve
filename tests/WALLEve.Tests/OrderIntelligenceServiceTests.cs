using WALLEve.Models.Authentication;
using WALLEve.Models.Database;
using WALLEve.Models.Esi.Alliance;
using WALLEve.Models.Esi.Character;
using WALLEve.Models.Esi.Corporation;
using WALLEve.Models.Esi.Markets;
using WALLEve.Models.Esi.Universe;
using WALLEve.Models.Esi.Wallet;
using WALLEve.Models.Sde;
using WALLEve.Services.Authentication.Interfaces;
using WALLEve.Services.Esi.Interfaces;
using WALLEve.Services.Market;
using WALLEve.Services.Market.Interfaces;
using WALLEve.Services.Sde.Interfaces;

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

    // ------------------------------------------------------------------
    // Invariante (#4): gespeicherte Basis ohne erneuten Buy-Aufschlag
    // ------------------------------------------------------------------

    [Fact]
    public void Simulate_Sell_StoredBasis_DoesNotChargeBuyBrokerFee()
    {
        var service = CreateService();
        var ctx = service.BuildOrderBook(new[] { Sell(1, 110), Sell(2, 112) }, OwnSell(115));
        ctx.OwnOrderId = 999; ctx.OwnPrice = 115; ctx.OwnRemaining = 500; ctx.OwnLocationId = 1;
        ctx.CostBasisPerUnit = 90;

        var sim = service.SimulatePriceChange(ctx, 109.99, null);

        // Ohne Skills: Sell-Netto 109.99×0.895×500 = 49.220,525; Erwerbskosten =
        // Basis 90×500 = 45.000 (KEIN 3%-Buy-Aufschlag); Modify-Fee 824,925
        // (Relist (1−0.5)×0.03×109.99×500) → Gewinn exakt 3.395,60
        Assert.NotNull(sim.NetProfitAfterChange);
        Assert.Equal(3_395.6, sim.NetProfitAfterChange!.Value, 2);
        // Break-even der gespeicherten Basis ohne Buy-Faktor: 90/0.895 = 100,56
        Assert.NotNull(sim.BreakEvenPrice);
        Assert.Equal(100.5587, sim.BreakEvenPrice!.Value, 4);
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

    // ------------------------------------------------------------------
    // Datenqualität des Orderbuchs (Issue #26): Fehler ≠ leeres Orderbuch
    // ------------------------------------------------------------------

    private sealed class FakeEsiApiService : IEsiApiService
    {
        public List<MarketOrder>? OwnOrders { get; set; }
        public List<RegionalMarketOrder>? ForeignOrders { get; set; }

        public Task<List<MarketOrder>?> GetMarketOrdersAsync(int characterId) => Task.FromResult(OwnOrders);
        public Task<List<RegionalMarketOrder>?> GetAllRegionalMarketOrdersAsync(int regionId, int? typeId = null, string orderType = "all", CancellationToken ct = default) => Task.FromResult(ForeignOrders);

        public Task<CharacterOverview?> GetCharacterOverviewAsync() => throw new NotImplementedException();
        public Task<EveCharacter?> GetCharacterAsync(int characterId) => throw new NotImplementedException();
        public Task<EveCorporation?> GetCorporationAsync(int corporationId) => throw new NotImplementedException();
        public Task<EveAlliance?> GetAllianceAsync(int allianceId) => throw new NotImplementedException();
        public Task<double?> GetWalletBalanceAsync(int characterId) => throw new NotImplementedException();
        public Task<CharacterLocation?> GetLocationAsync(int characterId) => throw new NotImplementedException();
        public Task<CharacterShip?> GetCurrentShipAsync(int characterId) => throw new NotImplementedException();
        public Task<CharacterOnlineStatus?> GetOnlineStatusAsync(int characterId) => throw new NotImplementedException();
        public Task<SolarSystem?> GetSolarSystemAsync(int systemId) => throw new NotImplementedException();
        public Task<EveType?> GetTypeAsync(int typeId) => throw new NotImplementedException();
        public Task<CharacterSkills?> GetCharacterSkillsAsync() => throw new NotImplementedException();
        public Task<List<CharacterAsset>> GetCharacterAssetsAsync(int characterId) => throw new NotImplementedException();
        public Task<List<WalletJournalEntry>?> GetWalletJournalAsync(int characterId, int page = 1) => throw new NotImplementedException();
        public Task<List<WalletTransaction>?> GetWalletTransactionsAsync(int characterId) => throw new NotImplementedException();
        public Task<List<WalletJournalEntry>?> GetAllWalletJournalPagesAsync(int characterId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<WalletTransaction>?> GetAllWalletTransactionsPagesAsync(int characterId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<MarketOrderHistory>?> GetMarketOrderHistoryAsync(int characterId) => throw new NotImplementedException();
        public Task<List<WalletJournalEntry>?> GetCorporationWalletJournalAsync(int corporationId, int division, int page = 1) => throw new NotImplementedException();
        public Task<List<WalletTransaction>?> GetCorporationWalletTransactionsAsync(int corporationId, int division) => throw new NotImplementedException();
        public Task<List<SystemJumps>?> GetSystemJumpsAsync() => throw new NotImplementedException();
        public Task<List<SystemKills>?> GetSystemKillsAsync() => throw new NotImplementedException();
        public Task<List<RegionalMarketOrder>?> GetRegionalMarketOrdersAsync(int regionId, int? typeId = null, string orderType = "all", int page = 1) => throw new NotImplementedException();
        public Task<List<MarketHistoryEntry>?> GetMarketHistoryAsync(int regionId, int typeId) => throw new NotImplementedException();
        public Task<List<MarketPrice>?> GetMarketPricesAsync() => throw new NotImplementedException();
    }

    private sealed class FakeSdeUniverseService : ISdeUniverseService
    {
        public Task<bool> IsDatabaseAvailableAsync() => Task.FromResult(false);
        public Task<string?> GetTypeNameAsync(int typeId) => Task.FromResult<string?>(null);
        public Task<string?> GetTypeGroupAsync(int typeId) => Task.FromResult<string?>(null);
        public Task<SolarSystemInfo?> GetSolarSystemAsync(int solarSystemId) => Task.FromResult<SolarSystemInfo?>(null);
        public Task<string?> GetRegionNameAsync(int regionId) => Task.FromResult<string?>(null);
        public Task<string?> GetLocationNameAsync(long locationId) => Task.FromResult<string?>(null);
        public Task<Dictionary<int, string>> GetAllMarketItemsAsync() => Task.FromResult(new Dictionary<int, string>());
        public Task<Dictionary<int, string>> GetAllRegionsAsync() => Task.FromResult(new Dictionary<int, string>());
        public Task<Dictionary<int, string>> SearchSolarSystemsAsync(string searchQuery, int maxResults = 10) => Task.FromResult(new Dictionary<int, string>());
    }

    private sealed class FakeCostBasisService : ICostBasisService
    {
        public Task<List<CostBasisItemView>> GetItemsAsync(int characterId) => Task.FromResult(new List<CostBasisItemView>());
        public Task<double?> GetCostBasisPerUnitAsync(int characterId, int typeId) => Task.FromResult<double?>(null);
        public Task<long> StartEstimateJobAsync(int characterId, IEnumerable<int> typeIds, int regionId) => Task.FromResult(0L);
        public Task<long> StartInventoryScanAsync(int characterId, int regionId) => Task.FromResult(0L);
        public Task SetManualValueAsync(int characterId, int typeId, double value, DateTime? purchaseDate = null) => Task.CompletedTask;
        public Task ResetEntryAsync(int characterId, int typeId) => Task.CompletedTask;
        public Task<BackgroundJob?> GetActiveJobAsync(string jobType, int? characterId = null) => Task.FromResult<BackgroundJob?>(null);
        public Task<int> GetDefaultEstimateRegionAsync() => Task.FromResult(10000002);
        public Task SetDefaultEstimateRegionAsync(int regionId) => Task.CompletedTask;
        public IReadOnlyDictionary<int, string> KnownRegions { get; } = new Dictionary<int, string>();
        public string EstimateJobType => "CostBasisEstimate";
        public string InventoryScanJobType => "InventoryScan";
    }

    private static MarketOrder OwnOrder() => new()
    {
        OrderId = 500, RegionId = 10000002, TypeId = 34, IsBuyOrder = true,
        Price = 100, VolumeRemain = 10, LocationId = 60003760, Duration = 90,
        Issued = new DateTime(2026, 6, 1)
    };

    private static OrderIntelligenceService CreateServiceWithDeps(FakeEsiApiService esi) => new(
        esi,
        new FakeSdeUniverseService(),
        new FeeCalculatorService(),
        new FakeCostBasisService(),
        Microsoft.Extensions.Logging.Abstractions.NullLogger<OrderIntelligenceService>.Instance);

    [Fact]
    public async Task GetOrderBook_ForeignFetchFailed_ReturnsFailedStatus_NoRecommendation()
    {
        var esi = new FakeEsiApiService { OwnOrders = new List<MarketOrder> { OwnOrder() }, ForeignOrders = null };
        var service = CreateServiceWithDeps(esi);

        var context = await service.GetOrderBookAsync(90073315, 500);

        // ESI-Fehler ist KEIN leeres Orderbuch: keine Positions-Empfehlung erzeugen
        Assert.NotNull(context);
        Assert.Equal(OrderBookDataStatus.Failed, context!.ForeignDataStatus);
        Assert.NotNull(context.ForeignDataError);
        Assert.False(context.IsHighestBuyAtLocation);
        Assert.Equal(0, context.OwnPosition);
        // Keine FREMDEN Zeilen (nur die eigene Order steht in der Schlange)
        Assert.All(context.BuySide, line => Assert.True(line.IsOwn));
        Assert.All(context.SellSide, line => Assert.True(line.IsOwn));
    }

    [Fact]
    public async Task GetOrderBook_ValidEmptyForeign_IsEmptyAndBestPosition()
    {
        var esi = new FakeEsiApiService { OwnOrders = new List<MarketOrder> { OwnOrder() }, ForeignOrders = new List<RegionalMarketOrder>() };
        var service = CreateServiceWithDeps(esi);

        var context = await service.GetOrderBookAsync(90073315, 500);

        // Gültig leeres Orderbuch: eigene Order ist die beste — Anzeige ist korrekt
        Assert.NotNull(context);
        Assert.Equal(OrderBookDataStatus.Empty, context!.ForeignDataStatus);
        Assert.True(context.IsHighestBuyAtLocation);
        Assert.Equal(1, context.OwnPosition);
    }
}