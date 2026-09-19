using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using WALLEve.Data;
using WALLEve.Models.Database;
using WALLEve.Models.Esi.Alliance;
using WALLEve.Models.Esi.Character;
using WALLEve.Models.Esi.Corporation;
using WALLEve.Models.Esi.Markets;
using WALLEve.Models.Measurement;
using WALLEve.Models.Esi.Universe;
using WALLEve.Models.Esi.Wallet;
using WALLEve.Models.Market;
using WALLEve.Models.Sde;
using WALLEve.Services.Esi.Interfaces;
using WALLEve.Services.Market;
using WALLEve.Services.Market.Interfaces;
using WALLEve.Services.Sde.Interfaces;

namespace WALLEve.Tests;

/// <summary>
/// Regression-Tests Issue #27: verlustfreie Inventar-Rohdaten und explizite
/// Ortsprojektionen. Zwei Stationen desselben Typs ergeben zwei Ortszeilen
/// und ein mengenrichtiges Typ-Aggregat; Container-Abstammung und
/// unaufgelöste LocationIds bleiben ohne erfundene Zuordnung erhalten.
/// </summary>
public class InventoryServiceTests
{
    private const int CharacterId = 90073315;

    private sealed class FakeEsiApiService : IEsiApiService
    {
        public List<CharacterAsset> Assets { get; set; } = new();
        public CharacterSkills? Skills { get; set; } = new();
        public List<MarketPrice>? Prices { get; set; }

        public Task<List<CharacterAsset>?> GetCharacterAssetsAsync(int characterId, CancellationToken ct = default)
            => Task.FromResult<List<CharacterAsset>?>(Assets);

        public Task<List<CharacterMiningEntry>?> GetCharacterMiningLedgerAsync(int characterId, CancellationToken ct = default) => throw new NotImplementedException();

        public Task<List<CharacterBlueprint>?> GetCharacterBlueprintsAsync(int characterId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<CharacterIndustryJob>?> GetCharacterIndustryJobsAsync(int characterId, CancellationToken ct = default) => throw new NotImplementedException();

        public Task<CharacterSkills?> GetCharacterSkillsAsync()
            => Task.FromResult(Skills);
        public Task<List<MarketPrice>?> GetMarketPricesAsync()
            => Task.FromResult(Prices);

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
        public Task<List<RegionalMarketOrder>?> GetAllRegionalMarketOrdersAsync(int regionId, int? typeId = null, string orderType = "all", CancellationToken ct = default, Action<RegionalScanPageTelemetry>? telemetrySink = null) => throw new NotImplementedException();
        public Task<List<MarketHistoryEntry>?> GetMarketHistoryAsync(int regionId, int typeId) => throw new NotImplementedException();
    }

    private sealed class FakeSdeUniverseService : ISdeUniverseService
    {
        public bool IsAvailable { get; set; } = false;
        public Dictionary<long, int> RegionByLocation { get; set; } = new();

        public Task<bool> IsDatabaseAvailableAsync() => Task.FromResult(IsAvailable);
        public Task<int?> GetRegionIdForLocationAsync(long locationId)
            => Task.FromResult(RegionByLocation.TryGetValue(locationId, out var region) ? (int?)region : null);
        public Task<int?> GetSolarSystemIdForLocationAsync(long locationId) => Task.FromResult<int?>(null);
        public Task<string?> GetTypeNameAsync(int typeId) => Task.FromResult<string?>(null);
        public Task<string?> GetTypeGroupAsync(int typeId) => Task.FromResult<string?>(null);
        public Task<Dictionary<int, string?>> GetTypeGroupsAsync(IReadOnlyCollection<int> typeIds) => Task.FromResult(new Dictionary<int, string?>());
        public Task<SolarSystemInfo?> GetSolarSystemAsync(int solarSystemId) => Task.FromResult<SolarSystemInfo?>(null);
        public Task<StationInfo?> GetStationAsync(long stationId) => Task.FromResult<StationInfo?>(null);
        public Task<string?> GetRegionNameAsync(int regionId) => Task.FromResult<string?>(null);
        public Task<string?> GetLocationNameAsync(long locationId) => Task.FromResult<string?>(null);
        public Task<Dictionary<int, string>> GetAllMarketItemsAsync() => Task.FromResult(new Dictionary<int, string>());
        public Task<Dictionary<int, string>> GetAllRegionsAsync() => Task.FromResult(new Dictionary<int, string>());
        public Task<Dictionary<int, string>> SearchSolarSystemsAsync(string searchQuery, int maxResults = 10) => Task.FromResult(new Dictionary<int, string>());
    }

    private sealed class FakeHubSelectionService : IHubSelectionService
    {
        public MarketHubProfile? ComparisonMarket { get; set; }

        public Task<List<MarketHubProfile>> GetProfilesAsync(CancellationToken ct = default)
            => Task.FromResult(new List<MarketHubProfile>());
        public Task<MarketHubProfile?> GetComparisonMarketAsync(CancellationToken ct = default)
            => Task.FromResult(ComparisonMarket);
        public Task SaveProfileAsync(MarketHubProfile profile, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteProfileAsync(int profileId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<HubSelectionResult> SelectNearestActiveHubAsync(int fromSystemId, CancellationToken ct = default)
            => Task.FromResult(new HubSelectionResult { GraphAvailable = true });
    }

    private static InventoryService CreateService(FakeEsiApiService esi, WalletDbContext? db = null, FakeSdeUniverseService? sde = null, FakeHubSelectionService? hub = null)
        => new(
            esi,
            sde ?? new FakeSdeUniverseService(),
            new FeeCalculatorService(),
            hub ?? new FakeHubSelectionService(),
            db ?? TestDb.Create(),
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<InventoryService>.Instance);

    private static CharacterAsset Asset(long itemId, int typeId, int quantity, long locationId, string locationType, string locationFlag = "Hangar")
        => new()
        {
            ItemId = itemId, TypeId = typeId, Quantity = quantity,
            LocationId = locationId, LocationType = locationType, LocationFlag = locationFlag
        };

    private static MarketSnapshot Snapshot(int typeId, int regionId, DateTime timestamp, double? buy = null, double? sell = null)
        => new()
        {
            RegionId = regionId, TypeId = typeId, Timestamp = timestamp,
            BestBuyPrice = buy, BestSellPrice = sell
        };

    private static FakeSdeUniverseService SdeWithRegions(params (long LocationId, int RegionId)[] entries)
        => new() { IsAvailable = true, RegionByLocation = entries.ToDictionary(e => e.LocationId, e => e.RegionId) };

    [Fact]
    public async Task GetInventoryAsync_SameTypeAtTwoStations_TwoLocationRowsAndQuantityCorrectTypeAggregate()
    {
        var esi = new FakeEsiApiService
        {
            Assets = new List<CharacterAsset>
            {
                Asset(1, 1234, 5, 60003466, "station"),
                Asset(2, 1234, 3, 60003760, "station")
            },
            Prices = new List<MarketPrice> { new() { TypeId = 1234, AdjustedPrice = 1000, AveragePrice = 1000 } }
        };
        var service = CreateService(esi);

        var result = await service.GetInventoryAsync(CharacterId);

        // Owner/Type-Aggregat: EINE Zeile, mengenrichtig über beide Orte summiert
        var item = Assert.Single(result);
        Assert.Equal(CharacterId, item.OwnerCharacterId);
        Assert.Equal(1234, item.TypeId);
        Assert.Equal(8, item.TotalQuantity);

        // Owner/Type/Location-Aggregat: zwei Ortszeilen statt eines fiktiven Primary
        Assert.Equal(2, item.Locations.Count);
        Assert.Contains(item.Locations, l =>
            l.LocationId == 60003466 && l.LocationType == "station" && l.Quantity == 5);
        Assert.Contains(item.Locations, l =>
            l.LocationId == 60003760 && l.LocationType == "station" && l.Quantity == 3);

        // Roh-Assets bleiben je Ort verlustfrei erhalten
        var stationA = item.Locations.Single(l => l.LocationId == 60003466);
        Assert.Single(stationA.RawAssets);
        Assert.Equal(1, stationA.RawAssets[0].ItemId);
    }

    [Fact]
    public async Task GetInventoryAsync_NoPrices_StillExposesLocationRows()
    {
        var esi = new FakeEsiApiService
        {
            Assets = new List<CharacterAsset>
            {
                Asset(1, 1111, 10, 60003466, "station"),
                Asset(2, 1111, 2, 60003760, "station")
            },
            Prices = null
        };
        var service = CreateService(esi);

        var result = await service.GetInventoryAsync(CharacterId);

        var item = Assert.Single(result);
        Assert.Equal(12, item.TotalQuantity);
        Assert.Equal(2, item.Locations.Count);
        Assert.Equal(10, item.Locations.Single(l => l.LocationId == 60003466).Quantity);
        Assert.Equal(2, item.Locations.Single(l => l.LocationId == 60003760).Quantity);
        Assert.Equal(2, item.RawAssets.Count);
    }

    [Fact]
    public async Task GetInventoryAsync_ContainerItems_PreserveAncestryAndUnresolvedIds()
    {
        const long containerItemId = 1000000000001; // Container-ItemId, nicht auflösbar als Station
        var esi = new FakeEsiApiService
        {
            Assets = new List<CharacterAsset>
            {
                Asset(11, 2222, 2, containerItemId, "item"),          // im Container
                Asset(12, 2222, 4, 60003466, "station")
            }
        };
        var service = CreateService(esi);

        var result = await service.GetInventoryAsync(CharacterId);

        var item = Assert.Single(result);
        Assert.Equal(6, item.TotalQuantity);
        Assert.Equal(2, item.Locations.Count);

        // Container-Zeile: unaufgelöste LocationId bleibt als Rohwert erhalten
        var containerRow = item.Locations.Single(l => l.LocationId == containerItemId);
        Assert.Equal("item", containerRow.LocationType);
        Assert.Equal(2, containerRow.Quantity);

        // Container-Abstammung wird abgeleitet, ohne erfundene Zuordnung
        var containerAsset = containerRow.RawAssets.Single();
        Assert.Equal(containerItemId, containerAsset.ParentItemId);

        // Station-Zeile: kein Parent, LocationId unverändert
        var stationRow = item.Locations.Single(l => l.LocationId == 60003466);
        Assert.Equal("station", stationRow.LocationType);
        Assert.Null(stationRow.RawAssets.Single().ParentItemId);
        Assert.Equal(60003466, stationRow.RawAssets.Single().LocationId);
    }

    [Fact]
    public async Task GetInventoryAsync_MultipleAssetsSameTypeSameLocation_OneQuantityCorrectLocationRow()
    {
        // Mehrere Rohzeilen desselben Typs am selben Ort ergeben EINE Ortszeile
        // mit summierter Menge und behalten jede Rohzeile.
        var esi = new FakeEsiApiService
        {
            Assets = new List<CharacterAsset>
            {
                Asset(1, 9999, 7, 60003466, "station"),
                Asset(2, 9999, 7, 60003466, "station")
            },
            Prices = new List<MarketPrice> { new() { TypeId = 9999, AdjustedPrice = 100, AveragePrice = 100 } }
        };
        var service = CreateService(esi);

        var result = await service.GetInventoryAsync(CharacterId);

        var item = Assert.Single(result);
        Assert.Equal(CharacterId, item.OwnerCharacterId);
        Assert.Equal(14, item.TotalQuantity);
        var location = Assert.Single(item.Locations);
        Assert.Equal(14, location.Quantity);
        Assert.Equal(2, location.RawAssets.Count);
    }

    // ------------------------------------------------------------------
    // Issue #28: ortsgebundener Handlungskontext — Mengen mehrerer Orte
    // werden nie still als ein verkaufbarer Stapel behandelt
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetInventoryAsync_SameTypeAtTwoStations_SeparateSellContextsWithOwnProceeds()
    {
        var db = TestDb.Create();
        db.MarketSnapshots.AddRange(
            Snapshot(1234, 10000002, DateTime.UtcNow.AddMinutes(-1), buy: 900, sell: 1000), // The Forge (Jita)
            Snapshot(1234, 10000043, DateTime.UtcNow.AddMinutes(-1), buy: 900, sell: 1000)); // Domain (Amarr)
        await db.SaveChangesAsync();

        var esi = new FakeEsiApiService
        {
            Assets = new List<CharacterAsset>
            {
                Asset(1, 1234, 5, 60003466, "station"),
                Asset(2, 1234, 3, 60003760, "station")
            },
            Prices = new List<MarketPrice> { new() { TypeId = 1234, AdjustedPrice = 1000, AveragePrice = 1000 } }
        };
        var service = CreateService(esi, db, SdeWithRegions((60003466, 10000002), (60003760, 10000043)));

        var result = await service.GetInventoryAsync(CharacterId);

        var item = Assert.Single(result);

        // AC1: gleicher Typ an zwei Stationen → getrennter Handlungskontext je Ort
        Assert.Equal(2, item.SellContexts.Count);
        Assert.True(item.HasMultipleLocations);

        var ctxA = item.SellContexts.Single(c => c.LocationId == 60003466);
        var ctxB = item.SellContexts.Single(c => c.LocationId == 60003760);
        Assert.Equal(5, ctxA.Quantity);
        Assert.Equal(3, ctxB.Quantity);
        Assert.Equal(1000, ctxA.SellPrice);
        Assert.Equal(1000, ctxB.SellPrice);
        Assert.False(ctxA.IsBlocked);
        Assert.False(ctxB.IsBlocked);

        // Jeder Kontext trägt seinen eigenen Netto-Erlös (Fehler 3% + Steuer 7,5%,
        // ohne Skills → Nettofaktor 0,895): 5 × 895 = 4475, 3 × 895 = 2685
        Assert.Equal(4475.0, ctxA.EstimatedNetProceeds!.Value, 2);
        Assert.Equal(2685.0, ctxB.EstimatedNetProceeds!.Value, 2);

        // Ausführbarer Quote kommt aus dem frischen Snapshot der eigenen Region
        // (pro Ort Regions-Quote), nicht aus einer Referenz
        Assert.Equal(900, item.BestBuyPrice);
        Assert.Equal(1000, item.BestSellPrice);
        Assert.Equal("snapshot", item.BuyPriceSource);
        Assert.Equal("snapshot", item.SellPriceSource);

        // AC2: Summen stimmen mit Rohdaten überein (Kontexte + Aggregat + Roh-Assets)
        Assert.Equal(8, item.SellContexts.Sum(c => c.Quantity));
        Assert.Equal(item.TotalQuantity, item.SellContexts.Sum(c => c.Quantity));
        Assert.Equal(item.TotalQuantity, esi.Assets.Sum(a => a.Quantity));

        // Kein stiller verkaufbarer Stapel: aggregierte Simulation ist gesperrt
        Assert.False(item.CanSimulateAggregate);
        Assert.Equal(8, item.ResolvedSellQuantity);
        Assert.False(string.IsNullOrEmpty(item.SellContextNote));
        Assert.DoesNotContain("Station 60003466", item.SellContextNote); // zwei Stationen → Orts-Hinweis
    }

    [Fact]
    public async Task GetInventoryAsync_ContainerAndStation_MixedSellContextsAndSumsMatchRawData()
    {
        const long containerItemId = 1000000000001;
        var db = TestDb.Create();
        db.MarketSnapshots.Add(Snapshot(2222, 10000002, DateTime.UtcNow.AddMinutes(-1), buy: 450, sell: 500));
        await db.SaveChangesAsync();

        var esi = new FakeEsiApiService
        {
            Assets = new List<CharacterAsset>
            {
                Asset(11, 2222, 2, containerItemId, "item"),          // Container: blockiert
                Asset(12, 2222, 4, 60003466, "station")               // Station: verkaufbar
            },
            Prices = new List<MarketPrice> { new() { TypeId = 2222, AdjustedPrice = 500, AveragePrice = 500 } }
        };
        var service = CreateService(esi, db, SdeWithRegions((60003466, 10000002)));

        var result = await service.GetInventoryAsync(CharacterId);

        var item = Assert.Single(result);

        // AC2: Summen stimmen mit Rohdaten überein
        Assert.Equal(6, item.TotalQuantity);
        Assert.Equal(6, item.SellContexts.Sum(c => c.Quantity));
        Assert.Equal(6, esi.Assets.Sum(a => a.Quantity));

        // Container/Ort unbekannt → ortsgebundene Empfehlung blockiert
        var containerCtx = item.SellContexts.Single(c => c.LocationId == containerItemId);
        Assert.True(containerCtx.IsBlocked);
        Assert.NotNull(containerCtx.BlockReason);
        Assert.Null(containerCtx.EstimatedNetProceeds);
        Assert.True(item.HasUnresolvedLocation);
        Assert.False(item.CanSimulateAggregate);

        // Station bleibt verkaufbar, nur mit ihrer eigenen Menge (4 × 500 × 0,895 = 1790)
        var stationCtx = item.SellContexts.Single(c => c.LocationId == 60003466);
        Assert.False(stationCtx.IsBlocked);
        Assert.Equal(4, stationCtx.Quantity);
        Assert.Equal(1790.0, stationCtx.EstimatedNetProceeds!.Value, 2);
        Assert.Equal(4, item.ResolvedSellQuantity);
        Assert.False(string.IsNullOrEmpty(item.SellContextNote));
    }

    [Fact]
    public async Task GetInventoryAsync_OnlyContainerLocation_NoSellableContextAtAll()
    {
        const long containerItemId = 1000000000002;
        var esi = new FakeEsiApiService
        {
            Assets = new List<CharacterAsset>
            {
                Asset(11, 3333, 9, containerItemId, "item")
            }
        };
        var service = CreateService(esi);

        var result = await service.GetInventoryAsync(CharacterId);

        var item = Assert.Single(result);
        var ctx = Assert.Single(item.SellContexts);
        Assert.True(ctx.IsBlocked);
        Assert.Null(ctx.EstimatedNetProceeds);
        Assert.Equal(0, item.ResolvedSellQuantity);
        Assert.False(item.CanSimulateAggregate);
        Assert.True(item.HasUnresolvedLocation);
        // Summen stimmen weiterhin mit den Rohdaten überein
        Assert.Equal(9, item.TotalQuantity);
        Assert.Equal(9, item.SellContexts.Sum(c => c.Quantity));
    }

    // ------------------------------------------------------------------
    // Issue #29: Quotes sind regions-, orts- und frischheitsgebunden —
    // kein fremder Regionspreis, kein synthetischer Schätzkaufpreis,
    // keine ausführbare Empfehlung aus veralteten/unvollständigen Quotes
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetInventoryAsync_SnapshotOnlyInOtherRegion_AssetDoesNotAdoptForeignRegionPrice()
    {
        var db = TestDb.Create();
        // Quote existiert nur in The Forge (Jita) — das Asset liegt in Domain (Amarr).
        db.MarketSnapshots.Add(Snapshot(4444, 10000002, DateTime.UtcNow.AddMinutes(-1), buy: 900, sell: 1000));
        await db.SaveChangesAsync();

        var esi = new FakeEsiApiService
        {
            Assets = new List<CharacterAsset> { Asset(1, 4444, 10, 60003760, "station") },
            Prices = new List<MarketPrice> { new() { TypeId = 4444, AdjustedPrice = 950, AveragePrice = 950 } }
        };
        var service = CreateService(esi, db, SdeWithRegions((60003760, 10000043)));

        var result = await service.GetInventoryAsync(CharacterId);

        var item = Assert.Single(result);

        // AC1: kein fremder Regionspreis — weder ausführbar noch als Ortskontext
        Assert.Null(item.BestBuyPrice);
        Assert.Null(item.BestSellPrice);
        Assert.Null(item.SellContexts.Single().SellPrice);
        Assert.Null(item.SellContexts.Single().EstimatedNetProceeds);
        Assert.NotEqual("sell", item.Recommendation);
        Assert.NotEqual("snapshot", item.SellPriceSource);

        // Referenzpreis bleibt als Bewertung erhalten, ist aber nicht ausführbar
        Assert.Equal(950, item.AveragePrice);
    }

    [Fact]
    public async Task GetInventoryAsync_NoExecutableQuote_RemainsUnknownInsteadOfSyntheticBuyPrice()
    {
        // Keine Snapshots, aber ESI-MarketPrices vorhanden: der frühere
        // adjusted_price * 0.95-Schätzkaufpreis darf NICHT mehr entstehen.
        var esi = new FakeEsiApiService
        {
            Assets = new List<CharacterAsset> { Asset(1, 5555, 4, 60003466, "station") },
            Prices = new List<MarketPrice> { new() { TypeId = 5555, AdjustedPrice = 1000, AveragePrice = 1000 } }
        };
        var service = CreateService(esi, TestDb.Create(), SdeWithRegions((60003466, 10000002)));

        var result = await service.GetInventoryAsync(CharacterId);

        var item = Assert.Single(result);

        // AC2: Unknown statt Null/Schätzkaufpreis
        Assert.Null(item.BestBuyPrice);   // kein 950 (= 1000 × 0,95)
        Assert.Null(item.BestSellPrice);
        Assert.Null(item.BuyPriceSource);
        Assert.Null(item.SellPriceSource);
        Assert.Equal(1000, item.AveragePrice); // Referenz bleibt Bewertung
        Assert.Equal("watch", item.Recommendation);
        Assert.Contains("Region", item.RecommendationReason);
    }

    [Fact]
    public async Task GetInventoryAsync_StaleSnapshot_NoExecutableRecommendation()
    {
        var db = TestDb.Create();
        // Ausführbarer Quote derselben Region, aber 12 Stunden alt → stale.
        db.MarketSnapshots.Add(Snapshot(6666, 10000002, DateTime.UtcNow.AddHours(-12), buy: 900, sell: 1000));
        await db.SaveChangesAsync();

        var esi = new FakeEsiApiService
        {
            Assets = new List<CharacterAsset> { Asset(1, 6666, 50, 60003466, "station") },
            Prices = new List<MarketPrice> { new() { TypeId = 6666, AdjustedPrice = 1000, AveragePrice = 1000 } }
        };
        var service = CreateService(esi, db, SdeWithRegions((60003466, 10000002)));

        var result = await service.GetInventoryAsync(CharacterId);

        var item = Assert.Single(result);

        // AC3: veralteter Quote löst keine ausführbare Empfehlung aus
        Assert.Null(item.BestSellPrice);
        Assert.Null(item.BestBuyPrice);
        Assert.Null(item.SellContexts.Single().SellPrice);
        Assert.NotEqual("sell", item.Recommendation);
        Assert.Contains("veraltet", item.RecommendationReason);
    }

    [Fact]
    public async Task GetInventoryAsync_PartialSnapshot_MissingSellSide_NoExecutableRecommendation()
    {
        var db = TestDb.Create();
        // Frischer Quote, aber nur die Kaufseite (kein Sell-Order) → partial.
        db.MarketSnapshots.Add(Snapshot(7777, 10000002, DateTime.UtcNow.AddMinutes(-1), buy: 900, sell: null));
        await db.SaveChangesAsync();

        var esi = new FakeEsiApiService
        {
            Assets = new List<CharacterAsset> { Asset(1, 7777, 20, 60003466, "station") },
            Prices = new List<MarketPrice> { new() { TypeId = 7777, AdjustedPrice = 1000, AveragePrice = 1000 } }
        };
        var service = CreateService(esi, db, SdeWithRegions((60003466, 10000002)));

        var result = await service.GetInventoryAsync(CharacterId);

        var item = Assert.Single(result);

        // AC3: unvollständiger Quote ohne Verkaufsseite → keine Verkaufsempfehlung
        Assert.Null(item.BestSellPrice);
        Assert.Null(item.SellContexts.Single().SellPrice);
        Assert.NotEqual("sell", item.Recommendation);
        Assert.Equal(0, item.OpportunityScore);
    }

    [Fact]
    public async Task GetInventoryAsync_FreshMatchingRegionSnapshot_ExecutablePricesApplied()
    {
        var db = TestDb.Create();
        db.MarketSnapshots.AddRange(
            Snapshot(8888, 10000002, DateTime.UtcNow.AddMinutes(-30), buy: 700, sell: 800),  // älter, andere Region
            Snapshot(8888, 10000043, DateTime.UtcNow.AddMinutes(-1), buy: 900, sell: 1000)); // frisch, Asset-Region
        await db.SaveChangesAsync();

        var esi = new FakeEsiApiService
        {
            Assets = new List<CharacterAsset> { Asset(1, 8888, 6, 60003760, "station") },
            Prices = new List<MarketPrice> { new() { TypeId = 8888, AdjustedPrice = 950, AveragePrice = 950 } }
        };
        var service = CreateService(esi, db, SdeWithRegions((60003760, 10000043)));

        var result = await service.GetInventoryAsync(CharacterId);

        var item = Assert.Single(result);

        // Positive Gegenprobe: der frische Quote der eigenen Region wird genutzt,
        // nicht der (ältere) Quote der fremden Region.
        Assert.Equal(900, item.BestBuyPrice);
        Assert.Equal(1000, item.BestSellPrice);
        Assert.Equal("snapshot", item.BuyPriceSource);
        Assert.Equal("snapshot", item.SellPriceSource);
        Assert.Equal(1000, item.SellContexts.Single().SellPrice);
        Assert.Equal(6 * 1000 * 0.895, item.SellContexts.Single().EstimatedNetProceeds!.Value, 2);
        Assert.NotNull(item.SpreadPercent);
        Assert.Equal(((1000.0 - 900.0) / 900.0) * 100, item.SpreadPercent!.Value, 3);
    }

    [Fact]
    public async Task GetInventoryAsync_UnresolvableLocationRegion_NoExecutableQuote()
    {
        // Container ohne auflösbare Region: selbst ein frischer Quote irgendeiner
        // Region darf nicht angewendet werden (keine erfundene Ortszuordnung).
        var db = TestDb.Create();
        db.MarketSnapshots.Add(Snapshot(9999, 10000002, DateTime.UtcNow.AddMinutes(-1), buy: 10, sell: 20));
        await db.SaveChangesAsync();

        var esi = new FakeEsiApiService
        {
            Assets = new List<CharacterAsset> { Asset(1, 9999, 3, 1000000000003, "item") },
            Prices = new List<MarketPrice> { new() { TypeId = 9999, AdjustedPrice = 20, AveragePrice = 20 } }
        };
        var service = CreateService(esi, db, SdeWithRegions((60003466, 10000002)));

        var result = await service.GetInventoryAsync(CharacterId);

        var item = Assert.Single(result);
        Assert.Null(item.BestBuyPrice);
        Assert.Null(item.BestSellPrice);
        Assert.True(item.SellContexts.Single().IsBlocked);
    }

    [Fact]
    public async Task GetInventoryAsync_OwnQuantityChanging_DoesNotChangeLiquidityScore()
    {
        // Issue #30, AC1: Die eigene Besitzmenge ist KEIN Liquiditätssignal mehr.
        // Identische Marktliquidität (kumulierte Snapshot-Tiefe + frische History)
        // muss bei 10 und bei 500.000 Einheiten denselben Score ergeben.
        async Task<InventoryItem> LoadAsync(int characterId, int quantity)
        {
            var db = TestDb.Create();
            var snapshot = Snapshot(7777, 10000002, DateTime.UtcNow.AddMinutes(-1), buy: 90, sell: 100);
            snapshot.SellVolume = 2000; // kumulierte Regions-Tiefe über viele Orders
            db.MarketSnapshots.Add(snapshot);
            db.MarketHistory.Add(new MarketHistory
            {
                RegionId = 10000002, TypeId = 7777, Date = DateTime.UtcNow.Date,
                Average = 95, Highest = 100, Lowest = 90, Volume = 60_000, OrderCount = 12
            });
            await db.SaveChangesAsync();

            var esi = new FakeEsiApiService
            {
                Assets = new List<CharacterAsset> { Asset(1, 7777, quantity, 60003466, "station") },
                Prices = new List<MarketPrice> { new() { TypeId = 7777, AdjustedPrice = 95, AveragePrice = 95 } }
            };
            var service = CreateService(esi, db, SdeWithRegions((60003466, 10000002)));

            return Assert.Single(await service.GetInventoryAsync(characterId));
        }

        var small = await LoadAsync(90073315, 10);
        var huge = await LoadAsync(90073316, 500_000);

        Assert.NotNull(small.OpportunityScore);
        Assert.True(small.OpportunityScore > 0);
        Assert.Equal(small.OpportunityScore, huge.OpportunityScore);
        Assert.Equal(small.Recommendation, huge.Recommendation);
        // Liquidität ist bekannt (Tiefe + frische History) — keine Unknown-Behauptung
        Assert.DoesNotContain("Liquidität unbekannt", small.RecommendationReason ?? string.Empty);
    }

    [Fact]
    public async Task GetInventoryAsync_StaleHistory_DoesNotClaimLiquidity()
    {
        // Issue #30, AC3: Fehlende/veraltete History wird nicht als liquide interpretiert.
        // Ohne History gibt es keinen Liquiditätszuschlag und keine Liquiditätsbehauptung.
        var db = TestDb.Create();
        var snapshot = Snapshot(7778, 10000002, DateTime.UtcNow.AddMinutes(-1), buy: 90, sell: 100);
        snapshot.SellVolume = 5000;
        db.MarketSnapshots.Add(snapshot);
        db.MarketHistory.Add(new MarketHistory
        {
            RegionId = 10000002, TypeId = 7778, Date = DateTime.UtcNow.Date.AddDays(-120),
            Average = 95, Highest = 100, Lowest = 90, Volume = 60_000, OrderCount = 12
        });
        await db.SaveChangesAsync();

        var esi = new FakeEsiApiService
        {
            Assets = new List<CharacterAsset> { Asset(1, 7778, 100, 60003466, "station") },
            Prices = new List<MarketPrice> { new() { TypeId = 7778, AdjustedPrice = 95, AveragePrice = 95 } }
        };
        var service = CreateService(esi, db, SdeWithRegions((60003466, 10000002)));

        var item = Assert.Single(await service.GetInventoryAsync(90073317));

        Assert.NotNull(item.OpportunityScore);
        // Veraltete History (120 Tage) wird nicht als liquide interpretiert: die
        // Empfehlung benennt die fehlende frische History explizit (#30, AC3).
        Assert.Contains("History fehlt oder ist veraltet", item.RecommendationReason ?? string.Empty);
    }

    // ------------------------------------------------------------------
    // Issue #63: kontextgebundene Vergleichs-Quotes in den Holdings
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetInventoryAsync_ComparisonMarket_AddsSeparateComparisonQuoteAndPreservesExecutableProvenance()
    {
        // Asset in Region 10000002 (frischer zweiseitiger Snapshot), Vergleichsmarkt
        // „Jita" in Region 10000043 mit eigenem Snapshot und anderen Preisen.
        var db = TestDb.Create();
        db.MarketSnapshots.Add(Snapshot(6543, 10000002, DateTime.UtcNow.AddMinutes(-1), buy: 90, sell: 110));
        var jitaSnap = Snapshot(6543, 10000043, DateTime.UtcNow.AddMinutes(-2), buy: 80, sell: 100);
        jitaSnap.BuyVolume = 500_000;
        jitaSnap.SellVolume = 300_000;
        db.MarketSnapshots.Add(jitaSnap);
        await db.SaveChangesAsync();

        var hub = new FakeHubSelectionService
        {
            ComparisonMarket = new MarketHubProfile
            {
                Id = 7, Name = "Jita", RegionId = 10000043, SystemId = 30000142, IsComparisonMarket = true
            }
        };
        var esi = new FakeEsiApiService
        {
            Assets = new List<CharacterAsset> { Asset(1, 6543, 10, 60003466, "station") },
            Prices = new List<MarketPrice> { new() { TypeId = 6543, AdjustedPrice = 95, AveragePrice = 95 } }
        };
        var service = CreateService(esi, db, SdeWithRegions((60003466, 10000002)), hub);

        var item = Assert.Single(await service.GetInventoryAsync(CharacterId));

        // Ausführbarer Quote bleibt ausschließlich der Asset-Region (#63, AC3: Originalprovenienz erhalten).
        Assert.Equal(90, item.BestBuyPrice);
        Assert.Equal(110, item.BestSellPrice);
        Assert.Equal("snapshot", item.BuyPriceSource);

        // Vergleichs-Quote ist separat und vollständig (Seiten, Tiefe, Quelle).
        Assert.NotNull(item.ComparisonQuote);
        var cmp = item.ComparisonQuote!;
        Assert.Equal("Jita", cmp.MarketName);
        Assert.Equal(80, cmp.BestBuyPrice);
        Assert.Equal(100, cmp.BestSellPrice);
        Assert.Equal(500_000, cmp.BuyVolume);
        Assert.Equal(300_000, cmp.SellVolume);
        Assert.False(cmp.IsStale);
        Assert.NotEqual(item.BestSellPrice, cmp.BestSellPrice);
        Assert.Equal(string.Empty, cmp.Note);
        Assert.Equal("market-snapshot", cmp.Source);
    }

    [Fact]
    public async Task GetInventoryAsync_ComparisonMarketWithoutSnapshot_ReferenceOnlyNeverExecutable()
    {
        // Asset-Region hat frischen Snapshot; die Vergleichsmarkt-Region hat KEINEN
        // Snapshot, aber einen ESI-Referenzpreis (#63, AC1 + AC2: kein fremder
        // Regionspreis, Reference Price erscheint nie als ausführbarer Quote).
        var db = TestDb.Create();
        db.MarketSnapshots.Add(Snapshot(6544, 10000002, DateTime.UtcNow.AddMinutes(-1), buy: 90, sell: 110));
        await db.SaveChangesAsync();

        var hub = new FakeHubSelectionService
        {
            ComparisonMarket = new MarketHubProfile
            {
                Id = 8, Name = "Amarr", RegionId = 10000043, SystemId = 30002187, IsComparisonMarket = true
            }
        };
        var esi = new FakeEsiApiService
        {
            Assets = new List<CharacterAsset> { Asset(1, 6544, 10, 60003466, "station") },
            Prices = new List<MarketPrice> { new() { TypeId = 6544, AdjustedPrice = 95, AveragePrice = 95 } }
        };
        var service = CreateService(esi, db, SdeWithRegions((60003466, 10000002)), hub);

        var item = Assert.Single(await service.GetInventoryAsync(CharacterId));

        // Ausführbarer Quote unverändert aus der Asset-Region.
        Assert.Equal(90, item.BestBuyPrice);
        Assert.Equal(110, item.BestSellPrice);

        // Vergleich: keine Order-Seiten, nur Referenzpreis — niemals ausführbar.
        Assert.NotNull(item.ComparisonQuote);
        var cmp = item.ComparisonQuote!;
        Assert.Null(cmp.BestBuyPrice);
        Assert.Null(cmp.BestSellPrice);
        Assert.Null(cmp.SnapshotTimestamp);
        Assert.Equal(95, cmp.AveragePrice);
        Assert.Contains("Referenz", cmp.Note);
        Assert.Equal("esi-reference", cmp.Source);
    }

    [Fact]
    public async Task GetInventoryAsync_ComparisonMarketWithoutAnyData_SourceIsUnknown()
    {
        // Vergleichsmarkt ohne Snapshot UND ohne Referenzpreis: Quelle „unknown",
        // keine erfundenen Seitenpreise — expliziter Hinweis statt stillem Wert.
        var db = TestDb.Create();
        db.MarketSnapshots.Add(Snapshot(6547, 10000002, DateTime.UtcNow.AddMinutes(-1), buy: 90, sell: 110));
        await db.SaveChangesAsync();

        var hub = new FakeHubSelectionService
        {
            ComparisonMarket = new MarketHubProfile
            {
                Id = 12, Name = "Jita", RegionId = 10000043, SystemId = 30000142, IsComparisonMarket = true
            }
        };
        var esi = new FakeEsiApiService
        {
            Assets = new List<CharacterAsset> { Asset(1, 6547, 10, 60003466, "station") },
            Prices = new List<MarketPrice>()
        };
        var service = CreateService(esi, db, SdeWithRegions((60003466, 10000002)), hub);

        var item = Assert.Single(await service.GetInventoryAsync(CharacterId));
        Assert.NotNull(item.ComparisonQuote);
        var cmp = item.ComparisonQuote!;
        Assert.Null(cmp.BestBuyPrice);
        Assert.Null(cmp.BestSellPrice);
        Assert.Equal("unknown", cmp.Source);
        Assert.Contains("unbekannt", cmp.Note);
    }

    [Fact]
    public async Task GetInventoryAsync_SwitchComparisonMarket_PreservesOriginalProvenanceAndRefreshesComparison()
    {
        // AC3: Der Wechsel des Vergleichsmarkts erhält die Provenienz des
        // automatischen Markt-Quotes vollständig; nur der Vergleich wechselt.
        var db = TestDb.Create();
        db.MarketSnapshots.Add(Snapshot(6545, 10000002, DateTime.UtcNow.AddMinutes(-1), buy: 90, sell: 110));
        var jitaSnap = Snapshot(6545, 10000043, DateTime.UtcNow.AddMinutes(-2), buy: 80, sell: 100);
        jitaSnap.SellVolume = 250_000;
        db.MarketSnapshots.Add(jitaSnap);
        var amarrSnap = Snapshot(6545, 10000054, DateTime.UtcNow.AddMinutes(-3), buy: 70, sell: 95);
        amarrSnap.SellVolume = 150_000;
        db.MarketSnapshots.Add(amarrSnap);
        await db.SaveChangesAsync();

        var hub = new FakeHubSelectionService
        {
            ComparisonMarket = new MarketHubProfile
            {
                Id = 9, Name = "Jita", RegionId = 10000043, SystemId = 30000142, IsComparisonMarket = true
            }
        };
        var esi = new FakeEsiApiService
        {
            Assets = new List<CharacterAsset> { Asset(1, 6545, 10, 60003466, "station") },
            Prices = new List<MarketPrice> { new() { TypeId = 6545, AdjustedPrice = 95, AveragePrice = 95 } }
        };
        var service = CreateService(esi, db, SdeWithRegions((60003466, 10000002)), hub);

        var first = Assert.Single(await service.GetInventoryAsync(CharacterId));
        Assert.Equal(90, first.BestBuyPrice);
        Assert.Equal(110, first.BestSellPrice);
        Assert.NotNull(first.ComparisonQuote);
        Assert.Equal(80, first.ComparisonQuote!.BestBuyPrice);
        Assert.Equal(100, first.ComparisonQuote!.BestSellPrice);

        // Vergleichsmarkt wechseln (neues Profil) — der Inventar-Cache ist
        // profilsensitiv (Cache-Key enthält das Vergleichsprofil), der
        // ausführbare Quote und seine Provenienz bleiben identisch.
        hub.ComparisonMarket = new MarketHubProfile
        {
            Id = 10, Name = "Amarr", RegionId = 10000054, SystemId = 30002187, IsComparisonMarket = true
        };

        var second = Assert.Single(await service.GetInventoryAsync(CharacterId));
        Assert.Equal(90, second.BestBuyPrice);
        Assert.Equal(110, second.BestSellPrice);
        Assert.Equal("snapshot", second.BuyPriceSource);
        Assert.NotNull(second.ComparisonQuote);
        Assert.Equal("Amarr", second.ComparisonQuote!.MarketName);
        Assert.Equal(70, second.ComparisonQuote!.BestBuyPrice);
        Assert.Equal(95, second.ComparisonQuote!.BestSellPrice);
    }

    [Fact]
    public async Task GetInventoryAsync_ComparisonSnapshotStale_ShownAsReferenceOnly()
    {
        // Veralteter Vergleichs-Snapshot (>6h) ist nur noch Referenz, nie ein
        // ausführbarer Kurs; der Item-Quote der Asset-Region bleibt unberührt.
        var db = TestDb.Create();
        db.MarketSnapshots.Add(Snapshot(6546, 10000002, DateTime.UtcNow.AddMinutes(-1), buy: 90, sell: 110));
        db.MarketSnapshots.Add(Snapshot(6546, 10000043, DateTime.UtcNow.AddHours(-8), buy: 80, sell: 100));
        await db.SaveChangesAsync();

        var hub = new FakeHubSelectionService
        {
            ComparisonMarket = new MarketHubProfile
            {
                Id = 11, Name = "Jita", RegionId = 10000043, SystemId = 30000142, IsComparisonMarket = true
            }
        };
        var esi = new FakeEsiApiService
        {
            Assets = new List<CharacterAsset> { Asset(1, 6546, 10, 60003466, "station") },
            Prices = new List<MarketPrice> { new() { TypeId = 6546, AdjustedPrice = 95, AveragePrice = 95 } }
        };
        var service = CreateService(esi, db, SdeWithRegions((60003466, 10000002)), hub);

        var item = Assert.Single(await service.GetInventoryAsync(CharacterId));

        Assert.Equal(90, item.BestBuyPrice);
        Assert.Equal(110, item.BestSellPrice);

        Assert.NotNull(item.ComparisonQuote);
        var cmp = item.ComparisonQuote!;
        Assert.True(cmp.IsStale);
        Assert.Contains("veraltet", cmp.Note);
        Assert.Equal("market-snapshot", cmp.Source);
    }
}