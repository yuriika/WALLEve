using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using WALLEve.Configuration;
using WALLEve.Models.Esi;
using WALLEve.Models.Esi.Alliance;
using WALLEve.Models.Esi.Character;
using WALLEve.Models.Esi.Corporation;
using WALLEve.Models.Esi.Markets;
using WALLEve.Models.Esi.Universe;
using WALLEve.Models.Esi.Wallet;
using WALLEve.Services.Authentication.Interfaces;
using WALLEve.Services.Esi.Interfaces;

namespace WALLEve.Services.Esi;

public class EsiApiService : IEsiApiService
{
    private readonly EveOnlineSettings _settings;
    private readonly ApplicationSettings _appSettings;
    private readonly IEveAuthenticationService _authService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<EsiApiService> _logger;

    public EsiApiService(
        IOptions<EveOnlineSettings> settings,
        IOptions<ApplicationSettings> appSettings,
        IEveAuthenticationService authService,
        IHttpClientFactory httpClientFactory,
        ILogger<EsiApiService> logger)
    {
        _settings = settings.Value;
        _appSettings = appSettings.Value;
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
            var client = _httpClientFactory.CreateClient("EveApi");

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

            var client = _httpClientFactory.CreateClient("EveApi");
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

    public async Task<CharacterSkills?> GetCharacterSkillsAsync()
    {
        var authState = await _authService.GetAuthStateAsync();
        if (authState?.CharacterId == null)
        {
            _logger.LogWarning("No authenticated character for skills request");
            return null;
        }

        try
        {
            var endpoint = $"/characters/{authState.CharacterId}/skills/";
            _logger.LogDebug("Fetching skills from: {Endpoint}", endpoint);

            var response = await GetAuthenticatedApiAsync<CharacterSkills>(endpoint);

            if (response != null)
            {
                _logger.LogInformation("Loaded {Count} skills, Total SP: {TotalSp:N0}",
                    response.Skills.Count, response.TotalSp);
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching character skills");
            return null;
        }
    }

    public async Task<List<WalletJournalEntry>?> GetWalletJournalAsync(int characterId, int page = 1)
    {
        try
        {
            var endpoint = $"/characters/{characterId}/wallet/journal/?page={page}";
            _logger.LogDebug("Fetching wallet journal from: {Endpoint}", endpoint);

            var response = await GetAuthenticatedApiAsync<List<WalletJournalEntry>>(endpoint);

            if (response != null)
            {
                _logger.LogInformation("Loaded {Count} wallet journal entries (page {Page})", response.Count, page);
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching wallet journal");
            return null;
        }
    }

    public async Task<List<WalletTransaction>?> GetWalletTransactionsAsync(int characterId)
    {
        try
        {
            var endpoint = $"/characters/{characterId}/wallet/transactions/";
            _logger.LogDebug("Fetching wallet transactions from: {Endpoint}", endpoint);

            var response = await GetAuthenticatedApiAsync<List<WalletTransaction>>(endpoint);

            if (response != null)
            {
                _logger.LogInformation("Loaded {Count} wallet transactions", response.Count);
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching wallet transactions");
            return null;
        }
    }

    public async Task<List<MarketOrder>?> GetMarketOrdersAsync(int characterId)
    {
        try
        {
            var endpoint = $"/characters/{characterId}/orders/";
            _logger.LogDebug("Fetching market orders from: {Endpoint}", endpoint);

            var response = await GetAuthenticatedApiAsync<List<MarketOrder>>(endpoint);

            if (response != null)
            {
                _logger.LogInformation("Loaded {Count} active market orders", response.Count);
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching market orders");
            return null;
        }
    }

    public async Task<List<MarketOrderHistory>?> GetMarketOrderHistoryAsync(int characterId)
    {
        try
        {
            var endpoint = $"/characters/{characterId}/orders/history/";
            _logger.LogDebug("Fetching market order history from: {Endpoint}", endpoint);

            var response = await GetAuthenticatedApiAsync<List<MarketOrderHistory>>(endpoint);

            if (response != null)
            {
                _logger.LogInformation("Loaded {Count} market order history entries", response.Count);
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching market order history");
            return null;
        }
    }

    public async Task<List<WalletJournalEntry>> GetAllWalletJournalPagesAsync(int characterId)
    {
        var allEntries = new List<WalletJournalEntry>();

        try
        {
            _logger.LogInformation("Fetching all wallet journal pages for character {CharacterId}", characterId);

            // Fetch first page and check X-Pages header
            var firstPageResponse = await GetAuthenticatedApiWithHeadersAsync<List<WalletJournalEntry>>(
                $"/characters/{characterId}/wallet/journal/?page=1");

            if (firstPageResponse?.Data == null)
            {
                _logger.LogWarning("Failed to fetch first page of wallet journal");
                return allEntries;
            }

            allEntries.AddRange(firstPageResponse.Data);
            var totalPages = firstPageResponse.TotalPages ?? 1;
            var firstPageLastModified = firstPageResponse.LastModified;

            _logger.LogInformation("Wallet journal has {TotalPages} pages, first page has {Count} entries",
                totalPages, firstPageResponse.Data.Count);

            // Fetch remaining pages if there are any
            if (totalPages > 1)
            {
                var tasks = new List<Task<EsiResponse<List<WalletJournalEntry>>?>>();

                for (int page = 2; page <= totalPages; page++)
                {
                    var pageNum = page;
                    tasks.Add(GetAuthenticatedApiWithHeadersAsync<List<WalletJournalEntry>>(
                        $"/characters/{characterId}/wallet/journal/?page={pageNum}"));
                }

                var results = await Task.WhenAll(tasks);

                foreach (var result in results)
                {
                    if (result?.Data != null)
                    {
                        // Verify Last-Modified header is consistent (ESI cache consistency check)
                        if (firstPageLastModified.HasValue && result.LastModified.HasValue
                            && result.LastModified.Value != firstPageLastModified.Value)
                        {
                            _logger.LogWarning(
                                "Cache inconsistency detected! First page Last-Modified: {First}, Current page: {Current}. " +
                                "Data may be incomplete or inconsistent.",
                                firstPageLastModified.Value, result.LastModified.Value);
                        }

                        allEntries.AddRange(result.Data);
                    }
                }
            }

            _logger.LogInformation("Successfully loaded {TotalCount} wallet journal entries across {Pages} pages",
                allEntries.Count, totalPages);

            return allEntries;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching all wallet journal pages");
            return allEntries; // Return partial data
        }
    }

    public async Task<List<WalletTransaction>> GetAllWalletTransactionsPagesAsync(int characterId)
    {
        var allTransactions = new List<WalletTransaction>();

        try
        {
            _logger.LogInformation("Fetching all wallet transaction pages for character {CharacterId}", characterId);

            // Fetch first page and check X-Pages header
            var firstPageResponse = await GetAuthenticatedApiWithHeadersAsync<List<WalletTransaction>>(
                $"/characters/{characterId}/wallet/transactions/?page=1");

            if (firstPageResponse?.Data == null)
            {
                _logger.LogWarning("Failed to fetch first page of wallet transactions");
                return allTransactions;
            }

            allTransactions.AddRange(firstPageResponse.Data);
            var totalPages = firstPageResponse.TotalPages ?? 1;
            var firstPageLastModified = firstPageResponse.LastModified;

            _logger.LogInformation("Wallet transactions has {TotalPages} pages, first page has {Count} entries",
                totalPages, firstPageResponse.Data.Count);

            // Fetch remaining pages if there are any
            if (totalPages > 1)
            {
                var tasks = new List<Task<EsiResponse<List<WalletTransaction>>?>>();

                for (int page = 2; page <= totalPages; page++)
                {
                    var pageNum = page;
                    tasks.Add(GetAuthenticatedApiWithHeadersAsync<List<WalletTransaction>>(
                        $"/characters/{characterId}/wallet/transactions/?page={pageNum}"));
                }

                var results = await Task.WhenAll(tasks);

                foreach (var result in results)
                {
                    if (result?.Data != null)
                    {
                        // Verify Last-Modified header is consistent (ESI cache consistency check)
                        if (firstPageLastModified.HasValue && result.LastModified.HasValue
                            && result.LastModified.Value != firstPageLastModified.Value)
                        {
                            _logger.LogWarning(
                                "Cache inconsistency detected! First page Last-Modified: {First}, Current page: {Current}. " +
                                "Data may be incomplete or inconsistent.",
                                firstPageLastModified.Value, result.LastModified.Value);
                        }

                        allTransactions.AddRange(result.Data);
                    }
                }
            }

            _logger.LogInformation("Successfully loaded {TotalCount} wallet transactions across {Pages} pages",
                allTransactions.Count, totalPages);

            return allTransactions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching all wallet transaction pages");
            return allTransactions; // Return partial data
        }
    }

    /// <summary>
    /// Erweiterte API-Methode die Response Headers ausliest für Paginierung und Rate Limiting
    /// </summary>
    private async Task<EsiResponse<T>?> GetAuthenticatedApiWithHeadersAsync<T>(string endpoint)
    {
        try
        {
            var accessToken = await _authService.GetAccessTokenAsync();
            if (string.IsNullOrEmpty(accessToken))
            {
                _logger.LogWarning("No access token available");
                return null;
            }

            var client = _httpClientFactory.CreateClient("EveApi");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await client.GetAsync($"{_settings.EsiBaseUrl}{endpoint}");

            var esiResponse = new EsiResponse<T>
            {
                StatusCode = (int)response.StatusCode
            };

            // Parse Response Headers
            esiResponse.RateLimit = ParseRateLimitHeaders(response.Headers);

            // Log Rate Limit Warnings
            if (esiResponse.RateLimit != null)
            {
                if (esiResponse.RateLimit.IsLowOnTokens())
                {
                    _logger.LogWarning("Rate limit tokens running low! Remaining: {Remaining}, Limit: {Limit}",
                        esiResponse.RateLimit.Remaining, esiResponse.RateLimit.Limit);
                }

                if (esiResponse.RateLimit.IsLowOnErrorBudget())
                {
                    _logger.LogWarning("Error budget running low! Remaining errors: {Remaining}, Reset in: {Reset}s",
                        esiResponse.RateLimit.ErrorLimitRemain, esiResponse.RateLimit.ErrorLimitReset);
                }
            }

            // Parse X-Pages header for pagination
            if (response.Headers.TryGetValues("X-Pages", out var xPagesValues))
            {
                var xPagesStr = xPagesValues.FirstOrDefault();
                if (int.TryParse(xPagesStr, out var totalPages))
                {
                    esiResponse.TotalPages = totalPages;
                }
            }

            // Parse ETag for caching
            if (response.Headers.ETag != null)
            {
                esiResponse.ETag = response.Headers.ETag.Tag;
            }

            // Parse Last-Modified
            if (response.Content.Headers.LastModified.HasValue)
            {
                esiResponse.LastModified = response.Content.Headers.LastModified.Value.UtcDateTime;
            }

            // Parse Expires
            if (response.Content.Headers.Expires.HasValue)
            {
                esiResponse.Expires = response.Content.Headers.Expires.Value.UtcDateTime;
            }

            // Handle different status codes
            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests) // 429
            {
                _logger.LogWarning("Rate limited! Retry after {RetryAfter}s", esiResponse.RateLimit?.RetryAfter);
                return esiResponse;
            }

            if ((int)response.StatusCode == 420) // Error Limited
            {
                _logger.LogError("ERROR LIMITED (420)! Too many errors, ESI has blocked requests. Wait for reset.");
                return esiResponse;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Authenticated ESI request failed: {Endpoint} - {Status}",
                    endpoint, response.StatusCode);
                return esiResponse;
            }

            // Parse response content
            var content = await response.Content.ReadAsStringAsync();

            if (!string.IsNullOrWhiteSpace(content))
            {
                try
                {
                    esiResponse.Data = JsonSerializer.Deserialize<T>(content);
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "Failed to deserialize JSON from {Endpoint}", endpoint);
                }
            }

            return esiResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling authenticated ESI endpoint: {Endpoint}", endpoint);
            return null;
        }
    }

    /// <summary>
    /// Parst Rate Limiting Headers aus ESI Response
    /// </summary>
    private RateLimitInfo ParseRateLimitHeaders(System.Net.Http.Headers.HttpResponseHeaders headers)
    {
        var rateLimitInfo = new RateLimitInfo();

        if (headers.TryGetValues("X-Ratelimit-Group", out var groupValues))
            rateLimitInfo.Group = groupValues.FirstOrDefault();

        if (headers.TryGetValues("X-Ratelimit-Limit", out var limitValues))
            rateLimitInfo.Limit = limitValues.FirstOrDefault();

        if (headers.TryGetValues("X-Ratelimit-Remaining", out var remainingValues))
        {
            var remainingStr = remainingValues.FirstOrDefault();
            if (int.TryParse(remainingStr, out var remaining))
                rateLimitInfo.Remaining = remaining;
        }

        if (headers.TryGetValues("X-Ratelimit-Used", out var usedValues))
        {
            var usedStr = usedValues.FirstOrDefault();
            if (int.TryParse(usedStr, out var used))
                rateLimitInfo.Used = used;
        }

        if (headers.TryGetValues("Retry-After", out var retryAfterValues))
        {
            var retryAfterStr = retryAfterValues.FirstOrDefault();
            if (int.TryParse(retryAfterStr, out var retryAfter))
                rateLimitInfo.RetryAfter = retryAfter;
        }

        if (headers.TryGetValues("X-ESI-Error-Limit-Remain", out var errorRemainValues))
        {
            var errorRemainStr = errorRemainValues.FirstOrDefault();
            if (int.TryParse(errorRemainStr, out var errorRemain))
                rateLimitInfo.ErrorLimitRemain = errorRemain;
        }

        if (headers.TryGetValues("X-ESI-Error-Limit-Reset", out var errorResetValues))
        {
            var errorResetStr = errorResetValues.FirstOrDefault();
            if (int.TryParse(errorResetStr, out var errorReset))
                rateLimitInfo.ErrorLimitReset = errorReset;
        }

        return rateLimitInfo;
    }
}
