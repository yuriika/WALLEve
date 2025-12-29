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
}
