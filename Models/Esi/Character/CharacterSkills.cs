using System.Text.Json.Serialization;

namespace WALLEve.Models.Esi.Character;

/// <summary>
/// Skills-Response von ESI
/// </summary>
public class CharacterSkills
{
    [JsonPropertyName("skills")]
    public List<CharacterSkill> Skills { get; set; } = new();

    [JsonPropertyName("total_sp")]
    public long TotalSp { get; set; }

    [JsonPropertyName("unallocated_sp")]
    public int? UnallocatedSp { get; set; }
}
