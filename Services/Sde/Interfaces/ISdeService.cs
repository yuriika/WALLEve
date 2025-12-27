using WALLEve.Models.Esi.Character;
using WALLEve.Models.Sde;

namespace WALLEve.Services.Sde.Interfaces;

/// <summary>
/// Service für Zugriff auf die EVE Static Data Export (SDE) Datenbank
/// </summary>
public interface ISdeService
{
    /// <summary>
    /// Prüft ob die SDE-Datenbank verfügbar und nutzbar ist
    /// </summary>
    Task<bool> IsDatabaseAvailableAsync();

    /// <summary>
    /// Holt den Namen eines Types (z.B. Schiff, Item, Skill)
    /// </summary>
    /// <param name="typeId">EVE Type ID</param>
    /// <returns>Name oder null falls nicht gefunden</returns>
    Task<string?> GetTypeNameAsync(int typeId);

    /// <summary>
    /// Holt die Gruppe eines Types (z.B. "Battleship", "Cruiser")
    /// </summary>
    /// <param name="typeId">EVE Type ID</param>
    /// <returns>Gruppenname oder null falls nicht gefunden</returns>
    Task<string?> GetTypeGroupAsync(int typeId);

    /// <summary>
    /// Holt Informationen über ein Sonnensystem
    /// </summary>
    /// <param name="solarSystemId">System ID</param>
    /// <returns>System-Info oder null falls nicht gefunden</returns>
    Task<SolarSystemInfo?> GetSolarSystemAsync(int solarSystemId);

    /// <summary>
    /// Holt den Namen einer Region
    /// </summary>
    /// <param name="regionId">Region ID</param>
    /// <returns>Regionname oder null falls nicht gefunden</returns>
    Task<string?> GetRegionNameAsync(int regionId);

    /// <summary>
    /// Holt den Namen einer Bloodline (Rasse)
    /// </summary>
    /// <param name="bloodlineId">Bloodline ID</param>
    /// <returns>Bloodline-Name oder null falls nicht gefunden</returns>
    Task<string?> GetBloodlineNameAsync(int bloodlineId);

    /// <summary>
    /// Holt alle Skills eines Charakters mit Details aus der SDE
    /// </summary>
    /// <param name="skills">Skills vom ESI API</param>
    /// <returns>Liste mit angereicherten Skill-Infos</returns>
    Task<List<SkillInfo>> GetSkillDetailsAsync(IEnumerable<CharacterSkill> skills);
}