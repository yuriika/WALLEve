using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using WALLEve.Configuration;
using WALLEve.Models;
using WALLEve.Services.Authentication;

namespace WALLEve.Services.Eve;

public interface IEveApiService
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
}

public class EveApiService : IEveApiService
{
    private readonly EveOnlineSettings _settings;
    private readonly IEveAuthenticationService _authService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<EveApiService> _logger;

    public EveApiService(
        IOptions<EveOnlineSettings> settings,
        IEveAuthenticationService authService,
        IHttpClientFactory httpClientFactory,
        ILogger<EveApiService> logger)
    {
        _settings = settings.Value;
        _authService = authService;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<CharacterOverview?> GetCharacterOverviewAsync()
    {
        var authState = await _authService.GetAuthStateAsync();
        if (authState == null || !authState.IsValid)
        {
            _logger.LogWarning("Cannot get character overview - not authenticated");
            return null;
        }

        var characterId = authState.CharacterId;
        _logger.LogInformation("Loading character overview for ID: {CharacterId}", characterId);
        
        var overview = new CharacterOverview { CharacterId = characterId };

        // Fetch character data first
        _logger.LogDebug("Fetching public character info...");
        overview.Character = await GetCharacterAsync(characterId) ?? new EveCharacter();
        _logger.LogDebug("Character name: {Name}", overview.Character.Name);

        // Fetch authenticated data in parallel
        _logger.LogDebug("Fetching authenticated endpoints...");
        
        var walletTask = GetWalletBalanceAsync(characterId);
        var locationTask = GetLocationAsync(characterId);
        var shipTask = GetCurrentShipAsync(characterId);
        var onlineTask = GetOnlineStatusAsync(characterId);

        try
        {
            await Task.WhenAll(walletTask, locationTask, shipTask, onlineTask);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Some authenticated API calls failed");
        }

        overview.WalletBalance = walletTask.IsCompletedSuccessfully ? await walletTask ?? 0 : 0;
        overview.Location = locationTask.IsCompletedSuccessfully ? await locationTask : null;
        overview.CurrentShip = shipTask.IsCompletedSuccessfully ? await shipTask : null;
        overview.OnlineStatus = onlineTask.IsCompletedSuccessfully ? await onlineTask : null;

        _logger.LogDebug("Wallet: {Wallet}, Location: {Loc}, Ship: {Ship}", 
            overview.WalletBalance, overview.Location?.SolarSystemId, overview.CurrentShip?.ShipName);

        // Fetch corporation info
        if (overview.Character.CorporationId > 0)
        {
            _logger.LogDebug("Fetching corporation {CorpId}...", overview.Character.CorporationId);
            overview.Corporation = await GetCorporationAsync(overview.Character.CorporationId) 
                ?? new EveCorporation();
        }

        // Fetch alliance info if applicable
        if (overview.Character.AllianceId.HasValue)
        {
            _logger.LogDebug("Fetching alliance {AllianceId}...", overview.Character.AllianceId);
            overview.Alliance = await GetAllianceAsync(overview.Character.AllianceId.Value);
        }

        // Fetch current system name
        if (overview.Location?.SolarSystemId > 0)
        {
            overview.CurrentSystem = await GetSolarSystemAsync(overview.Location.SolarSystemId);
        }

        // Fetch ship type name
        if (overview.CurrentShip?.ShipTypeId > 0)
        {
            overview.ShipType = await GetTypeAsync(overview.CurrentShip.ShipTypeId);
        }

        _logger.LogInformation("Successfully loaded character overview for {CharacterName}", overview.Character.Name);
        return overview;
    }

    public async Task<EveCharacter?> GetCharacterAsync(int characterId)
    {
        return await GetPublicApiAsync<EveCharacter>($"/characters/{characterId}/");
    }

    public async Task<EveCorporation?> GetCorporationAsync(int corporationId)
    {
        return await GetPublicApiAsync<EveCorporation>($"/corporations/{corporationId}/");
    }

    public async Task<EveAlliance?> GetAllianceAsync(int allianceId)
    {
        return await GetPublicApiAsync<EveAlliance>($"/alliances/{allianceId}/");
    }

    public async Task<double?> GetWalletBalanceAsync(int characterId)
    {
        var test1 = await GetAuthenticatedApiAsync<string>($"/characters/{characterId}/wallet/");
        var test2 = await GetAuthenticatedApiAsync<double>($"/characters/{characterId}/wallet/");

        return await GetAuthenticatedApiAsync<double>($"/characters/{characterId}/wallet/");
    }

    public async Task<CharacterLocation?> GetLocationAsync(int characterId)
    {
        return await GetAuthenticatedApiAsync<CharacterLocation>($"/characters/{characterId}/location/");
    }

    public async Task<CharacterShip?> GetCurrentShipAsync(int characterId)
    {
        return await GetAuthenticatedApiAsync<CharacterShip>($"/characters/{characterId}/ship/");
    }

    public async Task<CharacterOnlineStatus?> GetOnlineStatusAsync(int characterId)
    {
        return await GetAuthenticatedApiAsync<CharacterOnlineStatus>($"/characters/{characterId}/online/");
    }

    public async Task<SolarSystem?> GetSolarSystemAsync(int systemId)
    {
        return await GetPublicApiAsync<SolarSystem>($"/universe/systems/{systemId}/");
    }

    public async Task<EveType?> GetTypeAsync(int typeId)
    {
        return await GetPublicApiAsync<EveType>($"/universe/types/{typeId}/");
    }

    private async Task<T?> GetPublicApiAsync<T>(string endpoint) where T : class
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("User-Agent", "WALLEve/1.0");
            
            var response = await client.GetAsync($"{_settings.EsiBaseUrl}{endpoint}");
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("ESI request failed: {Endpoint} - {Status}", endpoint, response.StatusCode);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<T>(content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling ESI endpoint: {Endpoint}", endpoint);
            return null;
        }
    }

    private async Task<T?> GetAuthenticatedApiAsync<T>(string endpoint)
    {
        try
        {
            var accessToken = await _authService.GetAccessTokenAsync();
            if (string.IsNullOrEmpty(accessToken))
            {
                _logger.LogWarning("No access token available");
                return default;
            }

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("User-Agent", "WALLEve/1.0");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            
            var response = await client.GetAsync($"{_settings.EsiBaseUrl}{endpoint}");
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Authenticated ESI request failed: {Endpoint} - {Status}", endpoint, response.StatusCode);
                return default;
            }

            var content = await response.Content.ReadAsStringAsync();
            
            return JsonSerializer.Deserialize<T>(content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling authenticated ESI endpoint: {Endpoint}", endpoint);
            return default;
        }
    }
}
