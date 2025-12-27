using System.Text.Json.Serialization;

namespace WALLEve.Models.Esi.Character;

/// <summary>
/// Skill eines Charakters (von ESI)
/// </summary>
public class CharacterSkill
{
    [JsonPropertyName("skill_id")]
    public int SkillId { get; set; }

    [JsonPropertyName("trained_skill_level")]
    public int TrainedSkillLevel { get; set; }

    [JsonPropertyName("skillpoints_in_skill")]
    public long SkillPointsInSkill { get; set; }

    [JsonPropertyName("active_skill_level")]
    public int ActiveSkillLevel { get; set; }
}
