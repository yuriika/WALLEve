using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using WALLEve.Models.Map;
using WALLEve.Services.Map.Interfaces;
using WALLEve.Services.Sde;
using WALLEve.Services.Esi.Interfaces;

namespace WALLEve.Services.Map;

public class MapDataService : IMapDataService
{
    private readonly SdeDbContext _context;
    private readonly ILogger<MapDataService> _logger;
    private readonly IServiceProvider _serviceProvider;

    // Caches (Singleton Service → cache for lifetime)
    private Dictionary<int, List<int>>? _systemGraph;
    private List<MapRegionNode>? _allRegions;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    public MapDataService(
        SdeDbContext context,
        ILogger<MapDataService> logger,
        IServiceProvider serviceProvider)
    {
        _context = context;
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public async Task<List<MapRegionNode>> GetAllRegionsAsync()
    {
        if (_allRegions != null) return _allRegions;

        await _cacheLock.WaitAsync();
        try
        {
            if (_allRegions != null) return _allRegions;

            await _context.EnsureConnectionAsync();
            using var cmd = _context.Connection.CreateCommand();
            cmd.CommandText = @"
                SELECT regionID, regionName, x, y, z
                FROM mapRegions
                WHERE regionID < 11000000
                ORDER BY regionName";

            var regions = new List<MapRegionNode>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                regions.Add(new MapRegionNode
                {
                    RegionId = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    X = reader.IsDBNull(2) ? 0 : reader.GetDouble(2),
                    Y = reader.IsDBNull(3) ? 0 : reader.GetDouble(3),
                    Z = reader.IsDBNull(4) ? 0 : reader.GetDouble(4)
                });
            }

            _logger.LogInformation("Loaded {Count} regions from SDE", regions.Count);
            _allRegions = regions;
            return regions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading all regions");
            return new List<MapRegionNode>();
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    public async Task<MapRegionNode?> GetRegionAsync(int regionId)
    {
        try
        {
            var regions = await GetAllRegionsAsync();
            return regions.FirstOrDefault(r => r.RegionId == regionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting region {RegionId}", regionId);
            return null;
        }
    }

    public async Task<List<MapSolarSystemNode>> GetSystemsInRegionAsync(int regionId)
    {
        try
        {
            await _context.EnsureConnectionAsync();
            using var cmd = _context.Connection.CreateCommand();
            cmd.CommandText = @"
                SELECT s.solarSystemID, s.solarSystemName, s.security,
                       s.x, s.y, s.z, s.constellationID,
                       r.regionID, r.regionName,
                       c.constellationName
                FROM mapSolarSystems s
                JOIN mapRegions r ON s.regionID = r.regionID
                LEFT JOIN mapConstellations c ON s.constellationID = c.constellationID
                WHERE s.regionID = @regionId
                ORDER BY s.solarSystemName";
            cmd.Parameters.AddWithValue("@regionId", regionId);

            var systems = new List<MapSolarSystemNode>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                systems.Add(new MapSolarSystemNode
                {
                    SolarSystemId = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    Security = reader.GetFloat(2),
                    X = reader.IsDBNull(3) ? 0 : reader.GetDouble(3),
                    Y = reader.IsDBNull(4) ? 0 : reader.GetDouble(4),
                    Z = reader.IsDBNull(5) ? 0 : reader.GetDouble(5),
                    ConstellationId = reader.GetInt32(6),
                    RegionId = reader.GetInt32(7),
                    RegionName = reader.GetString(8),
                    ConstellationName = reader.IsDBNull(9) ? "" : reader.GetString(9)
                });
            }

            _logger.LogDebug("Loaded {Count} systems in region {RegionId}", systems.Count, regionId);
            return systems;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting systems in region {RegionId}", regionId);
            return new List<MapSolarSystemNode>();
        }
    }

    public async Task<MapSolarSystemNode?> GetSolarSystemAsync(int solarSystemId)
    {
        try
        {
            await _context.EnsureConnectionAsync();
            using var cmd = _context.Connection.CreateCommand();
            cmd.CommandText = @"
                SELECT s.solarSystemID, s.solarSystemName, s.security,
                       s.x, s.y, s.z, s.constellationID,
                       r.regionID, r.regionName,
                       c.constellationName
                FROM mapSolarSystems s
                JOIN mapRegions r ON s.regionID = r.regionID
                LEFT JOIN mapConstellations c ON s.constellationID = c.constellationID
                WHERE s.solarSystemID = @systemId";
            cmd.Parameters.AddWithValue("@systemId", solarSystemId);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new MapSolarSystemNode
                {
                    SolarSystemId = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    Security = reader.GetFloat(2),
                    X = reader.IsDBNull(3) ? 0 : reader.GetDouble(3),
                    Y = reader.IsDBNull(4) ? 0 : reader.GetDouble(4),
                    Z = reader.IsDBNull(5) ? 0 : reader.GetDouble(5),
                    ConstellationId = reader.GetInt32(6),
                    RegionId = reader.GetInt32(7),
                    RegionName = reader.GetString(8),
                    ConstellationName = reader.IsDBNull(9) ? "" : reader.GetString(9)
                };
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting solar system {SystemId}", solarSystemId);
            return null;
        }
    }

    public async Task<List<MapSolarSystemNode>> GetSystemsByIdsAsync(List<int> systemIds)
    {
        if (!systemIds.Any()) return new List<MapSolarSystemNode>();

        try
        {
            await _context.EnsureConnectionAsync();
            var idsString = string.Join(",", systemIds);
            using var cmd = _context.Connection.CreateCommand();
            cmd.CommandText = $@"
                SELECT s.solarSystemID, s.solarSystemName, s.security,
                       s.x, s.y, s.z, s.constellationID,
                       r.regionID, r.regionName,
                       c.constellationName
                FROM mapSolarSystems s
                JOIN mapRegions r ON s.regionID = r.regionID
                LEFT JOIN mapConstellations c ON s.constellationID = c.constellationID
                WHERE s.solarSystemID IN ({idsString})
                ORDER BY s.solarSystemName";

            var systems = new List<MapSolarSystemNode>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                systems.Add(new MapSolarSystemNode
                {
                    SolarSystemId = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    Security = reader.GetFloat(2),
                    X = reader.IsDBNull(3) ? 0 : reader.GetDouble(3),
                    Y = reader.IsDBNull(4) ? 0 : reader.GetDouble(4),
                    Z = reader.IsDBNull(5) ? 0 : reader.GetDouble(5),
                    ConstellationId = reader.GetInt32(6),
                    RegionId = reader.GetInt32(7),
                    RegionName = reader.GetString(8),
                    ConstellationName = reader.IsDBNull(9) ? "" : reader.GetString(9)
                });
            }

            return systems;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting systems by IDs");
            return new List<MapSolarSystemNode>();
        }
    }

    public async Task<List<MapSolarSystemNode>> GetSystemsWithinJumpsAsync(int originSystemId, int maxJumps)
    {
        try
        {
            var graph = await BuildSystemGraphAsync();
            var visited = new HashSet<int>();
            var queue = new Queue<(int systemId, int distance)>();
            var systemsInRange = new HashSet<int>();

            queue.Enqueue((originSystemId, 0));
            visited.Add(originSystemId);
            systemsInRange.Add(originSystemId);

            while (queue.Count > 0)
            {
                var (currentId, distance) = queue.Dequeue();

                if (distance >= maxJumps) continue;

                if (graph.TryGetValue(currentId, out var neighbors))
                {
                    foreach (var neighborId in neighbors)
                    {
                        if (!visited.Add(neighborId)) continue;

                        systemsInRange.Add(neighborId);
                        queue.Enqueue((neighborId, distance + 1));
                    }
                }
            }

            _logger.LogDebug("Found {Count} systems within {Jumps} jumps of system {SystemId}",
                systemsInRange.Count, maxJumps, originSystemId);

            return await GetSystemsByIdsAsync(systemsInRange.ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting systems within jumps");
            return new List<MapSolarSystemNode>();
        }
    }

    public async Task<List<MapConnection>> GetRegionConnectionsAsync()
    {
        try
        {
            await _context.EnsureConnectionAsync();
            using var cmd = _context.Connection.CreateCommand();
            cmd.CommandText = @"
                SELECT DISTINCT
                    s1.regionID as fromRegionID,
                    s2.regionID as toRegionID
                FROM mapSolarSystemJumps j
                JOIN mapSolarSystems s1 ON j.fromSolarSystemID = s1.solarSystemID
                JOIN mapSolarSystems s2 ON j.toSolarSystemID = s2.solarSystemID
                WHERE s1.regionID != s2.regionID
                  AND s1.regionID < 11000000
                  AND s2.regionID < 11000000
                ORDER BY s1.regionID, s2.regionID";

            var connections = new List<MapConnection>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                connections.Add(new MapConnection
                {
                    FromRegionId = reader.GetInt32(0),
                    ToRegionId = reader.GetInt32(1),
                    FromSystemId = 0, // Not relevant for region connections
                    ToSystemId = 0
                });
            }

            _logger.LogInformation("GetRegionConnectionsAsync: {Count} cross-region connections", connections.Count);
            return connections;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting region connections");
            return new List<MapConnection>();
        }
    }

    public async Task<List<MapConnection>> GetSystemConnectionsInRegionAsync(int regionId)
    {
        try
        {
            await _context.EnsureConnectionAsync();
            using var cmd = _context.Connection.CreateCommand();
            cmd.CommandText = @"
                SELECT
                    j.fromSolarSystemID,
                    j.toSolarSystemID,
                    s1.regionID as fromRegionID,
                    s2.regionID as toRegionID,
                    s1.constellationID as fromConstellationID,
                    s2.constellationID as toConstellationID
                FROM mapSolarSystemJumps j
                JOIN mapSolarSystems s1 ON j.fromSolarSystemID = s1.solarSystemID
                JOIN mapSolarSystems s2 ON j.toSolarSystemID = s2.solarSystemID
                WHERE s1.regionID = @regionId AND s2.regionID = @regionId
                ORDER BY j.fromSolarSystemID, j.toSolarSystemID";
            cmd.Parameters.AddWithValue("@regionId", regionId);

            var connections = new List<MapConnection>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                connections.Add(new MapConnection
                {
                    FromSystemId = reader.GetInt32(0),
                    ToSystemId = reader.GetInt32(1),
                    FromRegionId = reader.GetInt32(2),
                    ToRegionId = reader.GetInt32(3),
                    FromConstellationId = reader.GetInt32(4),
                    ToConstellationId = reader.GetInt32(5)
                });
            }

            _logger.LogInformation("GetSystemConnectionsInRegionAsync: Region {RegionId} → {Count} connections", regionId, connections.Count);
            return connections;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting system connections in region {RegionId}", regionId);
            return new List<MapConnection>();
        }
    }

    public async Task<List<MapConnection>> GetCrossRegionConnectionsForSystemAsync(int regionId)
    {
        try
        {
            await _context.EnsureConnectionAsync();
            using var cmd = _context.Connection.CreateCommand();
            cmd.CommandText = @"
                SELECT
                    j.fromSolarSystemID,
                    j.toSolarSystemID,
                    s1.regionID as fromRegionID,
                    s2.regionID as toRegionID,
                    s1.constellationID as fromConstellationID,
                    s2.constellationID as toConstellationID,
                    r2.regionName as toRegionName
                FROM mapSolarSystemJumps j
                JOIN mapSolarSystems s1 ON j.fromSolarSystemID = s1.solarSystemID
                JOIN mapSolarSystems s2 ON j.toSolarSystemID = s2.solarSystemID
                JOIN mapRegions r2 ON s2.regionID = r2.regionID
                WHERE s1.regionID = @regionId AND s2.regionID != @regionId
                ORDER BY j.fromSolarSystemID, j.toSolarSystemID";
            cmd.Parameters.AddWithValue("@regionId", regionId);

            var connections = new List<MapConnection>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                connections.Add(new MapConnection
                {
                    FromSystemId = reader.GetInt32(0),
                    ToSystemId = reader.GetInt32(1),
                    FromRegionId = reader.GetInt32(2),
                    ToRegionId = reader.GetInt32(3),
                    FromConstellationId = reader.GetInt32(4),
                    ToConstellationId = reader.GetInt32(5),
                    ToRegionName = reader.GetString(6)
                });
            }

            _logger.LogInformation("GetCrossRegionConnectionsForSystemAsync: Region {RegionId} → {Count} cross-region connections", regionId, connections.Count);
            return connections;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cross-region connections for region {RegionId}", regionId);
            return new List<MapConnection>();
        }
    }

    public async Task<List<MapConnection>> GetConnectionsForSystemsAsync(List<int> systemIds)
    {
        if (!systemIds.Any()) return new List<MapConnection>();

        try
        {
            await _context.EnsureConnectionAsync();
            var idsString = string.Join(",", systemIds);
            using var cmd = _context.Connection.CreateCommand();
            cmd.CommandText = $@"
                SELECT
                    j.fromSolarSystemID,
                    j.toSolarSystemID,
                    s1.regionID as fromRegionID,
                    s2.regionID as toRegionID,
                    s1.constellationID as fromConstellationID,
                    s2.constellationID as toConstellationID
                FROM mapSolarSystemJumps j
                JOIN mapSolarSystems s1 ON j.fromSolarSystemID = s1.solarSystemID
                JOIN mapSolarSystems s2 ON j.toSolarSystemID = s2.solarSystemID
                WHERE j.fromSolarSystemID IN ({idsString})
                  AND j.toSolarSystemID IN ({idsString})
                ORDER BY j.fromSolarSystemID, j.toSolarSystemID";

            var connections = new List<MapConnection>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                connections.Add(new MapConnection
                {
                    FromSystemId = reader.GetInt32(0),
                    ToSystemId = reader.GetInt32(1),
                    FromRegionId = reader.GetInt32(2),
                    ToRegionId = reader.GetInt32(3),
                    FromConstellationId = reader.GetInt32(4),
                    ToConstellationId = reader.GetInt32(5)
                });
            }

            _logger.LogInformation("GetConnectionsForSystemsAsync: {Count} systems → {ConnectionCount} connections", systemIds.Count, connections.Count);
            return connections;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting connections for systems");
            return new List<MapConnection>();
        }
    }

    public async Task<Dictionary<int, List<int>>> BuildSystemGraphAsync()
    {
        if (_systemGraph != null) return _systemGraph;

        await _cacheLock.WaitAsync();
        try
        {
            if (_systemGraph != null) return _systemGraph;

            await _context.EnsureConnectionAsync();
            using var cmd = _context.Connection.CreateCommand();
            cmd.CommandText = @"
                SELECT fromSolarSystemID, toSolarSystemID
                FROM mapSolarSystemJumps";

            var graph = new Dictionary<int, List<int>>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var from = reader.GetInt32(0);
                var to = reader.GetInt32(1);

                if (!graph.ContainsKey(from))
                    graph[from] = new List<int>();
                graph[from].Add(to);
            }

            _logger.LogInformation("Built system graph with {NodeCount} nodes and {EdgeCount} edges",
                graph.Count, graph.Values.Sum(v => v.Count));

            _systemGraph = graph;
            return graph;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error building system graph");
            return new Dictionary<int, List<int>>();
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    public async Task<Dictionary<int, SystemActivity>> GetSystemActivitiesAsync(List<int> systemIds)
    {
        if (!systemIds.Any())
            return new Dictionary<int, SystemActivity>();

        try
        {
            // Hole IEsiApiService aus dem ServiceProvider (Scoped Service)
            // Wir können keinen Scoped Service direkt in Singleton injizieren
            using var scope = _serviceProvider.CreateScope();
            var esiApi = scope.ServiceProvider.GetRequiredService<IEsiApiService>();

            // Hole beide ESI-Endpoints parallel
            var killsTask = esiApi.GetSystemKillsAsync();
            var jumpsTask = esiApi.GetSystemJumpsAsync();

            await Task.WhenAll(killsTask, jumpsTask);

            var kills = await killsTask ?? new List<WALLEve.Models.Esi.Universe.SystemKills>();
            var jumps = await jumpsTask ?? new List<WALLEve.Models.Esi.Universe.SystemJumps>();

            // Konvertiere zu Dictionary für schnellen Lookup
            var killsDict = kills.ToDictionary(k => k.SystemId);
            var jumpsDict = jumps.ToDictionary(j => j.SystemId);

            // Kombiniere Daten für angefragte Systeme
            var activities = new Dictionary<int, SystemActivity>();
            foreach (var systemId in systemIds)
            {
                var activity = new SystemActivity
                {
                    SystemId = systemId,
                    ShipKills = killsDict.TryGetValue(systemId, out var k) ? k.ShipKills : 0,
                    NpcKills = killsDict.TryGetValue(systemId, out var k2) ? k2.NpcKills : 0,
                    PodKills = killsDict.TryGetValue(systemId, out var k3) ? k3.PodKills : 0,
                    ShipJumps = jumpsDict.TryGetValue(systemId, out var j) ? j.ShipJumps : 0
                };

                activities[systemId] = activity;
            }

            _logger.LogInformation("GetSystemActivitiesAsync: Loaded activities for {Count} systems", activities.Count);
            return activities;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting system activities");
            return new Dictionary<int, SystemActivity>();
        }
    }
}
