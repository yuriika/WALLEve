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
using WALLEve.Services.Authentication.Interfaces;
using WALLEve.Services.Esi.Interfaces;
using WALLEve.Services.Market;
using WALLEve.Services.Market.Interfaces;
using WALLEve.Services.Trading;

namespace WALLEve.Tests;

/// <summary>
/// Tests für die BESTANDS-basierte Markt-Analyse: Verkaufssimulation pro Item
/// (Cost Basis vs. Marktpreis, echte Fees), Dedup, Cleanup abgelaufener
/// Opportunities und Charakter-Filter.
/// </summary>
public class MarketAnalysisServiceTests
{
    private const int CharacterId = 90073315;

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
        public Task<StructureLookupResult> GetStructureAsync(long structureId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<CharacterAsset>?> GetCharacterAssetsAsync(int characterId) => throw new NotImplementedException();
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
            db, new FeeCalculatorService(),
            inventory, new FakeAuthService(), new FakeEsiApiService(),
            new TradeStatusService(db),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<MarketAnalysisService>.Instance);

    /// <summary>Item mit Cost Basis, das mit echtem Gewinn verkauft werden kann.</summary>
    private static InventoryItem ProfitableItem(int typeId, double costBasis, double sellPrice, int qty = 1000)
    {
        // Cost Basis 90, Sell 110, ohne Skills (3% Broker, 7.5% Tax):
        // Netto Sell = 110 × 0.895 = 98.45; Erwerbskosten = Basis 90 (kein erneuter
        // Buy-Aufschlag — Invariante #4) → Profit 8.45 pro Einheit
        return new InventoryItem
        {
            TypeId = typeId,
            TypeName = $"Item {typeId}",
            TotalQuantity = qty,
            CostBasisPerUnit = costBasis,
            BestSellPrice = sellPrice,
            Locations = [StationLocation(qty)],
            SellContexts = [StationSellContext(typeId, qty)]
        };
    }

    private static InventoryItem LossItem(int typeId) => new()
    {
        TypeId = typeId,
        TypeName = $"Item {typeId}",
        TotalQuantity = 1000,
        CostBasisPerUnit = 120.0,
        BestSellPrice = 100.0, // Netto 89.5 − Basis 120 = −30,5 → Verlust
        Locations = [StationLocation(1000)],
        SellContexts = [StationSellContext(typeId, 1000)]
    };

    private static InventoryLocationAggregate StationLocation(int qty)
        => new() { LocationId = 60003466, LocationType = "station", LocationFlag = "Hangar", Quantity = qty };

    private static InventorySellContext StationSellContext(int typeId, int qty)
        => new()
        {
            LocationId = 60003466, LocationType = "station",
            LocationLabel = $"Station 60003466 (Typ {typeId})",
            Quantity = qty
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

        // Ohne Skills: Erwerbskosten = Basis 90 (KEIN Buy-Aufschlag, Invariante #4);
        // Sell 110×0.895 = 98.45 → Netto 8.45 pro Einheit × 1000 Einheiten = 8450 ISK
        Assert.Equal(8_450.0, opp.EstimatedProfit, 2);
        Assert.Equal("inventory_sell", opp.OpportunityType);
        Assert.Equal(CharacterId, opp.CharacterId);

        // RequiredCapital = reine Erwerbskosten (90×1000), ohne Brokeraufschlag
        Assert.Equal(90_000.0, opp.RequiredCapital, 2);
    }

    // ------------------------------------------------------------------
    // Invariante (#4): gespeicherte Basis = Erwerbskosten genau einmal
    // ------------------------------------------------------------------

    [Fact]
    public async Task Analyze_StoredBasis_DoesNotChargeBuyBrokerFee()
    {
        using var db = TestDb.Create();
        var inventory = new FakeInventoryService();
        inventory.Items.Add(ProfitableItem(1, 90, 110));
        var service = CreateService(db, inventory);

        var opp = (await service.AnalyzeMarketDataAsync()).Single();

        // Käme der 3%-Buy-Aufschlag fälschlich dazu, wäre der Gewinn
        // 5750 statt 8450 — genau dieser Unterschied ist der Bug (#4).
        var expectedProfit = (110 * 0.895 - 90) * 1000;
        Assert.Equal(expectedProfit, opp.EstimatedProfit, 2);
    }

    [Fact]
    public async Task Analyze_Profit_ScalesLinearlyWithQuantity()
    {
        using var db = TestDb.Create();
        var inventory = new FakeInventoryService();
        inventory.Items.Add(ProfitableItem(1, 90, 110, qty: 5000));
        var service = CreateService(db, inventory);

        var opp = (await service.AnalyzeMarketDataAsync()).Single();

        // 5× Menge gegenüber dem Default (1000) → 5× Gewinn, keine Mengen-Rundung
        Assert.Equal(8_450.0 * 5, opp.EstimatedProfit, 2);
        Assert.Equal(90_000.0 * 5, opp.RequiredCapital, 2);
    }

    [Fact]
    public async Task Analyze_UnknownCostBasis_CreatesNoOpportunityAndNoCrash()
    {
        using var db = TestDb.Create();
        var inventory = new FakeInventoryService();
        inventory.Items.Add(new InventoryItem
        {
            TypeId = 7, TypeName = "Item 7", TotalQuantity = 1000,
            CostBasisPerUnit = null, // Unbekannt: kein CostBasisEntry vorhanden
            BestSellPrice = 200.0
        });
        var service = CreateService(db, inventory);

        var opportunities = await service.AnalyzeMarketDataAsync();

        // Ohne gespeicherte Basis ist das Item nicht analysierbar — kein Crash, keine
        // Opportunity, keine erfundene Erwerbskosten-Zuordnung.
        Assert.Empty(opportunities);
    }

    [Fact]
    public async Task Analyze_BreakEvenInReasoning_UsesStoredBasisWithoutBuyFee()
    {
        using var db = TestDb.Create();
        var inventory = new FakeInventoryService();
        inventory.Items.Add(ProfitableItem(1, 90, 110));
        var service = CreateService(db, inventory);

        var opp = (await service.AnalyzeMarketDataAsync()).Single();

        // Break-even ohne Buy-Faktor: 90 / (1 − 0.03 − 0.075) = 100,56
        // (mit fälschlichem Buy-Aufschlag wäre er 103,58 — Rundung wird mitgeprüft)
        var expectedBreakEven = 90.0 / (1.0 - 0.03 - 0.075);
        Assert.Contains($"Break-even {expectedBreakEven:N2} ISK", opp.Evidence);
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

        var dbCount = await db.TradingOpportunities.CountAsync(o => o.TypeId == 1 && o.Status == "planned");
        Assert.Equal(1, dbCount);
        Assert.Single(second, o => o.TypeId == 1 && o.Status == "planned");
    }

    // ------------------------------------------------------------------
    // Cleanup abgelaufener Opportunities
    // ------------------------------------------------------------------

    [Fact]
    public async Task Analyze_MarksExpiredOpportunity_KeepsRecommendationAndWritesHistory()
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
            EstimatedProfit = 1, RequiredCapital = 90, Score = 70,
            Provenance = "heuristic", Evidence = "old",
            DetectedAt = DateTime.UtcNow.AddHours(-3),
            ExpiresAt = DateTime.UtcNow.AddHours(-2), // abgelaufen!
            Status = "active"
        });
        await db.SaveChangesAsync();

        var service = CreateService(db, inventory);
        var opportunities = await service.AnalyzeMarketDataAsync();

        // Issue #45: Ablauf löscht die ursprüngliche Empfehlung NICHT — sie wird
        // als "expired" markiert (Quelle System), die neu erkannte existiert parallel.
        Assert.Single(opportunities, o => o.TypeId == 1);
        var old = await db.TradingOpportunities.SingleAsync(o => o.TypeId == 1 && o.ExpiresAt < DateTime.UtcNow);
        Assert.Equal("expired", old.Status);
        Assert.Equal("old", old.Evidence); // ursprüngliche Empfehlung/Inputs erhalten

        // Historie mit Zeit + Quelle System dokumentiert den Übergang.
        var change = await db.TradeStatusChanges.SingleAsync(c => c.TradingOpportunityId == old.Id);
        Assert.Equal("active", change.FromStatus); // Legacy-Ausgangswert unverändert dokumentiert
        Assert.Equal("expired", change.ToStatus);
        Assert.Equal("system", change.Source);

        // Neue, gültige Empfehlung wurde mit canonicalem Status "planned" erzeugt.
        var fresh = await db.TradingOpportunities.SingleAsync(o => o.TypeId == 1 && o.Status == "planned");
        Assert.NotNull(fresh);
    }

    // ------------------------------------------------------------------
    // GetActiveOpportunitiesAsync (lesend, char-filter)
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetActive_ReturnsOnlyActiveAndSortedByScore()
    {
        using var db = TestDb.Create();
        db.TradingOpportunities.AddRange(
            new TradingOpportunity
            {
                CharacterId = CharacterId, TypeId = 1, OpportunityType = "inventory_sell",
                BuyPrice = 1, SellPrice = 2, EstimatedProfit = 1, Score = 50,
                Provenance = "heuristic", DetectedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(1), Status = "active"
            },
            new TradingOpportunity
            {
                CharacterId = CharacterId, TypeId = 2, OpportunityType = "inventory_sell",
                BuyPrice = 1, SellPrice = 2, EstimatedProfit = 1, Score = 90,
                Provenance = "heuristic", DetectedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(1), Status = "active"
            },
            new TradingOpportunity
            {
                CharacterId = CharacterId, TypeId = 3, OpportunityType = "inventory_sell",
                BuyPrice = 1, SellPrice = 2, EstimatedProfit = 1, Score = 80,
                Provenance = "heuristic", DetectedAt = DateTime.UtcNow.AddHours(-2),
                ExpiresAt = DateTime.UtcNow.AddHours(-1), Status = "expired"
            },
            new TradingOpportunity
            {
                CharacterId = 999, TypeId = 4, OpportunityType = "inventory_sell",
                BuyPrice = 1, SellPrice = 2, EstimatedProfit = 1, Score = 99,
                Provenance = "heuristic", DetectedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(1), Status = "active"
            });
        await db.SaveChangesAsync();
        var service = CreateService(db, new FakeInventoryService());

        var active = await service.GetActiveOpportunitiesAsync(CharacterId);

        Assert.Equal(2, active.Count);               // fremder Charakter (Type 4) ausgefiltert
        Assert.Equal(2, active[0].TypeId);           // höchster Score zuerst
        Assert.Equal(1, active[1].TypeId);
    }

    // ------------------------------------------------------------------
    // Issue #28: ortsgebundene Verkaufsanalyse — Mengen mehrerer Orte
    // werden NICHT zu einem verkaufbaren Stapel verschmolzen
    // ------------------------------------------------------------------

    [Fact]
    public async Task Analyze_QuantitySpreadOverTwoStations_OpportunityBoundToLargestLocationOnly()
    {
        using var db = TestDb.Create();
        var inventory = new FakeInventoryService();
        var item = ProfitableItem(1, 90, 110, qty: 8);
        item.TotalQuantity = 8;
        item.SellContexts =
        [
            StationSellContext(1, 5),
            new InventorySellContext
            {
                LocationId = 60003760, LocationType = "station",
                LocationLabel = "Station 60003760 (Typ 1)",
                Quantity = 3
            }
        ];
        inventory.Items.Add(item);
        var service = CreateService(db, inventory);

        var opportunities = await service.AnalyzeMarketDataAsync();

        var opp = Assert.Single(opportunities);
        // Profit nur für den größten Ort (5 Einheiten): 5 × 8.45 = 42.25; Kapital 5 × 90 = 450
        Assert.Equal(42.25, opp.EstimatedProfit, 2);
        Assert.Equal(450.0, opp.RequiredCapital, 2);
        Assert.Equal(60003466, opp.SellLocationId); // größter aufgelöster Handelsplatz
        // Reasoning benennt die Ortsbindung und die nicht abgedeckten übrigen Einheiten
        Assert.Contains("Ortsgebunden", opp.Evidence);
        Assert.Contains("5 von 8 Einheiten", opp.Evidence);
    }

    [Fact]
    public async Task Analyze_OnlyUnresolvedContainerLocation_NoLocationBoundOpportunity()
    {
        using var db = TestDb.Create();
        var inventory = new FakeInventoryService();
        var item = ProfitableItem(1, 90, 110, qty: 10);
        // Nur Container (location_type "item", unaufgelöst) — kein Handelsplatz
        item.Locations =
        [
            new InventoryLocationAggregate
            {
                LocationId = 140000123, LocationType = "item", LocationFlag = "CorporationMarket", Quantity = 10
            }
        ];
        item.SellContexts =
        [
            new InventorySellContext
            {
                LocationId = 140000123, LocationType = "item",
                LocationLabel = "Container 140000123 (unaufgelöst)",
                Quantity = 10, IsBlocked = true,
                BlockReason = "Ort ist kein aufgelöster Handelsplatz"
            }
        ];
        inventory.Items.Add(item);
        var service = CreateService(db, inventory);

        var opportunities = await service.AnalyzeMarketDataAsync();

        // Unbekannter Ort blockiert die ortsgebundene Empfehlung — keine Opportunity
        Assert.Empty(opportunities);
    }

    [Fact]
    public async Task Analyze_PartlyUnresolvedAndPartlySellable_UsesOnlyResolvedVenueQuantity()
    {
        using var db = TestDb.Create();
        var inventory = new FakeInventoryService();
        var item = ProfitableItem(1, 90, 110, qty: 12);
        item.TotalQuantity = 12;
        item.Locations =
        [
            new InventoryLocationAggregate { LocationId = 60003466, LocationType = "station", LocationFlag = "Hangar", Quantity = 7 },
            new InventoryLocationAggregate { LocationId = 140000555, LocationType = "item", LocationFlag = "CorporationMarket", Quantity = 5 }
        ];
        item.SellContexts =
        [
            StationSellContext(1, 7),
            new InventorySellContext
            {
                LocationId = 140000555, LocationType = "item",
                LocationLabel = "Container 140000555 (unaufgelöst)",
                Quantity = 5, IsBlocked = true,
                BlockReason = "Ort ist kein aufgelöster Handelsplatz"
            }
        ];
        inventory.Items.Add(item);
        var service = CreateService(db, inventory);

        var opportunities = await service.AnalyzeMarketDataAsync();

        var opp = Assert.Single(opportunities);
        // Nur der aufgelöste Handelsplatz (7 Einheiten) fließt ein: 7 × 8.45 = 59.15
        Assert.Equal(59.15, opp.EstimatedProfit, 2);
        Assert.Equal(630.0, opp.RequiredCapital, 2);
        Assert.Equal(60003466, opp.SellLocationId);
    }

    // ------------------------------------------------------------------
    // Issue #28 (Review): bestehende aktive Opportunity wird entfernt, wenn
    // die ortsgebundene Empfehlung wegfällt (blockierter Ort / kein Gewinn)
    // ------------------------------------------------------------------

    private async Task SeedActiveOpportunity(WalletDbContext db, int typeId, double expiresInHours = 1)
    {
        db.TradingOpportunities.Add(new TradingOpportunity
        {
            CharacterId = CharacterId,
            TypeId = typeId,
            OpportunityType = "inventory_sell",
            BuyPrice = 90, SellPrice = 110,
            EstimatedProfit = 8450, RequiredCapital = 90_000, Score = 80,
            Provenance = "heuristic", Evidence = "old recommendation",
            DetectedAt = DateTime.UtcNow.AddHours(-1),
            ExpiresAt = DateTime.UtcNow.AddHours(expiresInHours),
            Status = "active"
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Analyze_ExistingOpportunity_AllSellContextsBlocked_InvalidatesStaleOpportunity()
    {
        using var db = TestDb.Create();
        await SeedActiveOpportunity(db, 1);

        var inventory = new FakeInventoryService();
        var item = ProfitableItem(1, 90, 110, qty: 10);
        // Einziger Lagerort ist ein unaufgelöster Container → einziger SellContext blockiert
        item.Locations =
        [
            new InventoryLocationAggregate
            {
                LocationId = 140000123, LocationType = "item", LocationFlag = "CorporationMarket", Quantity = 10
            }
        ];
        item.SellContexts =
        [
            new InventorySellContext
            {
                LocationId = 140000123, LocationType = "item",
                LocationLabel = "Container 140000123 (unaufgelöst)",
                Quantity = 10, IsBlocked = true,
                BlockReason = "Ort ist kein aufgelöster Handelsplatz"
            }
        ];
        inventory.Items.Add(item);
        var service = CreateService(db, inventory);

        var opportunities = await service.AnalyzeMarketDataAsync();

        // Issue #45: Die alte, an einen verschwundenen Handelsplatz gebundene Empfehlung
        // wird NICHT gelöscht — sie wird als "invalid" markiert (Quelle System) und ist
        // weder aktiv noch Teil des Analyse-Ergebnisses.
        Assert.DoesNotContain(opportunities, o => o.TypeId == 1);
        var activeCount = await db.TradingOpportunities.CountAsync(o => o.TypeId == 1 && o.Status == "active");
        Assert.Equal(0, activeCount);
        var invalidated = await db.TradingOpportunities.SingleAsync(o => o.TypeId == 1);
        Assert.Equal("invalid", invalidated.Status);
        Assert.Equal("old recommendation", invalidated.Evidence); // Empfehlung bleibt erhalten
        var change = await db.TradeStatusChanges.SingleAsync(c => c.TradingOpportunityId == invalidated.Id);
        Assert.Equal("invalid", change.ToStatus);
        Assert.Equal("system", change.Source);
    }

    [Fact]
    public async Task Analyze_ExistingOpportunity_NoLongerProfitable_InvalidatesStaleOpportunity()
    {
        using var db = TestDb.Create();
        await SeedActiveOpportunity(db, 2);

        var inventory = new FakeInventoryService();
        inventory.Items.Add(LossItem(2)); // Verlust → keine Opportunity mehr
        var service = CreateService(db, inventory);

        var opportunities = await service.AnalyzeMarketDataAsync();

        // Issue #45: Nicht mehr profitable Empfehlung wird als "invalid" markiert
        // (Quelle System) statt gelöscht — aktiv bleibt sie nicht.
        Assert.DoesNotContain(opportunities, o => o.TypeId == 2);
        var activeCount = await db.TradingOpportunities.CountAsync(o => o.TypeId == 2 && o.Status == "active");
        Assert.Equal(0, activeCount);
        var invalidated = await db.TradingOpportunities.SingleAsync(o => o.TypeId == 2);
        Assert.Equal("invalid", invalidated.Status);
        Assert.Equal("old recommendation", invalidated.Evidence); // Empfehlung bleibt erhalten
        var change = await db.TradeStatusChanges.SingleAsync(c => c.TradingOpportunityId == invalidated.Id);
        Assert.Equal("invalid", change.ToStatus);
    }

    [Fact]
    public async Task Analyze_ItemWithoutExecutableSellQuote_InvalidatesStaleOpportunity()
    {
        using var db = TestDb.Create();
        await SeedActiveOpportunity(db, 3);

        var inventory = new FakeInventoryService();
        // Cost Basis vorhanden, aber KEIN ausführbarer Verkaufs-Quote mehr
        // (fremde Region / veraltet / unvollständig) → BestSellPrice null.
        var item = ProfitableItem(3, 90, 110, qty: 10);
        item.BestSellPrice = null;
        item.BestBuyPrice = null;
        inventory.Items.Add(item);
        var service = CreateService(db, inventory);

        var opportunities = await service.AnalyzeMarketDataAsync();

        // Issue #45: ohne ausführbaren Quote wird die alte Empfehlung als "invalid"
        // markiert (Quelle System) statt gelöscht — aktiv bleibt sie nicht.
        Assert.DoesNotContain(opportunities, o => o.TypeId == 3);
        var activeCount = await db.TradingOpportunities.CountAsync(o => o.TypeId == 3 && o.Status == "active");
        Assert.Equal(0, activeCount);
        var invalidated = await db.TradingOpportunities.SingleAsync(o => o.TypeId == 3);
        Assert.Equal("invalid", invalidated.Status);
        Assert.Equal("old recommendation", invalidated.Evidence); // Empfehlung bleibt erhalten
        var change = await db.TradeStatusChanges.SingleAsync(c => c.TradingOpportunityId == invalidated.Id);
        Assert.Equal("invalid", change.ToStatus);
        Assert.Equal("system", change.Source);
    }

    // ------------------------------------------------------------------
    // Issue #33: ehrliche Provenienz statt erfundener AI-Confidence
    // ------------------------------------------------------------------

    [Fact]
    public async Task Analyze_NewOpportunity_HasHonestProvenanceNotAiClaim()
    {
        using var db = TestDb.Create();
        var inventory = new FakeInventoryService();
        // Keine Cost-Basis-Quelle gesetzt → Datenqualität "partial" (nicht gesichert)
        inventory.Items.Add(ProfitableItem(1, 90, 110));
        // Gesicherte Cost-Basis (Echt) → Datenqualität "complete"
        var secure = ProfitableItem(2, 90, 110);
        secure.CostBasisSourceLabel = "Echt";
        inventory.Items.Add(secure);
        var service = CreateService(db, inventory);

        var opportunities = await service.AnalyzeMarketDataAsync();

        // Neuer Datensatz behauptet NICHT AI-Erzeugung: Provenienz ist die
        // deterministische Heuristik, keine AI-Model-Angabe, keine AI-Confidence.
        var partial = opportunities.Single(o => o.TypeId == 1);
        Assert.Equal(TradingOpportunity.ProvenanceHeuristic, partial.Provenance);
        Assert.Equal(MarketAnalysisService.AlgorithmVersionInventorySell, partial.AlgorithmVersion);
        Assert.Equal("partial", partial.DataQuality);
        Assert.False(string.IsNullOrWhiteSpace(partial.Evidence));
        Assert.Contains("ROI", partial.Evidence);

        var complete = opportunities.Single(o => o.TypeId == 2);
        Assert.Equal("complete", complete.DataQuality);

        // Score ist der dokumentierte Heuristik-Wert aus ROI, nicht "Confidence":
        // ROI = 8.45/90 = 9.3889% → 55 + 9.3889×1.5 = 69.08 (kein Clamp nötig)
        var roi = (8.45 / 90.0) * 100;
        Assert.Equal(Math.Clamp(55 + (roi * 1.5), 55, 95), partial.Score, 2);
    }

    [Fact]
    public async Task GetActive_LegacyOpportunity_PreservedAndFlaggedAsLegacy()
    {
        using var db = TestDb.Create();
        // Legacy-Datensatz (vor Provenienz-Erfassung, z.B. via Migration übernommen):
        // Score-Wert aus der AI-Confidence-Ära, Provenance "legacy", keine Algorithmusversion.
        db.TradingOpportunities.Add(new TradingOpportunity
        {
            CharacterId = CharacterId,
            TypeId = 9,
            OpportunityType = "inventory_sell",
            BuyPrice = 90, SellPrice = 95,
            EstimatedProfit = 1, RequiredCapital = 90, Score = 70,
            Provenance = TradingOpportunity.ProvenanceLegacy,
            Evidence = "alte Berechnung",
            DetectedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            Status = "active"
        });
        await db.SaveChangesAsync();
        var service = CreateService(db, new FakeInventoryService());

        var active = await service.GetActiveOpportunitiesAsync(CharacterId);

        // Datensatz bleibt erhalten; er wird als Legacy geflaggt und der Score
        // wird NICHT als neue Evidenz behandelt (keine Algorithmusversion).
        var opp = Assert.Single(active);
        Assert.Equal(70, opp.Score);
        Assert.Equal(TradingOpportunity.ProvenanceLegacy, opp.Provenance);
        Assert.Null(opp.AlgorithmVersion);
    }
}