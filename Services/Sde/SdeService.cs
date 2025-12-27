using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using WALLEve.Configuration;
using WALLEve.Models.Esi.Character;
using WALLEve.Models.Sde;
using WALLEve.Services.Sde.Interfaces;

namespace WALLEve.Services.Sde;

public class SdeService : ISdeService, IDisposable
{
    private readonly EveOnlineSettings _settings;
    private readonly ApplicationSettings _appSettings;
    private readonly ILogger<SdeService> _logger;
    private readonly string _dbPath;
    private SqliteConnection? _connection;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);

    public SdeService(
        IOptions<EveOnlineSettings> settings,
        IOptions<ApplicationSettings> appSettings,
        ILogger<SdeService> logger)
    {
        _settings = settings.Value;
        _appSettings = appSettings.Value;
        _logger = logger;

        _dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            _appSettings.AppDataFolder,
            _appSettings.DataFolder,
            _settings.Sde.LocalFileName);

        _logger.LogInformation("SDE Service initialized with path: {Path}", _dbPath);
    }

    public async Task<bool> IsDatabaseAvailableAsync()
    {
        if (!File.Exists(_dbPath))
        {
            _logger.LogWarning("SDE database file not found at {Path}", _dbPath);
            return false;
        }

        try
        {
            await EnsureConnectionAsync();
            return _connection?.State == ConnectionState.Open;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking SDE database availability");
            return false;
        }
    }

    private async Task EnsureConnectionAsync()
    {
        await _connectionLock.WaitAsync();
        try
        {
            if (_connection?.State == ConnectionState.Open)
                return;

            _connection?.Dispose();
            _connection = new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly");
            await _connection.OpenAsync();
            _logger.LogDebug("SDE database connection opened");
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async Task<string?> GetTypeNameAsync(int typeId)
    {
        try
        {
            await EnsureConnectionAsync();

            using var cmd = _connection!.CreateCommand();
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
            await EnsureConnectionAsync();

            using var cmd = _connection!.CreateCommand();
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
            await EnsureConnectionAsync();

            using var cmd = _connection!.CreateCommand();
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
            await EnsureConnectionAsync();

            using var cmd = _connection!.CreateCommand();
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

    public async Task<string?> GetBloodlineNameAsync(int bloodlineId)
    {
        try
        {
            await EnsureConnectionAsync();

            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "SELECT bloodlineName FROM chrBloodlines WHERE bloodlineID = @bloodlineId";
            cmd.Parameters.AddWithValue("@bloodlineId", bloodlineId);

            var result = await cmd.ExecuteScalarAsync();
            return result?.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting bloodline name for bloodlineId {BloodlineId}", bloodlineId);
            return null;
        }
    }

    public async Task<List<SkillInfo>> GetSkillDetailsAsync(IEnumerable<CharacterSkill> skills)
    {
        var result = new List<SkillInfo>();
        var skillsList = skills.ToList();

        if (!skillsList.Any())
            return result;

        try
        {
            await EnsureConnectionAsync();

            // Batch query mit IN-Clause statt N einzelne Queries
            var skillIds = string.Join(",", skillsList.Select(s => s.SkillId));
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = $@"
                SELECT t.typeID, t.typeName, g.groupName
                FROM invTypes t
                JOIN invGroups g ON t.groupID = g.groupID
                WHERE t.typeID IN ({skillIds})";

            using var reader = await cmd.ExecuteReaderAsync();

            // Dictionary für schnelles Lookup der Skill-Daten
            var sdeData = new Dictionary<int, (string Name, string GroupName)>();
            while (await reader.ReadAsync())
            {
                var typeId = reader.GetInt32(0);
                var typeName = reader.GetString(1);
                var groupName = reader.GetString(2);
                sdeData[typeId] = (typeName, groupName);
            }

            // Skills mit SDE-Daten kombinieren
            foreach (var skill in skillsList)
            {
                if (sdeData.TryGetValue(skill.SkillId, out var data))
                {
                    result.Add(new SkillInfo
                    {
                        SkillId = skill.SkillId,
                        Name = data.Name,
                        GroupName = data.GroupName,
                        TrainedSkillLevel = skill.TrainedSkillLevel,
                        SkillPointsInSkill = skill.SkillPointsInSkill,
                        ActiveSkillLevel = skill.ActiveSkillLevel
                    });
                }
                else
                {
                    _logger.LogWarning("Skill {SkillId} not found in SDE", skill.SkillId);
                }
            }

            return result.OrderBy(s => s.GroupName).ThenBy(s => s.Name).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting skill details");
            return result;
        }
    }

    public void Dispose()
    {
        _connection?.Dispose();
        _connectionLock?.Dispose();
    }
}