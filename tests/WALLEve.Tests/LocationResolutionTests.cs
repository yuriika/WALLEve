using WALLEve.Models.Esi.Alliance;
using WALLEve.Models.Esi.Character;
using WALLEve.Models.Esi.Corporation;
using WALLEve.Models.Esi.Markets;
using WALLEve.Models.Measurement;
using WALLEve.Models.Esi.Universe;
using WALLEve.Models.Esi.Wallet;
using WALLEve.Models.Holdings;
using WALLEve.Models.Sde;
using WALLEve.Services.Esi.Interfaces;
using WALLEve.Services.Holdings;
using WALLEve.Services.Sde.Interfaces;

namespace WALLEve.Tests;

/// <summary>
/// Regression-Tests für die sichere Holdings-Location-Auflösung (#50):
/// NPC-Stationen per SDE, zugängliche Strukturen per ESI, reine Parent-Ketten-
/// Auflösung mit Zyklus-/Missing-Parent-Erkennung. Akzeptanzkriterien:
/// Zyklen/fehlende Parents/403-Structures erzeugen Unresolved statt Absturz
/// oder falschem System; bekannte Station und mehrstufiger Container lösen
/// sich deterministisch auf. Determinismus: ausschließlich Fakes, keine
/// Live-ESI-/SDE-Abhängigkeit.
/// </summary>
public class LocationResolutionTests
{
    private const long JitaStation = 60003760;
    private const int JitaSystem = 30000142;
    private const int TheForge = 10000002;
    private const long StructureId = 1_000_000_000_000;

    // ---------------------------------------------------------------- Fakes

    private sealed class FakeSde : ISdeUniverseService
    {
        public Dictionary<long, StationInfo> Stations { get; } = new();
        public Dictionary<int, SolarSystemInfo> Systems { get; } = new();

        public Task<bool> IsDatabaseAvailableAsync() => Task.FromResult(true);
        public Task<string?> GetTypeNameAsync(int typeId) => Task.FromResult<string?>(null);
        public Task<string?> GetTypeGroupAsync(int typeId) => Task.FromResult<string?>(null);
        public Task<Dictionary<int, string?>> GetTypeGroupsAsync(IReadOnlyCollection<int> typeIds) => Task.FromResult(new Dictionary<int, string?>());

        public Task<SolarSystemInfo?> GetSolarSystemAsync(int solarSystemId)
            => Task.FromResult(Systems.TryGetValue(solarSystemId, out var s) ? s : null);

        public Task<StationInfo?> GetStationAsync(long stationId)
            => Task.FromResult(Stations.TryGetValue(stationId, out var s) ? s : null);

        public Task<string?> GetRegionNameAsync(int regionId) => Task.FromResult<string?>(null);
        public Task<string?> GetLocationNameAsync(long locationId) => Task.FromResult<string?>(null);
        public Task<int?> GetRegionIdForLocationAsync(long locationId) => Task.FromResult<int?>(null);
        public Task<int?> GetSolarSystemIdForLocationAsync(long locationId) => Task.FromResult<int?>(null);
        public Task<Dictionary<int, string>> GetAllMarketItemsAsync() => Task.FromResult(new Dictionary<int, string>());
        public Task<Dictionary<int, string>> GetAllRegionsAsync() => Task.FromResult(new Dictionary<int, string>());
        public Task<Dictionary<int, string>> SearchSolarSystemsAsync(string searchQuery, int maxResults = 10)
            => Task.FromResult(new Dictionary<int, string>());
    }

    private sealed class FakeEsi : IEsiApiService
    {
        public Dictionary<long, StructureLookupResult> Structures { get; } = new();

        /// <summary>Protokolliert alle Struktur-IDs, die der Resolver über den ESI-Dienst abfragt (Kontext-Regressionstest).</summary>
        public List<long> StructureLookupIds { get; } = new();

        public Task<StructureLookupResult> GetStructureAsync(long structureId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            StructureLookupIds.Add(structureId);
            return Task.FromResult(Structures.TryGetValue(structureId, out var r) ? r : new StructureLookupResult { Error = "unavailable" });
        }

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
        public Task<List<CharacterAsset>?> GetCharacterAssetsAsync(int characterId, CancellationToken ct = default) => throw new NotImplementedException();

        public Task<List<CharacterMiningEntry>?> GetCharacterMiningLedgerAsync(int characterId, CancellationToken ct = default) => throw new NotImplementedException();

        public Task<List<CharacterBlueprint>?> GetCharacterBlueprintsAsync(int characterId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<CharacterIndustryJob>?> GetCharacterIndustryJobsAsync(int characterId, CancellationToken ct = default) => throw new NotImplementedException();
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
        public Task<List<MarketPrice>?> GetMarketPricesAsync() => throw new NotImplementedException();
    }

    private static HoldingsLocationResolver CreateResolver(FakeSde sde, FakeEsi esi)
        => new(sde, esi, Microsoft.Extensions.Logging.Abstractions.NullLogger<HoldingsLocationResolver>.Instance);

    private static HoldingItem Item(long itemId, long locationId, long? parentItemId = null)
        => new()
        {
            ItemId = itemId,
            TypeId = 34,
            Quantity = 1,
            IsSingleton = false,
            LocationId = locationId,
            LocationFlag = "Hangar",
            ParentItemId = parentItemId
        };

    private static LocationResolution StationNode(long stationId)
        => new() { LocationId = stationId, Kind = LocationKind.Station, Name = "Jita IV - Moon 4 - Caldari Navy Assembly Plant" };

    private void SeedJita(FakeSde sde)
    {
        sde.Stations[JitaStation] = new StationInfo
        {
            StationId = JitaStation,
            Name = "Jita IV - Moon 4 - Caldari Navy Assembly Plant",
            SolarSystemId = JitaSystem,
            RegionId = TheForge
        };
        sde.Systems[JitaSystem] = new SolarSystemInfo
        {
            SolarSystemId = JitaSystem,
            Name = "Jita",
            Security = 0.9f,
            RegionId = TheForge,
            RegionName = "The Forge"
        };
    }

    // ------------------------------------------------ Reine Kettenauflösung

    [Theory]
    [InlineData(30_000_001, LocationKind.SolarSystem)]
    [InlineData(60_003_760, LocationKind.Station)]
    [InlineData(1_000_000_000_000, LocationKind.Structure)]
    [InlineData(42, LocationKind.Unresolved)]
    [InlineData(1_023, LocationKind.Unresolved)]
    public void Classify_MapsKnownEveIdRanges(long locationId, LocationKind expected)
    {
        Assert.Equal(expected, LocationChainResolver.Classify(locationId));
    }

    [Fact]
    public void ResolveChain_MultiLevelContainerToStation_ResolvesDeterministically()
    {
        // Item in Container 1002 in Container 1003 in NPC-Station (Jita).
        var items = new List<HoldingItem>
        {
            Item(1, 1002, 1002),
            Item(1002, 1003, 1003),
            Item(1003, JitaStation)
        };
        var itemsById = items.ToDictionary(i => i.ItemId);
        var external = new Dictionary<long, LocationResolution> { [JitaStation] = StationNode(JitaStation) };

        var result = LocationChainResolver.ResolveChain(items[0], itemsById, external);

        Assert.True(result.IsResolved);
        Assert.Equal(JitaStation, result.Anchor!.LocationId);
        Assert.Equal(3, result.Chain.Count);
        Assert.Equal(LocationKind.Container, result.Chain[0].Kind);
        Assert.Equal(1002, result.Chain[0].LocationId);
        Assert.Equal(LocationKind.Container, result.Chain[1].Kind);
        Assert.Equal(1003, result.Chain[1].LocationId);
        Assert.Equal(LocationKind.Station, result.Chain[2].Kind);
        Assert.Equal(JitaStation, result.Chain[2].LocationId);
    }

    [Fact]
    public void ResolveChain_Cycle_UnresolvedInsteadOfCrashOrLoop()
    {
        // A in B, B in A → Zyklus.
        var items = new List<HoldingItem>
        {
            Item(1, 2, 2),
            Item(2, 1, 1)
        };
        var itemsById = items.ToDictionary(i => i.ItemId);

        var result = LocationChainResolver.ResolveChain(items[0], itemsById, new Dictionary<long, LocationResolution>());

        Assert.False(result.IsResolved);
        Assert.Equal("cycle", result.UnresolvedReason);
        Assert.Null(result.Anchor);
    }

    [Fact]
    public void ResolveChain_MissingParent_UnresolvedWithReason()
    {
        // Item verweist per ParentItemId auf Container 9999, der nicht im Bestand liegt.
        var itemsById = new Dictionary<long, HoldingItem> { [1] = Item(1, 9999, 9999) };

        var result = LocationChainResolver.ResolveChain(itemsById[1], itemsById, new Dictionary<long, LocationResolution>());

        Assert.False(result.IsResolved);
        Assert.Equal("missing-parent", result.UnresolvedReason);
    }

    [Fact]
    public void ResolveChain_UnknownNonItemLocation_UnresolvedWithoutSystemGuess()
    {
        // Location 42 ist weder Item noch bekannter EVE-ID-Bereich — kein System-Raten.
        var itemsById = new Dictionary<long, HoldingItem> { [1] = Item(1, 42) };

        var result = LocationChainResolver.ResolveChain(itemsById[1], itemsById, new Dictionary<long, LocationResolution>());

        Assert.False(result.IsResolved);
        Assert.Equal("unknown-location", result.UnresolvedReason);
        Assert.Null(result.Anchor);
    }

    // -------------------------------------------- Service (SDE/ESI orchestriert)

    [Fact]
    public async Task ResolveSnapshot_KnownStation_ResolvesWithNamesDeterministically()
    {
        var sde = new FakeSde();
        SeedJita(sde);
        var resolver = CreateResolver(sde, new FakeEsi());
        var items = new List<HoldingItem> { Item(1, JitaStation) };

        var resolved = await resolver.ResolveSnapshotAsync(items);

        var r = Assert.Single(resolved);
        Assert.True(r.IsResolved);
        Assert.Equal(LocationKind.Station, r.Location.Kind);
        Assert.Equal("Jita IV - Moon 4 - Caldari Navy Assembly Plant", r.Location.Name);
        Assert.Equal(JitaSystem, r.Location.SolarSystemId);
        Assert.Equal("Jita", r.Location.SolarSystemName);
        Assert.Equal("The Forge", r.Location.RegionName);
        Assert.Equal(LocationKind.Station, r.Anchor!.Kind);
        Assert.Equal(JitaStation, r.Anchor.LocationId);
    }

    [Fact]
    public async Task ResolveSnapshot_MultiLevelContainer_ResolvesWholeChain()
    {
        var sde = new FakeSde();
        SeedJita(sde);
        var resolver = CreateResolver(sde, new FakeEsi());
        var items = new List<HoldingItem>
        {
            Item(1, 1002, 1002),
            Item(1002, 1003, 1003),
            Item(1003, JitaStation)
        };

        var resolved = await resolver.ResolveSnapshotAsync(items);

        Assert.Equal(3, resolved.Count);
        var innermost = resolved.Single(r => r.Item.ItemId == 1);
        Assert.True(innermost.IsResolved);
        Assert.Equal(3, innermost.Chain.Count);
        Assert.Equal(LocationKind.Container, innermost.Chain[0].Kind);
        Assert.Equal(LocationKind.Container, innermost.Chain[1].Kind);
        Assert.Equal(LocationKind.Station, innermost.Chain[2].Kind);
        Assert.Equal(JitaStation, innermost.Anchor!.LocationId);
    }

    [Fact]
    public async Task ResolveSnapshot_Structure403_UnresolvedWithReasonInsteadOfCrash()
    {
        var sde = new FakeSde();
        SeedJita(sde);
        var esi = new FakeEsi();
        esi.Structures[StructureId] = new StructureLookupResult { Error = "403" };
        var resolver = CreateResolver(sde, esi);
        var items = new List<HoldingItem> { Item(1, StructureId) };

        var resolved = await resolver.ResolveSnapshotAsync(items);

        var r = Assert.Single(resolved);
        Assert.False(r.IsResolved);
        Assert.Equal("403", r.UnresolvedReason);
        Assert.Equal(LocationKind.Unresolved, r.Location.Kind);
        Assert.Null(r.Anchor); // kein falsches System erfinden
        Assert.Equal(StructureId, r.Location.LocationId); // Rohwert bleibt erhalten
    }

    [Fact]
    public async Task ResolveSnapshot_AccessibleStructure_ResolvesWithNames()
    {
        var sde = new FakeSde();
        SeedJita(sde);
        var esi = new FakeEsi();
        esi.Structures[StructureId] = new StructureLookupResult
        {
            Structure = new EsiStructure { StructureId = StructureId, Name = "Keepstar", SolarSystemId = JitaSystem, TypeId = 35834 }
        };
        var resolver = CreateResolver(sde, esi);
        var items = new List<HoldingItem> { Item(1, StructureId) };

        var resolved = await resolver.ResolveSnapshotAsync(items);

        var r = Assert.Single(resolved);
        Assert.True(r.IsResolved);
        Assert.Equal(LocationKind.Structure, r.Location.Kind);
        Assert.Equal("Keepstar", r.Location.Name);
        Assert.Equal(JitaSystem, r.Location.SolarSystemId);
        Assert.Equal("Jita", r.Location.SolarSystemName);
        Assert.Equal("The Forge", r.Location.RegionName);
    }

    [Fact]
    public async Task ResolveSnapshot_UnknownStationId_UnresolvedWithoutSystemGuess()
    {
        var sde = new FakeSde(); // keine Station hinterlegt → not-in-sde
        var resolver = CreateResolver(sde, new FakeEsi());
        var items = new List<HoldingItem> { Item(1, JitaStation) };

        var resolved = await resolver.ResolveSnapshotAsync(items);

        var r = Assert.Single(resolved);
        Assert.False(r.IsResolved);
        Assert.Equal("not-in-sde", r.UnresolvedReason);
        Assert.Null(r.Anchor);
    }

    [Fact]
    public async Task ResolveSnapshot_MissingParentViaService_UnresolvedWithReason()
    {
        var resolver = CreateResolver(new FakeSde(), new FakeEsi());
        var items = new List<HoldingItem> { Item(1, 9999, 9999) };

        var resolved = await resolver.ResolveSnapshotAsync(items);

        var r = Assert.Single(resolved);
        Assert.False(r.IsResolved);
        Assert.Equal("missing-parent", r.UnresolvedReason);
        Assert.Equal(9999, r.Location.LocationId);
    }

    [Fact]
    public async Task ResolveSnapshot_SolarSystemLocation_ResolvesSystemNode()
    {
        var sde = new FakeSde();
        SeedJita(sde);
        var resolver = CreateResolver(sde, new FakeEsi());
        var items = new List<HoldingItem> { Item(1, JitaSystem) };

        var resolved = await resolver.ResolveSnapshotAsync(items);

        var r = Assert.Single(resolved);
        Assert.True(r.IsResolved);
        Assert.Equal(LocationKind.SolarSystem, r.Location.Kind);
        Assert.Equal("Jita", r.Location.Name);
        Assert.Equal(JitaSystem, r.Location.SolarSystemId);
        Assert.Equal("The Forge", r.Location.RegionName);
    }

    [Fact]
    public async Task ResolveSnapshot_StructureLookupCarriesNoOwnerContext()
    {
        // Review-Regression: Der Resolver behauptet KEINEN Snapshot-Owner. Die
        // Strukturabfrage geht ausschließlich mit der StructureId an den
        // ESI-Dienst (aktiver Auth-Kontext der Single-Active-Character-App) —
        // es wird kein irreführender Character-/Owner-Parameter transportiert.
        var sde = new FakeSde();
        SeedJita(sde);
        var esi = new FakeEsi();
        esi.Structures[StructureId] = new StructureLookupResult
        {
            Structure = new EsiStructure { StructureId = StructureId, Name = "Keepstar", SolarSystemId = JitaSystem, TypeId = 35834 }
        };
        var resolver = CreateResolver(sde, esi);
        var items = new List<HoldingItem> { Item(1, StructureId) };

        var resolved = await resolver.ResolveSnapshotAsync(items);

        var r = Assert.Single(resolved);
        Assert.True(r.IsResolved);
        Assert.Equal(LocationKind.Structure, r.Location.Kind);
        Assert.Equal("Keepstar", r.Location.Name);
        Assert.Equal(new[] { StructureId }, esi.StructureLookupIds); // genau eine Abfrage, nur die StructureId
    }

    [Fact]
    public async Task ResolveSnapshot_Cancelled_PropagatesOperationCanceledNotUnavailable()
    {
        // Review-Regression: Caller-Cancellation wird durchgereicht und nie als
        // erfolgreich zurückgegebenes Unresolved ("unavailable") verbucht.
        var sde = new FakeSde();
        SeedJita(sde);
        var resolver = CreateResolver(sde, new FakeEsi());
        var items = new List<HoldingItem> { Item(1, StructureId) };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => resolver.ResolveSnapshotAsync(items, cts.Token));
    }
}