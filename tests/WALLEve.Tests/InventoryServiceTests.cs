using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using WALLEve.Models.Esi.Alliance;
using WALLEve.Models.Esi.Character;
using WALLEve.Models.Esi.Corporation;
using WALLEve.Models.Esi.Markets;
using WALLEve.Models.Esi.Universe;
using WALLEve.Models.Esi.Wallet;
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

        public Task<List<CharacterAsset>> GetCharacterAssetsAsync(int characterId)
            => Task.FromResult(Assets);
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

    private static InventoryService CreateService(FakeEsiApiService esi)
        => new(
            esi,
            new FakeSdeUniverseService(),
            new FeeCalculatorService(),
            TestDb.Create(),
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<InventoryService>.Instance);

    private static CharacterAsset Asset(long itemId, int typeId, int quantity, long locationId, string locationType, string locationFlag = "Hangar")
        => new()
        {
            ItemId = itemId, TypeId = typeId, Quantity = quantity,
            LocationId = locationId, LocationType = locationType, LocationFlag = locationFlag
        };

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

        var item = Assert.Single(result);

        // AC1: gleicher Typ an zwei Stationen → getrennter Handlungskontext je Ort
        Assert.Equal(2, item.SellContexts.Count);
        Assert.True(item.HasMultipleLocations);

        var ctxA = item.SellContexts.Single(c => c.LocationId == 60003466);
        var ctxB = item.SellContexts.Single(c => c.LocationId == 60003760);
        Assert.Equal(5, ctxA.Quantity);
        Assert.Equal(3, ctxB.Quantity);
        Assert.Equal(1000, ctxA.SellPrice);
        Assert.False(ctxA.IsBlocked);
        Assert.False(ctxB.IsBlocked);

        // Jeder Kontext trägt seinen eigenen Netto-Erlös (Fehler 3% + Steuer 7,5%,
        // ohne Skills → Nettofaktor 0,895): 5 × 895 = 4475, 3 × 895 = 2685
        Assert.Equal(4475.0, ctxA.EstimatedNetProceeds!.Value, 2);
        Assert.Equal(2685.0, ctxB.EstimatedNetProceeds!.Value, 2);

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
        var esi = new FakeEsiApiService
        {
            Assets = new List<CharacterAsset>
            {
                Asset(11, 2222, 2, containerItemId, "item"),          // Container: blockiert
                Asset(12, 2222, 4, 60003466, "station")               // Station: verkaufbar
            },
            Prices = new List<MarketPrice> { new() { TypeId = 2222, AdjustedPrice = 500, AveragePrice = 500 } }
        };
        var service = CreateService(esi);

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
}