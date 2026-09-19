using WALLEve.Models.Sde;
using WALLEve.Services.Sde.Interfaces;

namespace WALLEve.Services.Sde;

/// <summary>
/// Service für SDE-Zugriff auf Universe-Daten (Types, Solar Systems, Regions, etc.)
/// </summary>
public class SdeUniverseService : ISdeUniverseService
{
    private readonly SdeDbContext _context;
    private readonly ILogger<SdeUniverseService> _logger;

    // Cache für die komplette Item-Liste — SDE-Items sind statisch und ändern sich
    // nur bei einem SDE-Update (App-Neustart). 15k Zeilen bei jedem Tab-Wechsel
    // neu aus SQLite zu laden, ist unnötig.
    private Dictionary<int, string>? _marketItemsCache;

    public SdeUniverseService(
        SdeDbContext context,
        ILogger<SdeUniverseService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<bool> IsDatabaseAvailableAsync()
    {
        return await _context.IsConnectionOpenAsync();
    }

    public async Task<string?> GetTypeNameAsync(int typeId)
    {
        try
        {
            await _context.EnsureConnectionAsync();

            using var cmd = _context.Connection.CreateCommand();
            cmd.CommandText = "SELECT typeName FROM invTypes WHERE typeID = @typeId";
            cmd.Parameters.AddWithValue("@typeId", typeId);

            var result = await cmd.ExecuteScalarAsync();
            return result?.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting type name for typeId {TypeId}", typeId);
            return null;
        }
    }

    public async Task<string?> GetTypeGroupAsync(int typeId)
    {
        try
        {
            await _context.EnsureConnectionAsync();

            using var cmd = _context.Connection.CreateCommand();
            cmd.CommandText = @"
                SELECT g.groupName
                FROM invTypes t
                JOIN invGroups g ON t.groupID = g.groupID
                WHERE t.typeID = @typeId";
            cmd.Parameters.AddWithValue("@typeId", typeId);

            var result = await cmd.ExecuteScalarAsync();
            return result?.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting type group for typeId {TypeId}", typeId);
            return null;
        }
    }

    public async Task<Dictionary<int, string?>> GetTypeGroupsAsync(IReadOnlyCollection<int> typeIds)
    {
        var result = new Dictionary<int, string?>();
        var distinct = typeIds.Distinct().ToList();
        if (distinct.Count == 0) return result;

        try
        {
            await _context.EnsureConnectionAsync();

            // Gebündelt in Blöcken von 500 (SQLite-Variablenlimit) — trotzdem
            // ein Lookup statt eines N+1 pro TypeId (Review #157, Kategorieauflösung).
            foreach (var chunk in distinct.Chunk(500))
            {
                using var cmd = _context.Connection.CreateCommand();
                var placeholders = new List<string>(chunk.Length);
                for (var i = 0; i < chunk.Length; i++)
                {
                    placeholders.Add($"@p{i}");
                    cmd.Parameters.AddWithValue($"@p{i}", chunk[i]);
                }
                cmd.CommandText = $@"
                    SELECT t.typeID, g.groupName
                    FROM invTypes t
                    JOIN invGroups g ON t.groupID = g.groupID
                    WHERE t.typeID IN ({string.Join(", ", placeholders)})";

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    result[reader.GetInt32(0)] = reader.IsDBNull(1) ? null : reader.GetString(1);
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting type groups for {Count} typeIds", distinct.Count);
            return result;
        }
    }

    public async Task<Dictionary<int, string?>> GetTypeNamesAsync(IReadOnlyCollection<int> typeIds)
    {
        var result = new Dictionary<int, string?>();
        var distinct = typeIds.Distinct().ToList();
        if (distinct.Count == 0) return result;

        try
        {
            await _context.EnsureConnectionAsync();

            // Gebündelt in Blöcken von 500 (SQLite-Variablenlimit).
            foreach (var chunk in distinct.Chunk(500))
            {
                using var cmd = _context.Connection.CreateCommand();
                var placeholders = new List<string>(chunk.Length);
                for (var i = 0; i < chunk.Length; i++)
                {
                    placeholders.Add($"@p{i}");
                    cmd.Parameters.AddWithValue($"@p{i}", chunk[i]);
                }
                cmd.CommandText = $@"
                    SELECT typeID, typeName
                    FROM invTypes
                    WHERE typeID IN ({string.Join(", ", placeholders)})";

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    result[reader.GetInt32(0)] = reader.IsDBNull(1) ? null : reader.GetString(1);
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting type names for {Count} typeIds", distinct.Count);
            return result;
        }
    }

    public async Task<Dictionary<int, SolarSystemInfo?>> GetSolarSystemsAsync(IReadOnlyCollection<int> solarSystemIds)
    {
        var result = new Dictionary<int, SolarSystemInfo?>();
        var distinct = solarSystemIds.Distinct().ToList();
        if (distinct.Count == 0) return result;

        try
        {
            await _context.EnsureConnectionAsync();

            // Gebündelt in Blöcken von 500 (SQLite-Variablenlimit).
            foreach (var chunk in distinct.Chunk(500))
            {
                using var cmd = _context.Connection.CreateCommand();
                var placeholders = new List<string>(chunk.Length);
                for (var i = 0; i < chunk.Length; i++)
                {
                    placeholders.Add($"@p{i}");
                    cmd.Parameters.AddWithValue($"@p{i}", chunk[i]);
                }
                cmd.CommandText = $@"
                    SELECT s.solarSystemID, s.solarSystemName, s.security,
                           r.regionID, r.regionName
                    FROM mapSolarSystems s
                    JOIN mapRegions r ON s.regionID = r.regionID
                    WHERE s.solarSystemID IN ({string.Join(", ", placeholders)})";

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var systemId = reader.GetInt32(0);
                    result[systemId] = new SolarSystemInfo
                    {
                        SolarSystemId = systemId,
                        Name = reader.GetString(1),
                        Security = reader.GetFloat(2),
                        RegionId = reader.GetInt32(3),
                        RegionName = reader.GetString(4)
                    };
                }
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting solar systems for {Count} systemIds", distinct.Count);
            return result;
        }
    }

    public async Task<SolarSystemInfo?> GetSolarSystemAsync(int solarSystemId)
    {
        try
        {
            await _context.EnsureConnectionAsync();

            using var cmd = _context.Connection.CreateCommand();
            cmd.CommandText = @"
                SELECT s.solarSystemID, s.solarSystemName, s.security,
                       r.regionID, r.regionName
                FROM mapSolarSystems s
                JOIN mapRegions r ON s.regionID = r.regionID
                WHERE s.solarSystemID = @systemId";
            cmd.Parameters.AddWithValue("@systemId", solarSystemId);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new SolarSystemInfo
                {
                    SolarSystemId = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    Security = reader.GetFloat(2),
                    RegionId = reader.GetInt32(3),
                    RegionName = reader.GetString(4)
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

    public async Task<StationInfo?> GetStationAsync(long stationId)
    {
        try
        {
            await _context.EnsureConnectionAsync();

            using var cmd = _context.Connection.CreateCommand();
            cmd.CommandText = @"
                SELECT stationName, solarSystemID, regionID
                FROM staStations
                WHERE stationID = @stationId";
            cmd.Parameters.AddWithValue("@stationId", stationId);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new StationInfo
                {
                    StationId = stationId,
                    Name = reader.GetString(0),
                    SolarSystemId = reader.GetInt32(1),
                    RegionId = reader.GetInt32(2)
                };
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting station {StationId}", stationId);
            return null;
        }
    }

    public async Task<string?> GetRegionNameAsync(int regionId)
    {
        try
        {
            await _context.EnsureConnectionAsync();

            using var cmd = _context.Connection.CreateCommand();
            cmd.CommandText = "SELECT regionName FROM mapRegions WHERE regionID = @regionId";
            cmd.Parameters.AddWithValue("@regionId", regionId);

            var result = await cmd.ExecuteScalarAsync();
            return result?.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting region name for regionId {RegionId}", regionId);
            return null;
        }
    }

    public async Task<string?> GetLocationNameAsync(long locationId)
    {
        try
        {
            await _context.EnsureConnectionAsync();

            using var cmd = _context.Connection.CreateCommand();
            cmd.CommandText = "SELECT itemName FROM mapDenormalize WHERE itemID = @locationId";
            cmd.Parameters.AddWithValue("@locationId", locationId);

            var result = await cmd.ExecuteScalarAsync();
            return result?.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting location name for locationId {LocationId}", locationId);
            return null;
        }
    }

    public async Task<int?> GetRegionIdForLocationAsync(long locationId)
    {
        try
        {
            await _context.EnsureConnectionAsync();

            using var cmd = _context.Connection.CreateCommand();
            // mapDenormalize enthält Stationen (itemID = stationID) und Himmelskörper
            // (itemID = solarSystemID) jeweils mit regionID. Spielerstrukturen und
            // Container stehen dort nicht → ehrlich null statt erfundener Region.
            cmd.CommandText = "SELECT regionID FROM mapDenormalize WHERE itemID = @locationId";
            cmd.Parameters.AddWithValue("@locationId", locationId);

            var result = await cmd.ExecuteScalarAsync();
            if (result == null || result == DBNull.Value) return null;
            return Convert.ToInt32(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting region for location {LocationId}", locationId);
            return null;
        }
    }

    public async Task<int?> GetSolarSystemIdForLocationAsync(long locationId)
    {
        try
        {
            await _context.EnsureConnectionAsync();

            using var cmd = _context.Connection.CreateCommand();
            // mapDenormalize enthält Stationen (itemID = stationID) und Sonnensysteme
            // (itemID = solarSystemID) jeweils mit solarSystemID. Spielerstrukturen und
            // Container stehen dort nicht → ehrlich null statt erfundener System-ID (#31).
            cmd.CommandText = "SELECT solarSystemID FROM mapDenormalize WHERE itemID = @locationId";
            cmd.Parameters.AddWithValue("@locationId", locationId);

            var result = await cmd.ExecuteScalarAsync();
            if (result == null || result == DBNull.Value) return null;
            return Convert.ToInt32(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting solar system for location {LocationId}", locationId);
            return null;
        }
    }

    public async Task<Dictionary<int, string>> GetAllMarketItemsAsync()
    {
        // Cache-Hit: SDE-Items sind statisch bis zum nächsten SDE-Update (App-Neustart)
        if (_marketItemsCache != null)
        {
            return _marketItemsCache;
        }

        var items = new Dictionary<int, string>();

        try
        {
            await _context.EnsureConnectionAsync();

            using var cmd = _context.Connection.CreateCommand();
            cmd.CommandText = @"
                SELECT typeID, typeName
                FROM invTypes
                WHERE marketGroupID IS NOT NULL
                AND published = 1
                ORDER BY typeName";

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var typeId = reader.GetInt32(0);
                var typeName = reader.GetString(1);
                items[typeId] = typeName;
            }

            _marketItemsCache = items;
            _logger.LogInformation("Loaded {Count} market items from SDE (cached)", items.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all market items");
        }

        return items;
    }

    public async Task<Dictionary<int, string>> GetAllRegionsAsync()
    {
        var regions = new Dictionary<int, string>();

        try
        {
            await _context.EnsureConnectionAsync();

            using var cmd = _context.Connection.CreateCommand();
            cmd.CommandText = "SELECT regionID, regionName FROM mapRegions ORDER BY regionName";

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var regionId = reader.GetInt32(0);
                var regionName = reader.GetString(1);
                regions[regionId] = regionName;
            }

            _logger.LogInformation("Loaded {Count} regions from SDE", regions.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all regions");
        }

        return regions;
    }

    public async Task<Dictionary<int, string>> SearchSolarSystemsAsync(string searchQuery, int maxResults = 10)
    {
        var systems = new Dictionary<int, string>();

        if (string.IsNullOrWhiteSpace(searchQuery))
            return systems;

        try
        {
            await _context.EnsureConnectionAsync();

            using var cmd = _context.Connection.CreateCommand();
            cmd.CommandText = @"
                SELECT solarSystemID, solarSystemName
                FROM mapSolarSystems
                WHERE solarSystemName LIKE @search
                ORDER BY solarSystemName
                LIMIT @limit";
            cmd.Parameters.AddWithValue("@search", $"%{searchQuery}%");
            cmd.Parameters.AddWithValue("@limit", maxResults);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var systemId = reader.GetInt32(0);
                var systemName = reader.GetString(1);
                systems[systemId] = systemName;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching solar systems with query {Query}", searchQuery);
        }

        return systems;
    }
}
