using WALLEve.Models.Map;
using WALLEve.Services.Map;
using WALLEve.Services.Map.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;

namespace WALLEve.Tests;

/// <summary>
/// Tests für den gemeinsamen Routenvertrag (#72): lokaler Dijkstra über den
/// SDE-System-Graphen, kürzeste Route, Security-Ausschluss (Safer), unerreichbares
/// Ziel sowie Konsistenz von Systemfolge, Sprungzählung und Security-Zusammenfassung.
///
/// Fixture-Graph (symmetrische Verbindungen):
///   highsec-Korridor: A(1, 0.9) – B(2, 0.6) – C(3, 0.5) – D(4, 0.7)
///   lowsec-Abkürzung:  A – X(5, 0.3) – D
///   nullsec-Route:     A – E(7, 0.0) – F(8, 0.0) – D
///   unerreichbar:      G(9, 0.9) – H(10, 0.8)
/// </summary>
public class RouteCalculationServiceTests
{
    // ------------------------------------------------------------------
    // Fixture
    // ------------------------------------------------------------------

    private sealed class FakeMapData : IMapDataService
    {
        /// <summary>System-ID → Security.</summary>
        private readonly Dictionary<int, float> _security = new()
        {
            [1] = 0.9f,  // A
            [2] = 0.6f,  // B
            [3] = 0.5f,  // C
            [4] = 0.7f,  // D
            [5] = 0.3f,  // X
            [6] = 0.2f,  // Y (nicht in diesem Fixture verbunden)
            [7] = 0.0f,  // E
            [8] = 0.0f,  // F
            [9] = 0.9f,  // G
            [10] = 0.8f  // H
        };

        public Task<Dictionary<int, List<int>>> BuildSystemGraphAsync()
        {
            var graph = new Dictionary<int, List<int>>
            {
                [1] = new() { 2, 5, 7 },          // A – B, A – X, A – E
                [2] = new() { 1, 3 },             // B – A, B – C
                [3] = new() { 2, 4 },             // C – B, C – D
                [4] = new() { 3, 5, 8 },          // D – C, D – X, D – F
                [5] = new() { 1, 4 },             // X – A, X – D
                [7] = new() { 1, 8 },             // E – A, E – F
                [8] = new() { 4, 7 },             // F – D, F – E
                [9] = new() { 10 },               // G – H
                [10] = new() { 9 }                // H – G
            };

            return Task.FromResult(graph);
        }

        public Task<List<MapSolarSystemNode>> GetSystemsByIdsAsync(List<int> systemIds)
        {
            var result = systemIds.Select(id => new MapSolarSystemNode
            {
                SolarSystemId = id,
                Name = id switch
                {
                    1 => "A", 2 => "B", 3 => "C", 4 => "D",
                    5 => "X", 6 => "Y", 7 => "E", 8 => "F",
                    9 => "G", _ => "H"
                },
                Security = _security.TryGetValue(id, out var sec) ? sec : -1.0f,
                RegionId = 1,
                RegionName = "Fixture",
                ConstellationId = 1,
                ConstellationName = "FixtureCon"
            }).ToList();

            return Task.FromResult(result);
        }

        public Task<MapSolarSystemNode?> GetSolarSystemAsync(int solarSystemId)
        {
            var systems = GetSystemsByIdsAsync(new List<int> { solarSystemId }).Result;
            return Task.FromResult(systems.FirstOrDefault());
        }

        public Task<List<MapRegionNode>> GetAllRegionsAsync() => throw new NotSupportedException();
        public Task<MapRegionNode?> GetRegionAsync(int regionId) => throw new NotSupportedException();
        public Task<List<MapSolarSystemNode>> GetSystemsInRegionAsync(int regionId) => throw new NotSupportedException();
        public Task<List<MapSolarSystemNode>> GetSystemsWithinJumpsAsync(int originSystemId, int maxJumps) => throw new NotSupportedException();
        public Task<Dictionary<int, int>> GetJumpDistancesAsync(int originSystemId, int maxJumps) => throw new NotSupportedException();
        public Task<List<MapConnection>> GetRegionConnectionsAsync() => throw new NotSupportedException();
        public Task<List<MapConnection>> GetSystemConnectionsInRegionAsync(int regionId) => throw new NotSupportedException();
        public Task<List<MapConnection>> GetCrossRegionConnectionsForSystemAsync(int regionId) => throw new NotSupportedException();
        public Task<List<MapConnection>> GetConnectionsForSystemsAsync(List<int> systemIds) => throw new NotSupportedException();
        public Task<Dictionary<int, SystemActivity>> GetSystemActivitiesAsync(List<int> systemIds) => throw new NotSupportedException();
    }

    private static RouteCalculationService CreateService() => new(
        new FakeMapData(),
        NullLogger<RouteCalculationService>.Instance);

    private const int A = 1;
    private const int B = 2;
    private const int C = 3;
    private const int D = 4;
    private const int X = 5;
    private const int E = 7;
    private const int F = 8;
    private const int G = 9;
    private const int H = 10;

    // ------------------------------------------------------------------
    // Kürzeste Route
    // ------------------------------------------------------------------

    [Fact]
    public async Task CalculateRouteLocal_Shorter_FindsFewestJumps()
    {
        var service = CreateService();

        var route = await service.CalculateRouteLocalAsync(A, D, RoutingPreference.Shorter);

        Assert.True(route.Success, route.Error);
        // A – X – D ist mit 2 Sprüngen kürzer als der 3-Sprung-Korridor.
        Assert.Equal(new List<int> { A, X, D }, route.Path);
        Assert.Equal(2, route.TotalJumps);
    }

    [Fact]
    public async Task CalculateRouteLocal_Shorter_OriginEqualsDestination()
    {
        var service = CreateService();

        var route = await service.CalculateRouteLocalAsync(A, A, RoutingPreference.Shorter);

        Assert.True(route.Success, route.Error);
        Assert.Equal(new List<int> { A }, route.Path);
        Assert.Equal(0, route.TotalJumps);
        Assert.Single(route.Systems ?? new());
    }

    // ------------------------------------------------------------------
    // Security-Ausschluss (Safer)
    // ------------------------------------------------------------------

    [Fact]
    public async Task CalculateRouteLocal_Safer_AvoidsLowSecShortcut()
    {
        var service = CreateService();

        var route = await service.CalculateRouteLocalAsync(A, D, RoutingPreference.Safer);

        Assert.True(route.Success, route.Error);
        // Safer wählt den längeren High-Sec-Korridor statt der Low-Sec-Abkürzung.
        Assert.Equal(new List<int> { A, B, C, D }, route.Path);
        Assert.Equal(3, route.TotalJumps);
        Assert.Equal(3, route.HighSecJumps);
        Assert.Equal(0, route.LowSecJumps);
        Assert.Equal(0, route.NullSecJumps);
    }

    [Fact]
    public async Task CalculateRouteLocal_Safer_StillPrefersShorterHighSecPath()
    {
        var service = CreateService();

        // B – C – D (2 Sprünge, High-Sec) schlägt B – A – X – D (3 Sprünge, Low-Sec).
        var route = await service.CalculateRouteLocalAsync(B, D, RoutingPreference.Safer);

        Assert.True(route.Success, route.Error);
        Assert.Equal(new List<int> { B, C, D }, route.Path);
        Assert.Equal(2, route.TotalJumps);
    }

    // ------------------------------------------------------------------
    // Unerreichbares Ziel
    // ------------------------------------------------------------------

    [Fact]
    public async Task CalculateRouteLocal_UnreachableTarget_Fails()
    {
        var service = CreateService();

        var route = await service.CalculateRouteLocalAsync(A, G, RoutingPreference.Shorter);

        Assert.False(route.Success);
        Assert.NotNull(route.Error);
        Assert.Null(route.Path);
        Assert.Equal(0, route.TotalJumps);
    }

    [Fact]
    public async Task CalculateRouteLocal_UnknownOriginOrDestination_Fails()
    {
        var service = CreateService();

        var unknownOrigin = await service.CalculateRouteLocalAsync(9999, D, RoutingPreference.Shorter);
        Assert.False(unknownOrigin.Success);
        Assert.NotNull(unknownOrigin.Error);

        var unknownDestination = await service.CalculateRouteLocalAsync(A, 9999, RoutingPreference.Shorter);
        Assert.False(unknownDestination.Success);
        Assert.NotNull(unknownDestination.Error);
    }

    // ------------------------------------------------------------------
    // Konsistenz von Systemfolge, Sprungzählung und Security-Zusammenfassung
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(RoutingPreference.Shorter)]
    [InlineData(RoutingPreference.Safer)]
    [InlineData(RoutingPreference.LessSecure)]
    public async Task CalculateRouteLocal_SummaryConsistentWithPath(RoutingPreference preference)
    {
        var service = CreateService();

        var route = await service.CalculateRouteLocalAsync(A, D, preference);

        Assert.True(route.Success, route.Error);
        Assert.NotNull(route.Path);
        Assert.NotNull(route.Systems);

        // Angezeigte und gezählte Systeme/Sprünge stimmen überein.
        Assert.Equal(route.Path.Count, route.Systems.Count);
        Assert.Equal(route.Path, route.Systems.Select(s => s.SolarSystemId).ToList());
        Assert.Equal(route.Path.Count - 1, route.TotalJumps);
        Assert.Equal(route.TotalJumps, route.HighSecJumps + route.LowSecJumps + route.NullSecJumps);

        // Pfad beginnt mit dem angefragten Ursprung.
        Assert.Equal(A, route.Path[0]);
        Assert.Equal(D, route.Path[^1]);

        // AverageSecurity ist der Mittelwert über die Pfad-Systeme.
        var expectedAverage = route.Systems.Average(s => s.Security);
        Assert.Equal(expectedAverage, route.AverageSecurity, 4);
    }

    [Fact]
    public async Task CalculateRouteLocal_Shorter_SecurityClassesCountedByArrivalSystem()
    {
        var service = CreateService();

        // A – X – D: Sprung nach X (Low-Sec) + Sprung nach D (High-Sec).
        var route = await service.CalculateRouteLocalAsync(A, D, RoutingPreference.Shorter);

        Assert.True(route.Success, route.Error);
        Assert.Equal(1, route.LowSecJumps);
        Assert.Equal(1, route.HighSecJumps);
        Assert.Equal(0, route.NullSecJumps);
        Assert.Equal((0.9f + 0.3f + 0.7f) / 3f, route.AverageSecurity, 4);
    }

    // ------------------------------------------------------------------
    // Charakterwechsel invalidiert Ursprung
    // ------------------------------------------------------------------

    [Fact]
    public async Task CalculateRouteLocal_SecondCallWithNewOrigin_DoesNotReusePreviousOrigin()
    {
        var service = CreateService();

        var first = await service.CalculateRouteLocalAsync(A, D, RoutingPreference.Shorter);
        Assert.True(first.Success, first.Error);

        // Charakterwechsel → neuer Ursprung: Die Route muss am neuen Ursprung beginnen.
        var second = await service.CalculateRouteLocalAsync(B, D, RoutingPreference.Shorter);
        Assert.True(second.Success, second.Error);

        Assert.Equal(B, second.Path![0]);
        Assert.Equal(A, first.Path![0]);
        Assert.NotEqual(first.Path, second.Path);
    }

    // ------------------------------------------------------------------
    // Vergleichsvertrag (lokal vs. ESI)
    // ------------------------------------------------------------------

    [Fact]
    public async Task CalculateRouteComparison_EsiStub_ReportsMismatchWithoutThrowing()
    {
        var service = CreateService();

        var comparison = await service.CalculateRouteComparisonAsync(A, D, RoutingPreference.Shorter);

        Assert.True(comparison.LocalRoute.Success);
        Assert.False(comparison.EsiRoute.Success);
        Assert.False(comparison.RoutesMatch);
        Assert.Empty(comparison.LocalOnlySystemIds);
        Assert.Empty(comparison.EsiOnlySystemIds);
    }
}