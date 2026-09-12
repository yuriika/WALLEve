using Microsoft.EntityFrameworkCore;
using WALLEve.Data;
using WALLEve.Models.Authentication;
using WALLEve.Models.Database;
using WALLEve.Models.Esi.Alliance;
using WALLEve.Models.Esi.Character;
using WALLEve.Models.Esi.Corporation;
using WALLEve.Models.Esi.Markets;
using WALLEve.Models.Esi.Universe;
using WALLEve.Models.Esi.Wallet;
using WALLEve.Services.AI.Interfaces;
using WALLEve.Services.Authentication.Interfaces;
using WALLEve.Services.Esi.Interfaces;
using WALLEve.Services.Market;
using WALLEve.Services.Market.Interfaces;

namespace WALLEve.Tests;

/// <summary>
/// Tests für die BESTANDS-basierte Markt-Analyse: Verkaufssimulation pro Item
/// (Cost Basis vs. Marktpreis, echte Fees), Dedup, Cleanup abgelaufener
/// Opportunities und Charakter-Filter.
/// </summary>
public class MarketAnalysisServiceTests
{
    private const int CharacterId = 90073315;

    private sealed class FakeOllamaService : IOllamaService
    {
        public Task<string> GenerateAsync(string prompt, object? context = null, string? model = null)
            => Task.FromResult("mock");

        public Task<T?> GenerateJsonAsync<T>(string prompt, object? context = null, string? model = null)
            => Task.FromResult(default(T));

        public Task<bool> IsAvailableAsync() => Task.FromResult(false);
        public Task<List<string>?> GetAvailableModelsAsync() => Task.FromResult<List<string>?>(null);
    }

    private sealed class FakeAuthService : IEveAuthenticationService
    {
        public Task<EveAuthState?> GetAuthStateAsync()
            => Task.FromResult<EveAuthState?>(new EveAuthState
            {
                AccessToken = "tok", RefreshToken = "ref",
                CharacterId = CharacterId, CharacterName = "Test"
            });

        public Task<bool> IsAuthenticatedAsync() => Task.FromResult(true);
        public string GetLoginUrl() => "http://login";
        public Task<bool> HandleCallbackAsync(string code, string state) => Task.FromResult(true);
        public Task<string?> GetAccessTokenAsync() => Task.FromResult<string?>("tok");
        public Task LogoutAsync() => Task.CompletedTask;
        public Task<List<KnownCharacter>> GetAllCharactersAsync() => Task.FromResult(new List<KnownCharacter>());
        public Task<bool> SwitchCharacterAsync(int characterId) => Task.FromResult(true);

        // Interface-Vertrag ohne Nutzung in diesen Tests: explizite leere
        // Accessoren statt Feld-Event, damit CS0067 nicht feuert.
        event EventHandler<bool>? IEveAuthenticationService.AuthenticationStateChanged
        {
            add { }
            remove { }
        }
    }

    private sealed class FakeInventoryService : IInventoryService
    {
        public List<InventoryItem> Items { get; } = new();

        public Task<List<InventoryItem>> GetInventoryAsync(int characterId) => Task.FromResult(Items);
        public Task<PortfolioOverview> GetPortfolioOverviewAsync(int characterId)
            => Task.FromResult(new PortfolioOverview());
        public Task<List<InventoryItem>> GetPrioritizedItemsAsync(int characterId,
            InventorySortMode sortMode = InventorySortMode.Opportunity)
            => Task.FromResult(Items);
    }

    /// <summary>Fake: nur GetCharacterSkillsAsync wird in der Analyse verwendet; Rest wirft.</summary>
    private sealed class FakeEsiApiService : IEsiApiService
    {
        public Task<CharacterSkills?> GetCharacterSkillsAsync()
            => Task.FromResult<CharacterSkills?>(new CharacterSkills());

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
        public Task<List<CharacterAsset>> GetCharacterAssetsAsync(int characterId) => throw new NotImplementedException();
        public Task<List<WalletJournalEntry>?> GetWalletJournalAsync(int characterId, int page = 1) => throw new NotImplementedException();
        public Task<List<WalletTransaction>?> GetWalletTransactionsAsync(int characterId) => throw new NotImplementedException();
        public Task<List<WalletJournalEntry>?> GetAllWalletJournalPagesAsync(int characterId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<WalletTransaction>?> GetAllWalletTransactionsPagesAsync(int characterId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<MarketOrder>?> GetMarketOrdersAsync(int characterId) => throw new NotImplementedException();
        public Task<List<MarketOrderHistory>?> GetMarketOrderHistoryAsync(int characterId) => throw new NotImplementedException();
        public Task<List<WalletJournalEntry>?> GetCorporationWalletJournalAsync(int corporationId, int division, int page = 1) => throw new NotImplementedException();
        public Task<List<WalletTransaction>?> GetCorporationWalletTransactionsAsync(int corporationId, int division) => throw new NotImplementedException();
        public Task<List<SystemJumps>?> GetSystemJumpsAsync() => throw new NotImplementedException();
        public Task<List<SystemKills>?> GetSystemKillsAsync() => throw new NotImplementedException();
        public Task<List<RegionalMarketOrder>?> GetRegionalMarketOrdersAsync(int regionId, int? typeId = null, string orderType = "all", int page = 1) => throw new NotImplementedException();
        public Task<List<RegionalMarketOrder>?> GetAllRegionalMarketOrdersAsync(int regionId, int? typeId = null, string orderType = "all", CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<MarketHistoryEntry>?> GetMarketHistoryAsync(int regionId, int typeId) => throw new NotImplementedException();
        public Task<List<MarketPrice>?> GetMarketPricesAsync() => throw new NotImplementedException();
    }

    private static MarketAnalysisService CreateService(WalletDbContext db, FakeInventoryService inventory)
        => new(
            new FakeOllamaService(), db, new FeeCalculatorService(),
            inventory, new FakeAuthService(), new FakeEsiApiService(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<MarketAnalysisService>.Instance);

    /// <summary>Item mit Cost Basis, das mit echtem Gewinn verkauft werden kann.</summary>
    private static InventoryItem ProfitableItem(int typeId, double costBasis, double sellPrice, int qty = 1000)
    {
        // Cost Basis 90, Sell 110, ohne Skills (3% Broker, 7.5% Tax):
        // Netto Sell = 110 × 0.895 = 98.45; Buy inkl. Broker = 90 × 1.03 = 92.7 → Profit 5.75
        return new InventoryItem
        {
            TypeId = typeId,
            TypeName = $"Item {typeId}",
            TotalQuantity = qty,
            CostBasisPerUnit = costBasis,
            BestSellPrice = sellPrice
        };
    }

    private static InventoryItem LossItem(int typeId) => new()
    {
        TypeId = typeId,
        TypeName = $"Item {typeId}",
        TotalQuantity = 1000,
        CostBasisPerUnit = 120.0,
        BestSellPrice = 100.0 // Netto 89.5 − Buy 123.6 = −34.1 → Verlust
    };

    // ------------------------------------------------------------------
    // Kern-Logik: nur gewinnbringende Items werden Opportunities
    // ------------------------------------------------------------------

    [Fact]
    public async Task Analyze_CreatesOpportunityOnlyForProfitableItem()
    {
        using var db = TestDb.Create();
        var inventory = new FakeInventoryService();
        inventory.Items.Add(ProfitableItem(1, 90, 110)); // Gewinn
        inventory.Items.Add(LossItem(2));                 // Verlust
        var service = CreateService(db, inventory);

        var opportunities = await service.AnalyzeMarketDataAsync();

        var types = opportunities.Select(o => o.TypeId).ToList();
        Assert.Contains(1, types);
        Assert.DoesNotContain(2, types); // Verlust-Item erzeugt KEINE Opportunity
        Assert.All(opportunities, o => Assert.True(o.EstimatedProfit > 0));
    }

    [Fact]
    public async Task Analyze_ProfitIsNetAfterFees_WithCharacterSkills()
    {
        using var db = TestDb.Create();
        var inventory = new FakeInventoryService();
        inventory.Items.Add(ProfitableItem(1, 90, 110));
        var service = CreateService(db, inventory);

        var opportunities = await service.AnalyzeMarketDataAsync();
        var opp = opportunities.Single();

        // Ohne Skills: Buy 90×1.03=92.7; Sell 110×0.895=98.45 → Netto 5.75 pro Einheit
        // × 1000 Einheiten = 5750 ISK Gesamtgewinn
        Assert.Equal(5_750.0, opp.EstimatedProfit, 2);
        Assert.Equal("inventory_sell", opp.OpportunityType);
        Assert.Equal(CharacterId, opp.CharacterId);
    }

    // ------------------------------------------------------------------
    // Dedup: zweimal analysieren → keine Duplikate
    // ------------------------------------------------------------------

    [Fact]
    public async Task Analyze_Twice_CreatesNoDuplicates()
    {
        using var db = TestDb.Create();
        var inventory = new FakeInventoryService();
        inventory.Items.Add(ProfitableItem(1, 90, 110));
        var service = CreateService(db, inventory);

        await service.AnalyzeMarketDataAsync();
        var second = await service.AnalyzeMarketDataAsync();

        var dbCount = await db.TradingOpportunities.CountAsync(o => o.TypeId == 1 && o.Status == "active");
        Assert.Equal(1, dbCount);
        Assert.Single(second, o => o.TypeId == 1 && o.Status == "active");
    }

    // ------------------------------------------------------------------
    // Cleanup abgelaufener Opportunities
    // ------------------------------------------------------------------

    [Fact]
    public async Task Analyze_RemovesExpiredOpportunities()
    {
        using var db = TestDb.Create();
        var inventory = new FakeInventoryService();
        inventory.Items.Add(ProfitableItem(1, 90, 110));

        db.TradingOpportunities.Add(new TradingOpportunity
        {
            CharacterId = CharacterId,
            TypeId = 1,
            OpportunityType = "inventory_sell",
            BuyPrice = 90, SellPrice = 95,
            EstimatedProfit = 1, RequiredCapital = 90, Confidence = 70,
            AIModel = "heuristic", Reasoning = "old",
            DetectedAt = DateTime.UtcNow.AddHours(-3),
            ExpiresAt = DateTime.UtcNow.AddHours(-2), // abgelaufen!
            Status = "active"
        });
        await db.SaveChangesAsync();

        var service = CreateService(db, inventory);
        var opportunities = await service.AnalyzeMarketDataAsync();

        // Abgelaufene wurde gelöscht, neue aktive existiert
        Assert.Single(opportunities, o => o.TypeId == 1);
        var staleCount = await db.TradingOpportunities.CountAsync(o => o.ExpiresAt < DateTime.UtcNow);
        Assert.Equal(0, staleCount);
    }

    // ------------------------------------------------------------------
    // GetActiveOpportunitiesAsync (lesend, char-filter)
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetActive_ReturnsOnlyActiveAndSortedByConfidence()
    {
        using var db = TestDb.Create();
        db.TradingOpportunities.AddRange(
            new TradingOpportunity
            {
                CharacterId = CharacterId, TypeId = 1, OpportunityType = "inventory_sell",
                BuyPrice = 1, SellPrice = 2, EstimatedProfit = 1, Confidence = 50,
                AIModel = "heuristic", DetectedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(1), Status = "active"
            },
            new TradingOpportunity
            {
                CharacterId = CharacterId, TypeId = 2, OpportunityType = "inventory_sell",
                BuyPrice = 1, SellPrice = 2, EstimatedProfit = 1, Confidence = 90,
                AIModel = "heuristic", DetectedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(1), Status = "active"
            },
            new TradingOpportunity
            {
                CharacterId = CharacterId, TypeId = 3, OpportunityType = "inventory_sell",
                BuyPrice = 1, SellPrice = 2, EstimatedProfit = 1, Confidence = 80,
                AIModel = "heuristic", DetectedAt = DateTime.UtcNow.AddHours(-2),
                ExpiresAt = DateTime.UtcNow.AddHours(-1), Status = "expired"
            },
            new TradingOpportunity
            {
                CharacterId = 999, TypeId = 4, OpportunityType = "inventory_sell",
                BuyPrice = 1, SellPrice = 2, EstimatedProfit = 1, Confidence = 99,
                AIModel = "heuristic", DetectedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(1), Status = "active"
            });
        await db.SaveChangesAsync();
        var service = CreateService(db, new FakeInventoryService());

        var active = await service.GetActiveOpportunitiesAsync(CharacterId);

        Assert.Equal(2, active.Count);               // fremder Charakter (Type 4) ausgefiltert
        Assert.Equal(2, active[0].TypeId);           // höchste Confidence zuerst
        Assert.Equal(1, active[1].TypeId);
    }
}