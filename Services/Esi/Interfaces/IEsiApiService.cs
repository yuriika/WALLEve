using WALLEve.Models.Esi.Alliance;
using WALLEve.Models.Esi.Character;
using WALLEve.Models.Esi.Corporation;
using WALLEve.Models.Esi.Markets;
using WALLEve.Models.Esi.Universe;
using WALLEve.Models.Esi.Wallet;

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

    // Wallet endpoints
    Task<List<WalletJournalEntry>?> GetWalletJournalAsync(int characterId, int page = 1);
    Task<List<WalletTransaction>?> GetWalletTransactionsAsync(int characterId);

    /// <summary>
    /// Holt ALLE Seiten des Wallet Journals mit automatischer Paginierung
    /// </summary>
    Task<List<WalletJournalEntry>> GetAllWalletJournalPagesAsync(int characterId);

    /// <summary>
    /// Holt ALLE Seiten der Wallet Transactions mit automatischer Paginierung
    /// </summary>
    Task<List<WalletTransaction>> GetAllWalletTransactionsPagesAsync(int characterId);

    // Market endpoints
    Task<List<MarketOrder>?> GetMarketOrdersAsync(int characterId);
    Task<List<MarketOrderHistory>?> GetMarketOrderHistoryAsync(int characterId);

    // Corporation Wallet endpoints
    /// <summary>
    /// Holt Corporation Wallet Journal für eine bestimmte Division
    /// GET /corporations/{corporation_id}/wallets/{division}/journal/
    /// Scope: esi-wallet.read_corporation_wallets.v1
    /// </summary>
    Task<List<WalletJournalEntry>?> GetCorporationWalletJournalAsync(int corporationId, int division, int page = 1);

    /// <summary>
    /// Holt Corporation Wallet Transactions für eine bestimmte Division
    /// GET /corporations/{corporation_id}/wallets/{division}/transactions/
    /// Scope: esi-wallet.read_corporation_wallets.v1
    /// </summary>
    Task<List<WalletTransaction>?> GetCorporationWalletTransactionsAsync(int corporationId, int division);
}
