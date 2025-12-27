using WALLEve.Models.Esi.Alliance;
using WALLEve.Models.Esi.Character;
using WALLEve.Models.Esi.Corporation;
using WALLEve.Models.Esi.Universe;

namespace WALLEve.Services.Esi.Interfaces;

public interface IEsiApiService
{
    Task<CharacterOverview?> GetCharacterOverviewAsync();
    Task<EveCharacter?> GetCharacterAsync(int characterId);
    Task<EveCorporation?> GetCorporationAsync(int corporationId);
    Task<EveAlliance?> GetAllianceAsync(int allianceId);
    Task<double?> GetWalletBalanceAsync(int characterId);
    Task<CharacterLocation?> GetLocationAsync(int characterId);
    Task<CharacterShip?> GetCurrentShipAsync(int characterId);
    Task<CharacterOnlineStatus?> GetOnlineStatusAsync(int characterId);
    Task<SolarSystem?> GetSolarSystemAsync(int systemId);
    Task<EveType?> GetTypeAsync(int typeId);

    /// <summary>
    /// Holt alle Skills des Charakters
    /// </summary>
    Task<CharacterSkills?> GetCharacterSkillsAsync();
}
