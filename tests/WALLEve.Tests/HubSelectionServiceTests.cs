using WALLEve.Data;
using WALLEve.Models.Database;
using WALLEve.Models.Map;
using WALLEve.Services.Map.Interfaces;
using WALLEve.Services.Market;

namespace WALLEve.Tests;

/// <summary>
/// Regression-Tests für Issue #58 (persistente Hub- und Vergleichsmarktprofile):
/// exakte ungewichtete Sprungdistanz im SDE-Graph, deterministischer
/// Gleichstand, unerreichbar/unknown NICHT als Nullsprünge, Vergleichsmarkt
/// ohne Einfluss auf die automatische Hub-Wahl. Ausschließlich Fakes — keine
/// Live-SDE-/ESI-Abhängigkeit.
/// </summary>
public class HubSelectionServiceTests
{
    // Fixture-Graph: 1-2-3-Kette plus 1-4-Sprung; 5 isoliert.
    private static readonly Dictionary<int, List<int>> FixtureGraph = new()
    {
        [1001] = new List<int> { 1002, 1004 },
        [1002] = new List<int> { 1001, 1003 },
        [1003] = new List<int> { 1002 },
        [1004] = new List<int> { 1001 },
        [1005] = new List<int>() // isoliert: von 1001 aus unerreichbar
    };

    private sealed class FakeMapData : IMapDataService
    {
        private readonly Dictionary<int, List<int>>? _graph;

        public FakeMapData(Dictionary<int, List<int>>? graph = null) => _graph = graph;

        public Task<Dictionary<int, List<int>>> BuildSystemGraphAsync()
            => Task.FromResult(_graph ?? FixtureGraph);

        public Task<List<MapRegionNode>> GetAllRegionsAsync() => Task.FromResult(new List<MapRegionNode>());
        public Task<MapRegionNode?> GetRegionAsync(int regionId) => Task.FromResult<MapRegionNode?>(null);
        public Task<List<MapSolarSystemNode>> GetSystemsInRegionAsync(int regionId) => Task.FromResult(new List<MapSolarSystemNode>());
        public Task<MapSolarSystemNode?> GetSolarSystemAsync(int solarSystemId) => Task.FromResult<MapSolarSystemNode?>(null);
        public Task<List<MapSolarSystemNode>> GetSystemsByIdsAsync(List<int> systemIds) => Task.FromResult(new List<MapSolarSystemNode>());
        public Task<List<MapSolarSystemNode>> GetSystemsWithinJumpsAsync(int originSystemId, int maxJumps) => Task.FromResult(new List<MapSolarSystemNode>());
        public Task<Dictionary<int, int>> GetJumpDistancesAsync(int originSystemId, int maxJumps) => Task.FromResult(new Dictionary<int, int>());
        public Task<List<MapConnection>> GetRegionConnectionsAsync() => Task.FromResult(new List<MapConnection>());
        public Task<List<MapConnection>> GetSystemConnectionsInRegionAsync(int regionId) => Task.FromResult(new List<MapConnection>());
        public Task<List<MapConnection>> GetCrossRegionConnectionsForSystemAsync(int regionId) => Task.FromResult(new List<MapConnection>());
        public Task<List<MapConnection>> GetConnectionsForSystemsAsync(List<int> systemIds) => Task.FromResult(new List<MapConnection>());
        public Task<Dictionary<int, SystemActivity>> GetSystemActivitiesAsync(List<int> systemIds) => Task.FromResult(new Dictionary<int, SystemActivity>());
    }

    private static MarketHubProfile Hub(string name, int regionId, int systemId,
        bool active = true, bool comparison = false) => new()
    {
        Name = name,
        RegionId = regionId,
        SystemId = systemId,
        IsActiveHub = active,
        IsComparisonMarket = comparison,
        UpdatedAt = DateTime.UtcNow
    };

    private static async Task<HubSelectionService> CreateServiceAsync(
        WalletDbContext db, Dictionary<int, List<int>>? graph = null)
    {
        // Graph-Basis direkt über den Service testen (symmetrische Normalisierung).
        var service = new HubSelectionService(db, new FakeMapData(graph));
        return service;
    }

    [Fact]
    public async Task SelectNearest_ReturnsExactShortestJumpDistance()
    {
        var db = TestDb.Create();
        db.MarketHubProfiles.AddRange(
            Hub("Mittel", 20000001, 1002),   // 1 Sprung von 1001
            Hub("Weit", 20000002, 1003));    // 2 Sprünge von 1001
        await db.SaveChangesAsync();

        var service = await CreateServiceAsync(db);
        var result = await service.SelectNearestActiveHubAsync(1001);

        Assert.True(result.GraphAvailable);
        Assert.NotNull(result.Selected);
        Assert.Equal("Mittel", result.Selected!.Name);
        Assert.Equal(1, result.Selected.JumpDistance);
        Assert.Equal(2, result.Candidates.Count(c => c.Reachable));
    }

    [Fact]
    public async Task SelectNearest_SymmetricGraph_ReachesHubFromReverseEdge()
    {
        // Nur eine gerichtete Kante 1002 → 1001: trotzdem muss 1001 den Hub
        // in 1002 über die symmetrische Normalisierung erreichen.
        var oneWay = new Dictionary<int, List<int>> { [1002] = new List<int> { 1001 } };
        var db = TestDb.Create();
        db.MarketHubProfiles.Add(Hub("Rücklauf", 20000001, 1002));
        await db.SaveChangesAsync();

        var service = await CreateServiceAsync(db, oneWay);
        var result = await service.SelectNearestActiveHubAsync(1001);

        Assert.NotNull(result.Selected);
        Assert.Equal(1, result.Selected!.JumpDistance);
    }

    [Fact]
    public async Task SelectNearest_Tie_IsDeterministicByRegionThenSystem()
    {
        var db = TestDb.Create();
        db.MarketHubProfiles.AddRange(
            Hub("Alpha", 20000002, 1004),    // 1 Sprung — höhere Region
            Hub("Beta", 20000001, 1002));    // 1 Sprung — niedrigere Region & System
        await db.SaveChangesAsync();

        var service = await CreateServiceAsync(db);
        var result = await service.SelectNearestActiveHubAsync(1001);

        Assert.NotNull(result.Selected);
        Assert.Equal("Beta", result.Selected!.Name);
        Assert.Equal(1, result.Selected.JumpDistance);
    }

    [Fact]
    public async Task SelectNearest_UnreachableHub_IsNotZeroJumps()
    {
        var db = TestDb.Create();
        db.MarketHubProfiles.Add(Hub("Isoliert", 20000003, 1005)); // von 1001 unerreichbar
        await db.SaveChangesAsync();

        var service = await CreateServiceAsync(db);
        var result = await service.SelectNearestActiveHubAsync(1001);

        // Kandidat vorhanden, aber NICHT erreichbar — und niemals 0 Sprünge.
        Assert.True(result.GraphAvailable);
        Assert.Null(result.Selected);
        var candidate = Assert.Single(result.Candidates);
        Assert.False(candidate.Reachable);
        Assert.Null(candidate.JumpDistance);
    }

    [Fact]
    public async Task SelectNearest_MixedReachableAndUnreachable_SelectsReachable()
    {
        var db = TestDb.Create();
        db.MarketHubProfiles.AddRange(
            Hub("Isoliert", 20000003, 1005),
            Hub("Erreichbar", 20000001, 1004));
        await db.SaveChangesAsync();

        var service = await CreateServiceAsync(db);
        var result = await service.SelectNearestActiveHubAsync(1001);

        Assert.NotNull(result.Selected);
        Assert.Equal("Erreichbar", result.Selected!.Name);
        Assert.Equal(1, result.Selected.JumpDistance);
    }

    [Fact]
    public async Task SelectNearest_GraphUnavailable_SelectsNothing()
    {
        var db = TestDb.Create();
        db.MarketHubProfiles.Add(Hub("Jita", 10000002, 30000142));
        await db.SaveChangesAsync();

        var service = await CreateServiceAsync(db, new Dictionary<int, List<int>>());
        var result = await service.SelectNearestActiveHubAsync(1001);

        Assert.False(result.GraphAvailable);
        Assert.Null(result.Selected);
    }

    [Fact]
    public async Task SelectNearest_ComparisonMarket_DoesNotInfluenceSelection()
    {
        var db = TestDb.Create();
        // Vergleichsmarkt NEBEN dem aktivierten Hub, näher dran — darf nicht gewählt werden.
        db.MarketHubProfiles.AddRange(
            Hub("AktiverHub", 20000001, 1004, active: true),
            Hub("Vergleich", 20000002, 1002, active: false, comparison: true));
        await db.SaveChangesAsync();

        var service = await CreateServiceAsync(db);
        var result = await service.SelectNearestActiveHubAsync(1001);

        Assert.NotNull(result.Selected);
        Assert.Equal("AktiverHub", result.Selected!.Name);

        // Vergleichsmarkt separat abrufbar, ohne die Wahl verändert zu haben.
        var comparison = await service.GetComparisonMarketAsync();
        Assert.NotNull(comparison);
        Assert.Equal("Vergleich", comparison!.Name);
    }

    [Fact]
    public async Task SelectNearest_SameSystem_IsZeroJumps()
    {
        // Hub im Ausgangssystem: echte 0 Sprünge (kein erfundener Nullsprung).
        var db = TestDb.Create();
        db.MarketHubProfiles.Add(Hub("VorOrt", 20000001, 1002));
        await db.SaveChangesAsync();

        var service = await CreateServiceAsync(db);
        var result = await service.SelectNearestActiveHubAsync(1002);

        Assert.NotNull(result.Selected);
        Assert.Equal(0, result.Selected!.JumpDistance);
    }

    [Fact]
    public async Task SelectNearest_UnknownOriginSystem_SelectsNothing()
    {
        // Ausgangssystem nicht im Graph (unknown): kein Hub, nie 0 Sprünge.
        var db = TestDb.Create();
        db.MarketHubProfiles.Add(Hub("Jita", 10000002, 30000142));
        await db.SaveChangesAsync();

        var service = await CreateServiceAsync(db);
        var result = await service.SelectNearestActiveHubAsync(999999);

        Assert.True(result.GraphAvailable);
        Assert.Null(result.Selected);
    }

    [Fact]
    public async Task SelectNearest_NoActiveHubs_SelectsNothing()
    {
        var db = TestDb.Create();
        db.MarketHubProfiles.Add(Hub("NurVergleich", 20000002, 1002, active: false, comparison: true));
        await db.SaveChangesAsync();

        var service = await CreateServiceAsync(db);
        var result = await service.SelectNearestActiveHubAsync(1001);

        Assert.True(result.GraphAvailable);
        Assert.Null(result.Selected);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public async Task SaveProfile_UpdatesExistingRow_AndKeepsSingleSystemUnique()
    {
        var db = TestDb.Create();
        var service = await CreateServiceAsync(db);

        var profile = Hub("Jita", 10000002, 30000142);
        await service.SaveProfileAsync(profile);

        profile.Name = "Jita 2.0";
        await service.SaveProfileAsync(profile);

        var profiles = await service.GetProfilesAsync();
        var single = Assert.Single(profiles);
        Assert.Equal("Jita 2.0", single.Name);
        Assert.Equal(30000142, single.SystemId);
    }
}