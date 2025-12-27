namespace WALLEve.Models.Sde;

/// <summary>
/// Skill-Informationen aus SDE + ESI kombiniert
/// </summary>
public class SkillInfo
{
    public int SkillId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public int TrainedSkillLevel { get; set; }
    public long SkillPointsInSkill { get; set; }
    public int ActiveSkillLevel { get; set; }

    public string FormattedSkillPoints => SkillPointsInSkill.ToString("N0");
}
