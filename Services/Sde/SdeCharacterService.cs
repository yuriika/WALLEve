using WALLEve.Models.Esi.Character;
using WALLEve.Models.Sde;
using WALLEve.Services.Sde.Interfaces;

namespace WALLEve.Services.Sde;

/// <summary>
/// Service für SDE-Zugriff auf Character-bezogene Daten (Bloodlines, Skills, etc.)
/// </summary>
public class SdeCharacterService : ISdeCharacterService
{
    private readonly SdeDbContext _context;
    private readonly ILogger<SdeCharacterService> _logger;

    public SdeCharacterService(
        SdeDbContext context,
        ILogger<SdeCharacterService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<string?> GetBloodlineNameAsync(int bloodlineId)
    {
        try
        {
            await _context.EnsureConnectionAsync();

            using var cmd = _context.Connection.CreateCommand();
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
            await _context.EnsureConnectionAsync();

            // Batch query mit IN-Clause statt N einzelne Queries
            var skillIds = string.Join(",", skillsList.Select(s => s.SkillId));
            using var cmd = _context.Connection.CreateCommand();
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
}
