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
    private readonly IEsiCacheService _cacheService;
    private readonly ILogger<EsiApiService> _logger;

    public EsiApiService(
        IOptions<EveOnlineSettings> settings,
        IOptions<ApplicationSettings> appSettings,
        IEveAuthenticationService authService,
        IHttpClientFactory httpClientFactory,
        IEsiCacheService cacheService,
        ILogger<EsiApiService> logger)
    {
        _settings = settings.Value;
        _appSettings = appSettings.Value;
        _authService = authService;
        _httpClientFactory = httpClientFactory;
        _cacheService = cacheService;
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
            // 1. Check cache first
            var cachedEntry = _cacheService.Get<T>(endpoint);

            var client = _httpClientFactory.CreateClient("EveApi");

            // 2. Add If-None-Match if cached
            if (cachedEntry != null && !string.IsNullOrEmpty(cachedEntry.ETag))
            {
                client.DefaultRequestHeaders.IfNoneMatch.Add(
                    new EntityTagHeaderValue(cachedEntry.ETag));
            }

            var response = await client.GetAsync($"{_settings.EsiBaseUrl}{endpoint}");

            // 3. Handle 304 Not Modified
            if (response.StatusCode == System.Net.HttpStatusCode.NotModified)
            {
                _logger.LogInformation("ESI returned 304 Not Modified for {Endpoint} - using cached data", endpoint);
                return cachedEntry!.Data;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("ESI request failed: {Endpoint} - {Status}", endpoint, response.StatusCode);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            var data = JsonSerializer.Deserialize<T>(content);

            // 4. Cache response with ETag
            if (data != null && response.Headers.ETag != null)
            {
                var etag = response.Headers.ETag.Tag;
                var expires = response.Content.Headers.Expires?.UtcDateTime;
                _cacheService.Set(endpoint, etag, data, expires);
                _logger.LogDebug("Cached {Endpoint} with ETag {ETag}, Expires: {Expires}",
                    endpoint, etag, expires);
            }

            return data;
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

            // Check cache first
            var cachedEntry = _cacheService.Get<T>(endpoint);

            var client = _httpClientFactory.CreateClient("EveApi");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            // Add If-None-Match header if we have cached data
            if (cachedEntry != null && !string.IsNullOrEmpty(cachedEntry.ETag))
            {
                client.DefaultRequestHeaders.IfNoneMatch.Add(new EntityTagHeaderValue(cachedEntry.ETag));
            }

            var response = await client.GetAsync($"{_settings.EsiBaseUrl}{endpoint}");

            // 304 Not Modified - return cached data
            if (response.StatusCode == System.Net.HttpStatusCode.NotModified)
            {
                _logger.LogInformation("ESI returned 304 Not Modified for {Endpoint} - using cached data", endpoint);
                return cachedEntry!.Data;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Authenticated ESI request failed: {Endpoint} - {Status}", endpoint, response.StatusCode);
                return default;
            }

            var content = await response.Content.ReadAsStringAsync();

            // Validate response is not empty
            if (string.IsNullOrWhiteSpace(content))
            {
                _logger.LogWarning("Received empty response from {Endpoint}", endpoint);
                return default;
            }

            // Validate JSON and deserialize
            T? data = default;
            try
            {
                data = JsonSerializer.Deserialize<T>(content);

                // Validate deserialized data
                if (data == null)
                {
                    _logger.LogWarning("Deserialization resulted in null data for {Endpoint}", endpoint);
                    return default;
                }
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to deserialize JSON from {Endpoint}. Response: {Content}",
                    endpoint, content.Length > 500 ? content.Substring(0, 500) + "..." : content);
                return default;
            }

            // Cache the response if we got an ETag
            if (response.Headers.ETag != null)
            {
                var etag = response.Headers.ETag.Tag;
                var expires = response.Content.Headers.Expires?.UtcDateTime;
                _cacheService.Set(endpoint, etag, data, expires);
            }

            return data;
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

            // 1. Check cache first
            var cachedEntry = _cacheService.Get<T>(endpoint);

            var client = _httpClientFactory.CreateClient("EveApi");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            // 2. Add If-None-Match if cached
            if (cachedEntry != null && !string.IsNullOrEmpty(cachedEntry.ETag))
            {
                client.DefaultRequestHeaders.IfNoneMatch.Add(
                    new EntityTagHeaderValue(cachedEntry.ETag));
            }

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

            // Handle different status codes with detailed logging
            switch (response.StatusCode)
            {
                case System.Net.HttpStatusCode.OK: // 200
                    // Success - continue to parse response
                    break;

                case System.Net.HttpStatusCode.NotModified: // 304
                    _logger.LogInformation("ESI returned 304 Not Modified for {Endpoint} - using cached data", endpoint);
                    // Return cached data in response
                    if (cachedEntry != null)
                    {
                        esiResponse.Data = cachedEntry.Data;
                        esiResponse.ETag = cachedEntry.ETag;
                        esiResponse.Expires = cachedEntry.Expires;
                    }
                    return esiResponse;

                case System.Net.HttpStatusCode.BadRequest: // 400
                    _logger.LogWarning("Bad Request (400) for {Endpoint} - Invalid parameters or malformed request", endpoint);
                    return esiResponse;

                case System.Net.HttpStatusCode.Unauthorized: // 401
                    _logger.LogError("Unauthorized (401) for {Endpoint} - Invalid or expired access token", endpoint);
                    return esiResponse;

                case System.Net.HttpStatusCode.Forbidden: // 403
                    _logger.LogError("Forbidden (403) for {Endpoint} - Missing required scope or character not authorized", endpoint);
                    return esiResponse;

                case System.Net.HttpStatusCode.NotFound: // 404
                    _logger.LogWarning("Not Found (404) for {Endpoint} - Resource does not exist", endpoint);
                    return esiResponse;

                case System.Net.HttpStatusCode.TooManyRequests: // 429
                    _logger.LogWarning("Rate Limited (429) for {Endpoint} - Retry after {RetryAfter}s, Remaining: {Remaining}/{Limit}",
                        endpoint, esiResponse.RateLimit?.RetryAfter, esiResponse.RateLimit?.Remaining, esiResponse.RateLimit?.Limit);
                    return esiResponse;

                case System.Net.HttpStatusCode.InternalServerError: // 500
                    _logger.LogError("Internal Server Error (500) for {Endpoint} - ESI is experiencing issues", endpoint);
                    return esiResponse;

                case System.Net.HttpStatusCode.BadGateway: // 502
                    _logger.LogError("Bad Gateway (502) for {Endpoint} - ESI proxy error", endpoint);
                    return esiResponse;

                case System.Net.HttpStatusCode.ServiceUnavailable: // 503
                    _logger.LogError("Service Unavailable (503) for {Endpoint} - ESI is down or under maintenance", endpoint);
                    return esiResponse;

                case System.Net.HttpStatusCode.GatewayTimeout: // 504
                    _logger.LogError("Gateway Timeout (504) for {Endpoint} - ESI request timed out", endpoint);
                    return esiResponse;

                default:
                    // 420 Error Limited (custom code)
                    if ((int)response.StatusCode == 420)
                    {
                        _logger.LogError("ERROR LIMITED (420) for {Endpoint} - Too many errors ({ErrorsRemaining}/{ErrorsLimit}), requests blocked until reset in {ResetSeconds}s",
                            endpoint, esiResponse.RateLimit?.ErrorLimitRemain, esiResponse.RateLimit?.ErrorLimitRemain,
                            esiResponse.RateLimit?.ErrorLimitReset);
                        return esiResponse;
                    }

                    _logger.LogWarning("Unexpected HTTP status {StatusCode} for {Endpoint}", (int)response.StatusCode, endpoint);
                    return esiResponse;
            }

            // Parse response content with validation
            var content = await response.Content.ReadAsStringAsync();

            // Validate response is not empty
            if (string.IsNullOrWhiteSpace(content))
            {
                _logger.LogWarning("Received empty response from {Endpoint}", endpoint);
                return esiResponse;
            }

            // Validate JSON and deserialize
            try
            {
                esiResponse.Data = JsonSerializer.Deserialize<T>(content);

                // Validate deserialized data is not null
                if (esiResponse.Data == null)
                {
                    _logger.LogWarning("Deserialization resulted in null data for {Endpoint}", endpoint);
                }
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to deserialize JSON from {Endpoint}. Response: {Content}",
                    endpoint, content.Length > 500 ? content.Substring(0, 500) + "..." : content);
            }

            // 3. Cache successful response with ETag
            if (esiResponse.Data != null && !string.IsNullOrEmpty(esiResponse.ETag))
            {
                _cacheService.Set(endpoint, esiResponse.ETag, esiResponse.Data, esiResponse.Expires);
                _logger.LogDebug("Cached {Endpoint} with ETag {ETag}, Expires: {Expires}",
                    endpoint, esiResponse.ETag, esiResponse.Expires);
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

    // Corporation Wallet Endpoints

    /// <summary>
    /// Holt Corporation Wallet Journal für eine bestimmte Division
    /// GET /corporations/{corporation_id}/wallets/{division}/journal/
    /// Scope: esi-wallet.read_corporation_wallets.v1
    /// </summary>
    public async Task<List<WalletJournalEntry>?> GetCorporationWalletJournalAsync(int corporationId, int division, int page = 1)
    {
        try
        {
            var endpoint = $"/corporations/{corporationId}/wallets/{division}/journal/?page={page}";
            _logger.LogDebug("Fetching corporation wallet journal from: {Endpoint}", endpoint);

            var response = await GetAuthenticatedApiAsync<List<WalletJournalEntry>>(endpoint);

            if (response != null)
            {
                _logger.LogInformation("Loaded {Count} corporation wallet journal entries (Division {Division}, Page {Page})",
                    response.Count, division, page);
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get corporation wallet journal for corporation {CorporationId}, division {Division}",
                corporationId, division);
            return null;
        }
    }

    /// <summary>
    /// Holt Corporation Wallet Transactions für eine bestimmte Division
    /// GET /corporations/{corporation_id}/wallets/{division}/transactions/
    /// Scope: esi-wallet.read_corporation_wallets.v1
    /// </summary>
    public async Task<List<WalletTransaction>?> GetCorporationWalletTransactionsAsync(int corporationId, int division)
    {
        try
        {
            var endpoint = $"/corporations/{corporationId}/wallets/{division}/transactions/";
            _logger.LogDebug("Fetching corporation wallet transactions from: {Endpoint}", endpoint);

            var response = await GetAuthenticatedApiAsync<List<WalletTransaction>>(endpoint);

            if (response != null)
            {
                _logger.LogInformation("Loaded {Count} corporation wallet transactions (Division {Division})",
                    response.Count, division);
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get corporation wallet transactions for corporation {CorporationId}, division {Division}",
                corporationId, division);
            return null;
        }
    }
}
