using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WALLEve.Models.Authentication;
using WALLEve.Models.Configuration;
using WALLEve.Models.Database;
using WALLEve.Models.Esi.Character;
using WALLEve.Models.Esi.Alliance;
using WALLEve.Models.Esi.Corporation;
using WALLEve.Models.Esi.Markets;
using WALLEve.Models.Measurement;
using WALLEve.Models.Esi.Universe;
using WALLEve.Models.Esi.Wallet;
using WALLEve.Models.Sde;
using WALLEve.Models.Wallet;
using WALLEve.Services.Authentication.Interfaces;
using WALLEve.Services.Esi.Interfaces;
using WALLEve.Services.Sde.Interfaces;
using WALLEve.Services.Wallet;
using WALLEve.Services.Wallet.Interfaces;

namespace WALLEve.Tests;

/// <summary>
/// Consumer-Tests der Datenqualitäts-Umstellung (Issue #26):
/// Ein fehlgeschlagener ESI-Abruf ist kein „leeres Konto" — der
/// WalletService liefert Status Failed mit Meldung statt einer leeren
/// Liste; gültig leere Ergebnisse bleiben Empty.
/// </summary>
public class WalletServiceDataQualityTests
{
    private const int CharacterId = 90073315;

    private sealed class FakeEsiApiService : IEsiApiService
    {
        public List<WalletJournalEntry>? Journal { get; set; }
        public List<WalletTransaction>? Transactions { get; set; }
        public List<MarketOrder>? Orders { get; set; } = new();
        public List<MarketOrderHistory>? OrderHistory { get; set; } = new();

        public Task<List<WalletJournalEntry>?> GetAllWalletJournalPagesAsync(int characterId, CancellationToken ct = default)
            => Task.FromResult(Journal);
        public Task<List<WalletTransaction>?> GetAllWalletTransactionsPagesAsync(int characterId, CancellationToken ct = default)
            => Task.FromResult(Transactions);
        public Task<List<MarketOrder>?> GetMarketOrdersAsync(int characterId) => Task.FromResult(Orders);
        public Task<List<MarketOrderHistory>?> GetMarketOrderHistoryAsync(int characterId) => Task.FromResult(OrderHistory);

        public Task<CharacterOverview?> GetCharacterOverviewAsync() => throw new NotImplementedException();
        public Task<EveCharacter?> GetCharacterAsync(int characterId) => throw new NotImplementedException();
        public Task<EveCorporation?> GetCorporationAsync(int corporationId) => throw new NotImplementedException();
        public Task<EveAlliance?> GetAllianceAsync(int allianceId) => throw new NotImplementedException();
        public Task<double?> GetWalletBalanceAsync(int characterId) => throw new NotImplementedException();
        public Task<CharacterLocation?> GetLocationAsync(int characterId) => throw new NotImplementedException();
        public Task<CharacterShip?> GetCurrentShipAsync(int characterId) => throw new NotImplementedException();
        public Task<CharacterOnlineStatus?> GetOnlineStatusAsync(int characterId) => throw new NotImplementedException();
        public Task<SolarSystem?> GetSolarSystemAsync(int systemId) => throw new NotImplementedException();
        public Task<EveType?> GetTypeAsync(int typeId) => throw new NotImplementedException();
        public Task<StructureLookupResult> GetStructureAsync(long structureId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<CharacterSkills?> GetCharacterSkillsAsync() => throw new NotImplementedException();
        public Task<List<CharacterAsset>?> GetCharacterAssetsAsync(int characterId, CancellationToken ct = default) => throw new NotImplementedException();

        public Task<List<CharacterMiningEntry>?> GetCharacterMiningLedgerAsync(int characterId, CancellationToken ct = default) => throw new NotImplementedException();

        public Task<List<CharacterBlueprint>?> GetCharacterBlueprintsAsync(int characterId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<CharacterIndustryJob>?> GetCharacterIndustryJobsAsync(int characterId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<WalletJournalEntry>?> GetWalletJournalAsync(int characterId, int page = 1) => throw new NotImplementedException();
        public Task<List<WalletTransaction>?> GetWalletTransactionsAsync(int characterId) => throw new NotImplementedException();
        public Task<List<WalletJournalEntry>?> GetCorporationWalletJournalAsync(int corporationId, int division, int page = 1) => throw new NotImplementedException();
        public Task<List<WalletTransaction>?> GetCorporationWalletTransactionsAsync(int corporationId, int division) => throw new NotImplementedException();
        public Task<List<SystemJumps>?> GetSystemJumpsAsync() => throw new NotImplementedException();
        public Task<List<SystemKills>?> GetSystemKillsAsync() => throw new NotImplementedException();
        public Task<List<RegionalMarketOrder>?> GetRegionalMarketOrdersAsync(int regionId, int? typeId = null, string orderType = "all", int page = 1) => throw new NotImplementedException();
        public Task<List<RegionalMarketOrder>?> GetAllRegionalMarketOrdersAsync(int regionId, int? typeId = null, string orderType = "all", CancellationToken ct = default, Action<RegionalScanPageTelemetry>? telemetrySink = null) => throw new NotImplementedException();
        public Task<List<MarketHistoryEntry>?> GetMarketHistoryAsync(int regionId, int typeId) => throw new NotImplementedException();
        public Task<List<MarketPrice>?> GetMarketPricesAsync() => throw new NotImplementedException();
    }

    private sealed class FakeSdeUniverseService : ISdeUniverseService
    {
        public Task<bool> IsDatabaseAvailableAsync() => Task.FromResult(false);
        public Task<int?> GetRegionIdForLocationAsync(long locationId) => Task.FromResult<int?>(null);
        public Task<int?> GetSolarSystemIdForLocationAsync(long locationId) => Task.FromResult<int?>(null);
        public Task<string?> GetTypeNameAsync(int typeId) => Task.FromResult<string?>(null);
        public Task<string?> GetTypeGroupAsync(int typeId) => Task.FromResult<string?>(null);
        public Task<Dictionary<int, string?>> GetTypeGroupsAsync(IReadOnlyCollection<int> typeIds) => Task.FromResult(new Dictionary<int, string?>());
        public Task<Dictionary<int, string?>> GetTypeNamesAsync(IReadOnlyCollection<int> typeIds) => Task.FromResult(new Dictionary<int, string?>());
        public Task<Dictionary<int, SolarSystemInfo?>> GetSolarSystemsAsync(IReadOnlyCollection<int> solarSystemIds) => Task.FromResult(new Dictionary<int, SolarSystemInfo?>());
        public Task<SolarSystemInfo?> GetSolarSystemAsync(int solarSystemId) => Task.FromResult<SolarSystemInfo?>(null);
        public Task<StationInfo?> GetStationAsync(long stationId) => Task.FromResult<StationInfo?>(null);
        public Task<string?> GetRegionNameAsync(int regionId) => Task.FromResult<string?>(null);
        public Task<string?> GetLocationNameAsync(long locationId) => Task.FromResult<string?>(null);
        public Task<Dictionary<int, string>> GetAllMarketItemsAsync() => Task.FromResult(new Dictionary<int, string>());
        public Task<Dictionary<int, string>> GetAllRegionsAsync() => Task.FromResult(new Dictionary<int, string>());
        public Task<Dictionary<int, string>> SearchSolarSystemsAsync(string searchQuery, int maxResults = 10) => Task.FromResult(new Dictionary<int, string>());
    }

    private sealed class FakeAuthService : IEveAuthenticationService
    {
        public Task<EveAuthState?> GetAuthStateAsync()
            => Task.FromResult<EveAuthState?>(new EveAuthState
            {
                AccessToken = "tok", RefreshToken = "ref",
                CharacterId = CharacterId, CharacterName = "Test"
            });

        public Task<bool> IsAuthenticatedAsync() => Task.FromResult(true);
        public string GetLoginUrl() => "http://login";
        public Task<bool> HandleCallbackAsync(string code, string state) => Task.FromResult(true);
        public Task<string?> GetAccessTokenAsync() => Task.FromResult<string?>("tok");
        public Task LogoutAsync() => Task.CompletedTask;
        public Task<List<KnownCharacter>> GetAllCharactersAsync() => Task.FromResult(new List<KnownCharacter>());
        public Task<bool> SwitchCharacterAsync(int characterId) => Task.FromResult(true);
        public Task<bool> ForceRefreshAccessTokenAsync() => Task.FromResult(true);

        event EventHandler<bool>? IEveAuthenticationService.AuthenticationStateChanged
        {
            add { }
            remove { }
        }
    }

    private sealed class FakeLinkService : IWalletLinkService
    {
        public Task<List<WalletEntryLink>> GetCharacterLinksAsync(int characterId) => Task.FromResult(new List<WalletEntryLink>());
        public Task<List<WalletEntryLink>> GetCharacterLinksByEntryIdsAsync(int characterId, IEnumerable<long> entryIds) => Task.FromResult(new List<WalletEntryLink>());
        public Task SaveCharacterLinksAsync(int characterId, string characterName, IEnumerable<WalletEntryLink> links) => Task.CompletedTask;
        public Task<List<WalletEntryLink>> GetCorporationLinksAsync(int corporationId, int division) => Task.FromResult(new List<WalletEntryLink>());
        public Task<List<WalletEntryLink>> GetCorporationLinksByEntryIdsAsync(int corporationId, int division, IEnumerable<long> entryIds) => Task.FromResult(new List<WalletEntryLink>());
        public Task SaveCorporationLinksAsync(int corporationId, string corporationName, int division, IEnumerable<WalletEntryLink> links) => Task.CompletedTask;
        public Task<bool> VerifyLinkAsync(int linkId) => Task.FromResult(true);
        public Task<bool> RejectLinkAsync(int linkId) => Task.FromResult(true);
        public Task<WalletEntryLink> CreateManualLinkAsync(long sourceEntryId, long targetEntryId, LinkType type, int? characterId = null, int? corporationId = null, int? division = null, string? userNotes = null) => throw new NotImplementedException();
        public Task<bool> DeleteLinkAsync(int linkId) => Task.FromResult(true);
        public Task<int> CleanupOldLinksAsync(int daysToKeep = 90) => Task.FromResult(0);
        public Task<int> DeleteCharacterLinksAsync(int characterId) => Task.FromResult(0);
        public Task<int> DeleteCorporationLinksAsync(int corporationId) => Task.FromResult(0);
    }

    private static WalletService CreateService(FakeEsiApiService esi) => new(
        esi,
        new FakeSdeUniverseService(),
        new FakeAuthService(),
        new FakeLinkService(),
        NullLogger<WalletService>.Instance,
        Options.Create(new WalletOptions()));

    private static WalletJournalEntry JournalEntry(long id) => new()
    {
        Id = id, Date = DateTime.UtcNow, RefType = "buy",
        Description = $"entry {id}", Amount = 10, Balance = 10
    };

    [Fact]
    public async Task GetCombinedWalletData_AllSourcesFailed_ReturnsFailed_NotEmpty()
    {
        var esi = new FakeEsiApiService { Journal = null, Transactions = null, Orders = null, OrderHistory = null };
        var service = CreateService(esi);

        var result = await service.GetCombinedWalletDataAsync();

        // Fehler ist KEIN „leeres Konto"
        Assert.Equal(WalletDataStatus.Failed, result.Status);
        Assert.NotNull(result.StatusMessage);
        Assert.Contains("Wallet-Journal", result.StatusMessage);
        Assert.Contains("Wallet-Transaktionen", result.StatusMessage);
        Assert.NotEqual(WalletDataStatus.Empty, result.Status);
    }

    [Fact]
    public async Task GetCombinedWalletData_AllEmpty_IsValidEmpty()
    {
        var esi = new FakeEsiApiService
        {
            Journal = new List<WalletJournalEntry>(),
            Transactions = new List<WalletTransaction>(),
            Orders = new List<MarketOrder>(),
            OrderHistory = new List<MarketOrderHistory>()
        };
        var service = CreateService(esi);

        var result = await service.GetCombinedWalletDataAsync();

        // Gültig leeres Ergebnis: es gibt tatsächlich keine Einträge
        Assert.Equal(WalletDataStatus.Empty, result.Status);
        Assert.Null(result.StatusMessage);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public async Task GetCombinedWalletData_PartialFailure_KeepsSuccessfulData_AndFlagsFailure()
    {
        var esi = new FakeEsiApiService
        {
            Journal = new List<WalletJournalEntry> { JournalEntry(1), JournalEntry(2) },
            Transactions = null,   // fehlgeschlagen
            Orders = new List<MarketOrder> { new() { OrderId = 10, TypeId = 34, Price = 100 } },
            OrderHistory = null    // fehlgeschlagen
        };
        var service = CreateService(esi);

        var result = await service.GetCombinedWalletDataAsync();

        // Erfolgreich geladene Teile bleiben sichtbar (best effort), Fehler wird signalisiert
        Assert.Equal(WalletDataStatus.Failed, result.Status);
        Assert.Contains("Wallet-Transaktionen", result.StatusMessage);
        Assert.Contains("Order-Historie", result.StatusMessage);
        Assert.Equal(2, result.Entries.Count);
    }
}