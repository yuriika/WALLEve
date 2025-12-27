using WALLEve.Models.Esi.Character;
using WALLEve.Models.Sde;

namespace WALLEve.Services.Sde.Interfaces;

/// <summary>
/// Service für SDE-Zugriff auf Character-bezogene Daten (Bloodlines, Skills, etc.)
/// </summary>
public interface ISdeCharacterService
{
    /// <summary>
    /// Holt den Namen einer Bloodline (Rasse)
    /// </summary>
    Task<string?> GetBloodlineNameAsync(int bloodlineId);

    /// <summary>
    /// Holt alle Skills eines Charakters mit Details aus der SDE
    /// </summary>
    Task<List<SkillInfo>> GetSkillDetailsAsync(IEnumerable<CharacterSkill> skills);
}
