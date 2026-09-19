using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using WALLEve.Configuration;
using WALLEve.Exceptions;
using WALLEve.Models.Esi;
using WALLEve.Models.Esi.Alliance;
using WALLEve.Models.Esi.Character;
using WALLEve.Models.Esi.Corporation;
using WALLEve.Models.Esi.Markets;
using WALLEve.Models.Esi.Universe;
using WALLEve.Models.Esi.Wallet;
using WALLEve.Models.Measurement;
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

    public async Task<StructureLookupResult> GetStructureAsync(long structureId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var structure = await GetAuthenticatedApiAsync<EsiStructure>($"/universe/structures/{structureId}/", ct);
            if (structure == null)
            {
                return new StructureLookupResult { Error = "unavailable" };
            }
            structure.StructureId = structureId;
            return new StructureLookupResult { Structure = structure };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Caller-Cancellation durchreichen — nie als "unavailable" verbuchen.
            throw;
        }
        catch (EsiAuthException ex) when (ex.IsForbidden)
        {
            _logger.LogWarning("Struktur {StructureId} nicht zugänglich (403)", structureId);
            return new StructureLookupResult { Error = "403" };
        }
        catch (EsiAuthException)
        {
            _logger.LogWarning("Struktur {StructureId} nicht auflösbar (nicht authentifiziert)", structureId);
            return new StructureLookupResult { Error = "unauthenticated" };
        }
        catch (EsiNotFoundException)
        {
            _logger.LogWarning("Struktur {StructureId} nicht gefunden (404)", structureId);
            return new StructureLookupResult { Error = "not-found" };
        }
        catch (EsiRateLimitException)
        {
            _logger.LogWarning("Struktur {StructureId} rate-limited (429)", structureId);
            return new StructureLookupResult { Error = "rate-limit" };
        }
        catch (EsiErrorLimitException)
        {
            _logger.LogWarning("Struktur {StructureId} error-limited (420)", structureId);
            return new StructureLookupResult { Error = "rate-limit" };
        }
        catch (EsiServerException)
        {
            _logger.LogError("Struktur {StructureId} nicht auflösbar (ESI-Serverfehler)", structureId);
            return new StructureLookupResult { Error = "server-error" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Struktur {StructureId} nicht auflösbar", structureId);
            return new StructureLookupResult { Error = "unavailable" };
        }
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

            // Parse rate limit headers (if available)
            var rateLimit = ParseRateLimitHeaders(response.Headers);

            // 3. Handle 304 Not Modified
            if (response.StatusCode == System.Net.HttpStatusCode.NotModified)
            {
                _logger.LogInformation("ESI returned 304 Not Modified for {Endpoint} - using cached data", endpoint);
                return cachedEntry!.Data;
            }

            // Handle error status codes with proper exceptions
            if (!response.IsSuccessStatusCode)
            {
                switch (response.StatusCode)
                {
                    case System.Net.HttpStatusCode.BadRequest: // 400
                        _logger.LogWarning("Bad Request (400) for {Endpoint}", endpoint);
                        var badRequestContent = await response.Content.ReadAsStringAsync();
                        throw new EsiBadRequestException(endpoint, badRequestContent, rateLimit);

                    case System.Net.HttpStatusCode.Unauthorized: // 401
                        _logger.LogError("Unauthorized (401) for {Endpoint}", endpoint);
                        throw new EsiAuthException(endpoint, System.Net.HttpStatusCode.Unauthorized, rateLimit);

                    case System.Net.HttpStatusCode.Forbidden: // 403
                        _logger.LogError("Forbidden (403) for {Endpoint}", endpoint);
                        throw new EsiAuthException(endpoint, System.Net.HttpStatusCode.Forbidden, rateLimit);

                    case System.Net.HttpStatusCode.NotFound: // 404
                        _logger.LogWarning("Not Found (404) for {Endpoint}", endpoint);
                        throw new EsiNotFoundException(endpoint, rateLimit);

                    case System.Net.HttpStatusCode.TooManyRequests: // 429
                        _logger.LogWarning("Rate Limited (429) for {Endpoint}", endpoint);
                        throw new EsiRateLimitException(endpoint, rateLimit, rateLimit?.RetryAfter);

                    case System.Net.HttpStatusCode.InternalServerError: // 500
                        _logger.LogError("Internal Server Error (500) for {Endpoint}", endpoint);
                        throw new EsiServerException(endpoint, System.Net.HttpStatusCode.InternalServerError, rateLimit);

                    case System.Net.HttpStatusCode.BadGateway: // 502
                        _logger.LogError("Bad Gateway (502) for {Endpoint}", endpoint);
                        throw new EsiServerException(endpoint, System.Net.HttpStatusCode.BadGateway, rateLimit);

                    case System.Net.HttpStatusCode.ServiceUnavailable: // 503
                        _logger.LogError("Service Unavailable (503) for {Endpoint}", endpoint);
                        throw new EsiServerException(endpoint, System.Net.HttpStatusCode.ServiceUnavailable, rateLimit);

                    case System.Net.HttpStatusCode.GatewayTimeout: // 504
                        _logger.LogError("Gateway Timeout (504) for {Endpoint}", endpoint);
                        throw new EsiServerException(endpoint, System.Net.HttpStatusCode.GatewayTimeout, rateLimit);

                    default:
                        // 420 Error Limited (custom code)
                        if ((int)response.StatusCode == 420)
                        {
                            _logger.LogError("ERROR LIMITED (420) for {Endpoint}", endpoint);
                            throw new EsiErrorLimitException(endpoint, rateLimit);
                        }

                        _logger.LogWarning("Unexpected HTTP status {StatusCode} for {Endpoint}",
                            (int)response.StatusCode, endpoint);
                        throw new EsiApiException(
                            $"Unexpected HTTP status {(int)response.StatusCode}",
                            endpoint,
                            response.StatusCode,
                            null,
                            rateLimit);
                }
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
        catch (EsiApiException)
        {
            // Re-throw ESI-specific exceptions
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling ESI endpoint: {Endpoint}", endpoint);
            throw new EsiApiException(
                $"Unexpected error calling ESI endpoint: {ex.Message}",
                ex,
                endpoint,
                null,
                null);
        }
    }

    private async Task<T?> GetAuthenticatedApiAsync<T>(string endpoint, CancellationToken ct = default)
    {
        try
        {
            var accessToken = await _authService.GetAccessTokenAsync();
            if (string.IsNullOrEmpty(accessToken))
            {
                _logger.LogWarning("No access token available");
                throw new EsiAuthException(endpoint, System.Net.HttpStatusCode.Unauthorized, null);
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

            var response = await client.GetAsync($"{_settings.EsiBaseUrl}{endpoint}", ct);

            // Parse rate limit headers (if available)
            var rateLimit = ParseRateLimitHeaders(response.Headers);

            // 304 Not Modified - return cached data
            if (response.StatusCode == System.Net.HttpStatusCode.NotModified)
            {
                _logger.LogInformation("ESI returned 304 Not Modified for {Endpoint} - using cached data", endpoint);
                return cachedEntry!.Data;
            }

            // Handle error status codes with proper exceptions
            if (!response.IsSuccessStatusCode)
            {
                switch (response.StatusCode)
                {
                    case System.Net.HttpStatusCode.BadRequest: // 400
                        _logger.LogWarning("Bad Request (400) for {Endpoint}", endpoint);
                        var badRequestContent = await response.Content.ReadAsStringAsync();
                        throw new EsiBadRequestException(endpoint, badRequestContent, rateLimit);

                    case System.Net.HttpStatusCode.Unauthorized: // 401
                        _logger.LogError("Unauthorized (401) for {Endpoint}", endpoint);
                        throw new EsiAuthException(endpoint, System.Net.HttpStatusCode.Unauthorized, rateLimit);

                    case System.Net.HttpStatusCode.Forbidden: // 403
                        _logger.LogError("Forbidden (403) for {Endpoint}", endpoint);
                        throw new EsiAuthException(endpoint, System.Net.HttpStatusCode.Forbidden, rateLimit);

                    case System.Net.HttpStatusCode.NotFound: // 404
                        _logger.LogWarning("Not Found (404) for {Endpoint}", endpoint);
                        throw new EsiNotFoundException(endpoint, rateLimit);

                    case System.Net.HttpStatusCode.TooManyRequests: // 429
                        _logger.LogWarning("Rate Limited (429) for {Endpoint}", endpoint);
                        throw new EsiRateLimitException(endpoint, rateLimit, rateLimit?.RetryAfter);

                    case System.Net.HttpStatusCode.InternalServerError: // 500
                        _logger.LogError("Internal Server Error (500) for {Endpoint}", endpoint);
                        throw new EsiServerException(endpoint, System.Net.HttpStatusCode.InternalServerError, rateLimit);

                    case System.Net.HttpStatusCode.BadGateway: // 502
                        _logger.LogError("Bad Gateway (502) for {Endpoint}", endpoint);
                        throw new EsiServerException(endpoint, System.Net.HttpStatusCode.BadGateway, rateLimit);

                    case System.Net.HttpStatusCode.ServiceUnavailable: // 503
                        _logger.LogError("Service Unavailable (503) for {Endpoint}", endpoint);
                        throw new EsiServerException(endpoint, System.Net.HttpStatusCode.ServiceUnavailable, rateLimit);

                    case System.Net.HttpStatusCode.GatewayTimeout: // 504
                        _logger.LogError("Gateway Timeout (504) for {Endpoint}", endpoint);
                        throw new EsiServerException(endpoint, System.Net.HttpStatusCode.GatewayTimeout, rateLimit);

                    default:
                        // 420 Error Limited (custom code)
                        if ((int)response.StatusCode == 420)
                        {
                            _logger.LogError("ERROR LIMITED (420) for {Endpoint}", endpoint);
                            throw new EsiErrorLimitException(endpoint, rateLimit);
                        }

                        _logger.LogWarning("Unexpected HTTP status {StatusCode} for {Endpoint}",
                            (int)response.StatusCode, endpoint);
                        throw new EsiApiException(
                            $"Unexpected HTTP status {(int)response.StatusCode}",
                            endpoint,
                            response.StatusCode,
                            null,
                            rateLimit);
                }
            }

            var content = await response.Content.ReadAsStringAsync();

            // Validate response is not empty
            if (string.IsNullOrWhiteSpace(content))
            {
                _logger.LogWarning("Received empty response from {Endpoint}", endpoint);
                throw new EsiApiException(
                    "Received empty response from ESI",
                    endpoint,
                    response.StatusCode,
                    null,
                    rateLimit);
            }

            // Validate JSON and deserialize
            T? data;
            try
            {
                data = JsonSerializer.Deserialize<T>(content);

                // Validate deserialized data
                if (data == null)
                {
                    _logger.LogWarning("Deserialization resulted in null data for {Endpoint}", endpoint);
                    throw new EsiApiException(
                        "Deserialization resulted in null data",
                        endpoint,
                        response.StatusCode,
                        content.Length > 500 ? content.Substring(0, 500) + "..." : content,
                        rateLimit);
                }
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to deserialize JSON from {Endpoint}. Response: {Content}",
                    endpoint, content.Length > 500 ? content.Substring(0, 500) + "..." : content);
                throw new EsiApiException(
                    $"Failed to deserialize JSON: {ex.Message}",
                    ex,
                    endpoint,
                    response.StatusCode,
                    rateLimit);
            }

            // Cache the response if we got an ETag
            if (response.Headers.ETag != null)
            {
                var etag = response.Headers.ETag.Tag;
                var expires = response.Content.Headers.Expires?.UtcDateTime;
                _cacheService.Set(endpoint, etag, data, expires);
                _logger.LogDebug("Cached {Endpoint} with ETag {ETag}, Expires: {Expires}",
                    endpoint, etag, expires);
            }

            return data;
        }
        catch (EsiApiException)
        {
            // Re-throw ESI-specific exceptions
            throw;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Caller-Cancellation nie in einen ESI-Fehler umwandeln — durchreichen.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling authenticated ESI endpoint: {Endpoint}", endpoint);
            throw new EsiApiException(
                $"Unexpected error calling ESI endpoint: {ex.Message}",
                ex,
                endpoint,
                null,
                null);
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

    public async Task<List<CharacterAsset>?> GetCharacterAssetsAsync(int characterId, CancellationToken ct = default)
    {
        _logger.LogInformation("Loading assets for character ID: {CharacterId}", characterId);
        try
        {
            var authState = await _authService.GetAuthStateAsync();
            if (authState == null || !authState.IsValid)
            {
                _logger.LogWarning("Cannot load assets - not authenticated");
                return null;
            }

            var client = _httpClientFactory.CreateClient("EveApi");
            var token = await _authService.GetAccessTokenAsync();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);

            var allAssets = new List<CharacterAsset>();
            var currentPage = 1;
            var totalPages = 1;

            while (currentPage <= totalPages)
            {
                // M0-Vertrag: Abbruch liefert keine Teildaten.
                ct.ThrowIfCancellationRequested();

                var url = $"{_settings.EsiBaseUrl}/characters/{characterId}/assets/?page={currentPage}";
                var request = new HttpRequestMessage(HttpMethod.Get, url);

                var response = await client.SendAsync(request, ct);
                response.EnsureSuccessStatusCode();

                if (response.Headers.TryGetValues("X-Pages", out var pages))
                {
                    totalPages = int.Parse(pages.First());
                }

                var content = await response.Content.ReadAsStringAsync(ct);
                var pageAssets = JsonSerializer.Deserialize<List<CharacterAsset>>(content);
                if (pageAssets != null)
                {
                    // Owner verlustfrei an jede Rohzeile heften (Issue #27):
                    // Assets gehören immer dem abgefragten Charakter.
                    foreach (var asset in pageAssets)
                    {
                        asset.OwnerCharacterId = characterId;
                    }
                    allAssets.AddRange(pageAssets);
                }

                currentPage++;
            }

            _logger.LogInformation("Loaded {Count} assets for character {CharacterId}",
                allAssets.Count, characterId);
            return allAssets;
        }
        catch (OperationCanceledException)
        {
            // Abbruch: keine Teildaten - Aufrufer erhaelt die Exception.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading assets for character {CharacterId}", characterId);
            return null;
        }
    }

    public async Task<List<CharacterMiningEntry>?> GetCharacterMiningLedgerAsync(int characterId, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Loading mining ledger for character ID: {CharacterId}", characterId);

            var firstPage = await GetAuthenticatedApiWithHeadersAsync<List<CharacterMiningEntry>>(
                $"/characters/{characterId}/mining/?page=1", ct);

            var allEntries = await CollectAllPagesAtomicallyAsync(
                firstPage,
                page => GetAuthenticatedApiWithHeadersAsync<List<CharacterMiningEntry>>(
                    $"/characters/{characterId}/mining/?page={page}", ct),
                $"mining ledger for character {characterId}",
                ct);

            if (allEntries != null)
            {
                _logger.LogInformation("Loaded {Count} mining ledger entries for character {CharacterId}",
                    allEntries.Count, characterId);
            }

            return allEntries; // null = Fehler/Cancellation → Aufrufer behält alten Snapshot
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Fetching mining ledger cancelled for character {CharacterId}", characterId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading mining ledger for character {CharacterId}", characterId);
            return null;
        }
    }

    public async Task<List<CharacterIndustryJob>?> GetCharacterIndustryJobsAsync(int characterId, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Loading industry jobs for character ID: {CharacterId}", characterId);

            var firstPage = await GetAuthenticatedApiWithHeadersAsync<List<CharacterIndustryJob>>(
                $"/characters/{characterId}/industry/jobs/?page=1", ct);

            var allEntries = await CollectAllPagesAtomicallyAsync(
                firstPage,
                page => GetAuthenticatedApiWithHeadersAsync<List<CharacterIndustryJob>>(
                    $"/characters/{characterId}/industry/jobs/?page={page}", ct),
                $"industry jobs for character {characterId}",
                ct);

            if (allEntries != null)
            {
                _logger.LogInformation("Loaded {Count} industry jobs for character {CharacterId}",
                    allEntries.Count, characterId);
            }

            return allEntries; // null = Fehler/Cancellation → Aufrufer behält alten Snapshot
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Fetching industry jobs cancelled for character {CharacterId}", characterId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading industry jobs for character {CharacterId}", characterId);
            return null;
        }
    }

    public async Task<List<CharacterBlueprint>?> GetCharacterBlueprintsAsync(int characterId, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Loading blueprints for character ID: {CharacterId}", characterId);

            var firstPage = await GetAuthenticatedApiWithHeadersAsync<List<CharacterBlueprint>>(
                $"/characters/{characterId}/blueprints/?page=1", ct);

            var allEntries = await CollectAllPagesAtomicallyAsync(
                firstPage,
                page => GetAuthenticatedApiWithHeadersAsync<List<CharacterBlueprint>>(
                    $"/characters/{characterId}/blueprints/?page={page}", ct),
                $"blueprints for character {characterId}",
                ct);

            if (allEntries != null)
            {
                _logger.LogInformation("Loaded {Count} blueprints for character {CharacterId}",
                    allEntries.Count, characterId);
            }

            return allEntries; // null = Fehler/Cancellation → Aufrufer behält alten Snapshot
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Fetching blueprints cancelled for character {CharacterId}", characterId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading blueprints for character {CharacterId}", characterId);
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

    public async Task<List<WalletJournalEntry>?> GetAllWalletJournalPagesAsync(int characterId, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Fetching all wallet journal pages for character {CharacterId}", characterId);

            var firstPage = await GetAuthenticatedApiWithHeadersAsync<List<WalletJournalEntry>>(
                $"/characters/{characterId}/wallet/journal/?page=1", ct);

            var allEntries = await CollectAllPagesAtomicallyAsync(
                firstPage,
                page => GetAuthenticatedApiWithHeadersAsync<List<WalletJournalEntry>>(
                    $"/characters/{characterId}/wallet/journal/?page={page}", ct),
                $"wallet journal for character {characterId}",
                ct);

            if (allEntries != null)
            {
                _logger.LogInformation("Successfully loaded {TotalCount} wallet journal entries across {TotalPages} pages",
                    allEntries.Count, firstPage?.TotalPages ?? 1);
            }

            return allEntries; // null = Fehler/Cancellation → Aufrufer behält alten Snapshot
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Fetching all wallet journal pages cancelled for character {CharacterId}", characterId);
            return null; // Cancellation publiziert keine Teilmenge
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching all wallet journal pages");
            return null; // kein Teildaten-Leak
        }
    }

    public async Task<List<WalletTransaction>?> GetAllWalletTransactionsPagesAsync(int characterId, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Fetching all wallet transaction pages for character {CharacterId}", characterId);

            var firstPage = await GetAuthenticatedApiWithHeadersAsync<List<WalletTransaction>>(
                $"/characters/{characterId}/wallet/transactions/?page=1", ct);

            var allTransactions = await CollectAllPagesAtomicallyAsync(
                firstPage,
                page => GetAuthenticatedApiWithHeadersAsync<List<WalletTransaction>>(
                    $"/characters/{characterId}/wallet/transactions/?page={page}", ct),
                $"wallet transactions for character {characterId}",
                ct);

            if (allTransactions != null)
            {
                _logger.LogInformation("Successfully loaded {TotalCount} wallet transactions across {TotalPages} pages",
                    allTransactions.Count, firstPage?.TotalPages ?? 1);
            }

            return allTransactions; // null = Fehler/Cancellation → Aufrufer behält alten Snapshot
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Fetching all wallet transactions pages cancelled for character {CharacterId}", characterId);
            return null; // Cancellation publiziert keine Teilmenge
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching all wallet transaction pages");
            return null; // kein Teildaten-Leak
        }
    }

    /// <summary>
    /// Erweiterte API-Methode die Response Headers ausliest für Paginierung und Rate Limiting
    /// </summary>
    private async Task<EsiResponse<T>?> GetAuthenticatedApiWithHeadersAsync<T>(string endpoint, CancellationToken ct)
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

            var response = await client.GetAsync($"{_settings.EsiBaseUrl}{endpoint}", ct);

            var esiResponse = new EsiResponse<T>
            {
                StatusCode = (int)response.StatusCode,
                FetchedAt = DateTime.UtcNow
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

            // Handle different status codes with detailed logging and exception throwing for critical errors
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
                        // Ursprünglicher Datenzeitpunkt des Snapshots bleibt erhalten
                        esiResponse.FetchedAt = cachedEntry.CachedAt;
                    }
                    return esiResponse;

                case System.Net.HttpStatusCode.BadRequest: // 400
                    _logger.LogWarning("Bad Request (400) for {Endpoint} - Invalid parameters or malformed request", endpoint);
                    var badRequestContent = await response.Content.ReadAsStringAsync();
                    throw new EsiBadRequestException(endpoint, badRequestContent, esiResponse.RateLimit);

                case System.Net.HttpStatusCode.Unauthorized: // 401
                    _logger.LogError("Unauthorized (401) for {Endpoint} - Invalid or expired access token", endpoint);
                    throw new EsiAuthException(endpoint, System.Net.HttpStatusCode.Unauthorized, esiResponse.RateLimit);

                case System.Net.HttpStatusCode.Forbidden: // 403
                    _logger.LogError("Forbidden (403) for {Endpoint} - Missing required scope or character not authorized", endpoint);
                    throw new EsiAuthException(endpoint, System.Net.HttpStatusCode.Forbidden, esiResponse.RateLimit);

                case System.Net.HttpStatusCode.NotFound: // 404
                    _logger.LogWarning("Not Found (404) for {Endpoint} - Resource does not exist", endpoint);
                    throw new EsiNotFoundException(endpoint, esiResponse.RateLimit);

                case System.Net.HttpStatusCode.TooManyRequests: // 429
                    _logger.LogWarning("Rate Limited (429) for {Endpoint} - Retry after {RetryAfter}s, Remaining: {Remaining}/{Limit}",
                        endpoint, esiResponse.RateLimit?.RetryAfter, esiResponse.RateLimit?.Remaining, esiResponse.RateLimit?.Limit);
                    throw new EsiRateLimitException(endpoint, esiResponse.RateLimit, esiResponse.RateLimit?.RetryAfter);

                case System.Net.HttpStatusCode.InternalServerError: // 500
                    _logger.LogError("Internal Server Error (500) for {Endpoint} - ESI is experiencing issues", endpoint);
                    throw new EsiServerException(endpoint, System.Net.HttpStatusCode.InternalServerError, esiResponse.RateLimit);

                case System.Net.HttpStatusCode.BadGateway: // 502
                    _logger.LogError("Bad Gateway (502) for {Endpoint} - ESI proxy error", endpoint);
                    throw new EsiServerException(endpoint, System.Net.HttpStatusCode.BadGateway, esiResponse.RateLimit);

                case System.Net.HttpStatusCode.ServiceUnavailable: // 503
                    _logger.LogError("Service Unavailable (503) for {Endpoint} - ESI is down or under maintenance", endpoint);
                    throw new EsiServerException(endpoint, System.Net.HttpStatusCode.ServiceUnavailable, esiResponse.RateLimit);

                case System.Net.HttpStatusCode.GatewayTimeout: // 504
                    _logger.LogError("Gateway Timeout (504) for {Endpoint} - ESI request timed out", endpoint);
                    throw new EsiServerException(endpoint, System.Net.HttpStatusCode.GatewayTimeout, esiResponse.RateLimit);

                default:
                    // 420 Error Limited (custom code)
                    if ((int)response.StatusCode == 420)
                    {
                        _logger.LogError("ERROR LIMITED (420) for {Endpoint} - Too many errors ({ErrorsRemaining}/{ErrorsLimit}), requests blocked until reset in {ResetSeconds}s",
                            endpoint, esiResponse.RateLimit?.ErrorLimitRemain, esiResponse.RateLimit?.ErrorLimitRemain,
                            esiResponse.RateLimit?.ErrorLimitReset);
                        throw new EsiErrorLimitException(endpoint, esiResponse.RateLimit);
                    }

                    _logger.LogWarning("Unexpected HTTP status {StatusCode} for {Endpoint}", (int)response.StatusCode, endpoint);
                    throw new EsiApiException(
                        $"Unexpected HTTP status {(int)response.StatusCode}",
                        endpoint,
                        response.StatusCode,
                        null,
                        esiResponse.RateLimit);
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
                // Ungültiges JSON ist ein Fehler, kein (leeres) Erfolgs-Ergebnis
                esiResponse.ErrorCategory = EsiErrorCategory.Malformed;
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
    /// Sammelt alle Seiten lokal und veröffentlicht das Gesamtergebnis erst
    /// nach Gesamterfolg (atomare Veröffentlichung). Jede fehlgeschlagene
    /// Seite oder Cancellation verwirft die bereits gesammelten Teildaten
    /// (Rückgabe null). Ein gültig leeres Gesamtergebnis (2xx ohne Daten)
    /// ist zulässig und wird als leere Liste zurückgegeben. Fehlgeschlagene
    /// Antworten werden nicht als Erfolg gecacht (erfolgt im Transport nur
    /// bei Data != null + ETag).
    /// </summary>
    private async Task<List<T>?> CollectAllPagesAtomicallyAsync<T>(
        EsiResponse<List<T>>? firstPage,
        Func<int, Task<EsiResponse<List<T>>?>> fetchPage,
        string resourceName,
        CancellationToken ct,
        Action<int, EsiResponse<List<T>>?, long>? pageObserved = null)
        where T : class
    {
        if (firstPage == null || firstPage.ErrorCategory != EsiErrorCategory.None)
        {
            _logger.LogWarning("Failed to fetch first page of {ResourceName}", resourceName);
            return null;
        }

        var result = new List<T>();
        if (firstPage.Data != null)
        {
            result.AddRange(firstPage.Data);
        }

        var totalPages = firstPage.TotalPages ?? 1;
        if (totalPages <= 1)
        {
            return result;
        }

        // Begrenzte Parallelität + kleine Staffelung statt vollständigem
        // Burst: ESI bewertet geballte Request-Spitzen negativ (Token-System,
        // 100 Fehler/Min → 420 auf alle Routen).
        var failed = false;
        var pageResults = new List<EsiResponse<List<T>>?>();
        using var semaphore = new SemaphoreSlim(4);
        var pageTasks = new List<Task>();

        for (int page = 2; page <= totalPages; page++)
        {
            var pageNum = page;
            pageTasks.Add(Task.Run(async () =>
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    await Task.Delay(250, ct);
                    var pageStopwatch = Stopwatch.StartNew();
                    var pageResponse = await fetchPage(pageNum);
                    pageStopwatch.Stop();
                    pageObserved?.Invoke(pageNum, pageResponse, pageStopwatch.ElapsedMilliseconds);
                    lock (pageResults)
                    {
                        if (pageResponse == null || pageResponse.ErrorCategory != EsiErrorCategory.None)
                        {
                            // Fehler/Cancellation: Teildaten verwerfen, nichts veröffentlichen
                            failed = true;
                        }
                        else
                        {
                            pageResults.Add(pageResponse);
                        }
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            }, ct));
        }

        await Task.WhenAll(pageTasks); // Cancellation → OperationCanceledException → kein Teilergebnis

        if (failed)
        {
            _logger.LogWarning("Failed to fetch all pages of {ResourceName} - discarding partial data", resourceName);
            return null;
        }

        foreach (var pageResponse in pageResults)
        {
            if (pageResponse?.Data == null)
            {
                continue;
            }

            // Verify Last-Modified header is consistent (ESI cache consistency check)
            if (firstPage.LastModified.HasValue && pageResponse.LastModified.HasValue
                && pageResponse.LastModified.Value != firstPage.LastModified.Value)
            {
                _logger.LogWarning(
                    "Cache inconsistency detected for {ResourceName}! First page Last-Modified: {First}, Current page: {Current}. " +
                    "Data may be incomplete or inconsistent.",
                    resourceName, firstPage.LastModified.Value, pageResponse.LastModified.Value);
            }

            result.AddRange(pageResponse.Data);
        }

        return result;
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

    /// <summary>
    /// Holt System-Jump Statistiken (letzte Stunde)
    /// GET /universe/system_jumps/
    /// Public endpoint, kein Auth erforderlich
    /// </summary>
    public async Task<List<WALLEve.Models.Esi.Universe.SystemJumps>?> GetSystemJumpsAsync()
    {
        try
        {
            var endpoint = "/universe/system_jumps/";
            _logger.LogDebug("Fetching system jumps from: {Endpoint}", endpoint);

            var response = await GetPublicApiAsync<List<WALLEve.Models.Esi.Universe.SystemJumps>>(endpoint);

            if (response != null)
            {
                _logger.LogInformation("Loaded jump statistics for {Count} systems", response.Count);
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get system jumps");
            return null;
        }
    }

    /// <summary>
    /// Holt System-Kill Statistiken (letzte Stunde)
    /// GET /universe/system_kills/
    /// Public endpoint, kein Auth erforderlich
    /// </summary>
    public async Task<List<WALLEve.Models.Esi.Universe.SystemKills>?> GetSystemKillsAsync()
    {
        try
        {
            var endpoint = "/universe/system_kills/";
            _logger.LogDebug("Fetching system kills from: {Endpoint}", endpoint);

            var response = await GetPublicApiAsync<List<WALLEve.Models.Esi.Universe.SystemKills>>(endpoint);

            if (response != null)
            {
                _logger.LogInformation("Loaded kill statistics for {Count} systems", response.Count);
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get system kills");
            return null;
        }
    }

    // Market Data Endpoints (Public) - for Market Analysis Feature

    /// <summary>
    /// Holt alle Market Orders für eine Region
    /// GET /markets/{region_id}/orders/
    /// Public endpoint, kein Auth erforderlich
    /// </summary>
    /// <param name="regionId">Region ID (z.B. 10000002 für The Forge/Jita)</param>
    /// <param name="typeId">Optional: Filter auf bestimmten Item Type</param>
    /// <param name="orderType">Order Type: "all", "buy", "sell" (default: "all")</param>
    /// <param name="page">Seite für Paginierung (default: 1)</param>
    public async Task<List<RegionalMarketOrder>?> GetRegionalMarketOrdersAsync(
        int regionId,
        int? typeId = null,
        string orderType = "all",
        int page = 1)
    {
        try
        {
            var queryParams = new List<string>
            {
                $"order_type={orderType}",
                $"page={page}"
            };

            if (typeId.HasValue)
            {
                queryParams.Add($"type_id={typeId.Value}");
            }

            var endpoint = $"/markets/{regionId}/orders/?{string.Join("&", queryParams)}";
            _logger.LogDebug("Fetching regional market orders from: {Endpoint}", endpoint);

            var response = await GetPublicApiAsync<List<RegionalMarketOrder>>(endpoint);

            if (response != null)
            {
                _logger.LogInformation("Loaded {Count} market orders for region {RegionId} (page {Page})",
                    response.Count, regionId, page);
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get regional market orders for region {RegionId}", regionId);
            return null;
        }
    }

    /// <summary>
    /// Holt alle Market Orders für eine Region (alle Seiten)
    /// Automatische Paginierung mit parallelen Requests
    /// </summary>
    public async Task<List<RegionalMarketOrder>?> GetAllRegionalMarketOrdersAsync(
        int regionId,
        int? typeId = null,
        string orderType = "all",
        CancellationToken ct = default,
        Action<RegionalScanPageTelemetry>? telemetrySink = null)
    {
        try
        {
            _logger.LogInformation("Fetching all market orders for region {RegionId}, Type: {TypeId}, OrderType: {OrderType}",
                regionId, typeId, orderType);

            // Erste Seite holen um X-Pages zu erhalten
            var queryParams = new List<string>
            {
                $"order_type={orderType}",
                "page=1"
            };

            if (typeId.HasValue)
            {
                queryParams.Add($"type_id={typeId.Value}");
            }

            var firstPageEndpoint = $"/markets/{regionId}/orders/?{string.Join("&", queryParams)}";
            var firstPageStopwatch = Stopwatch.StartNew();
            var firstPage = await GetPublicApiWithHeadersAsync<List<RegionalMarketOrder>>(firstPageEndpoint, ct);
            firstPageStopwatch.Stop();
            telemetrySink?.Invoke(new RegionalScanPageTelemetry
            {
                RegionId = regionId,
                Page = 1,
                OrderCount = firstPage?.Data?.Count ?? 0,
                ContentLength = firstPage?.ContentLength,
                ElapsedMs = firstPageStopwatch.ElapsedMilliseconds,
                FromCache = firstPage?.IsNotModified ?? false,
                Success = firstPage?.ErrorCategory == EsiErrorCategory.None
            });

            var allOrders = await CollectAllPagesAtomicallyAsync(
                firstPage,
                page =>
                {
                    var pageParams = new List<string>
                    {
                        $"order_type={orderType}",
                        $"page={page}"
                    };

                    if (typeId.HasValue)
                    {
                        pageParams.Add($"type_id={typeId.Value}");
                    }

                    var pageEndpoint = $"/markets/{regionId}/orders/?{string.Join("&", pageParams)}";
                    return GetPublicApiWithHeadersAsync<List<RegionalMarketOrder>>(pageEndpoint, ct);
                },
                $"market orders for region {regionId}",
                ct,
                (page, pageResponse, elapsedMs) => telemetrySink?.Invoke(new RegionalScanPageTelemetry
                {
                    RegionId = regionId,
                    Page = page,
                    OrderCount = pageResponse?.Data?.Count ?? 0,
                    ContentLength = pageResponse?.ContentLength,
                    ElapsedMs = elapsedMs,
                    FromCache = pageResponse?.IsNotModified ?? false,
                    Success = pageResponse?.ErrorCategory == EsiErrorCategory.None
                }));

            if (allOrders != null)
            {
                _logger.LogInformation("Successfully loaded {TotalCount} market orders across {TotalPages} pages",
                    allOrders.Count, firstPage?.TotalPages ?? 1);
            }

            return allOrders; // null = Fehler/Cancellation → Aufrufer behält alten Snapshot
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Fetching all regional market orders cancelled for region {RegionId}", regionId);
            return null; // Cancellation publiziert keine Teilmenge
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching all regional market orders");
            return null; // kein Teildaten-Leak
        }
    }

    /// <summary>
    /// Holt historische Market-Statistiken für einen Item Type in einer Region
    /// GET /markets/{region_id}/history/
    /// Public endpoint, kein Auth erforderlich
    /// </summary>
    /// <param name="regionId">Region ID</param>
    /// <param name="typeId">Type ID des Items</param>
    public async Task<List<MarketHistoryEntry>?> GetMarketHistoryAsync(int regionId, int typeId)
    {
        try
        {
            var endpoint = $"/markets/{regionId}/history/?type_id={typeId}";
            _logger.LogDebug("Fetching market history from: {Endpoint}", endpoint);

            var response = await GetPublicApiAsync<List<MarketHistoryEntry>>(endpoint);

            if (response != null)
            {
                _logger.LogInformation("Loaded {Count} days of market history for type {TypeId} in region {RegionId}",
                    response.Count, typeId, regionId);
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get market history for type {TypeId} in region {RegionId}",
                typeId, regionId);
            return null;
        }
    }

    /// <summary>
    /// Holt globale Durchschnittspreise für alle Items
    /// GET /markets/prices/
    /// Public endpoint, kein Auth erforderlich
    /// </summary>
    public async Task<List<MarketPrice>?> GetMarketPricesAsync()
    {
        try
        {
            var endpoint = "/markets/prices/";
            _logger.LogDebug("Fetching global market prices from: {Endpoint}", endpoint);

            var response = await GetPublicApiAsync<List<MarketPrice>>(endpoint);

            if (response != null)
            {
                _logger.LogInformation("Loaded global prices for {Count} item types", response.Count);
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get global market prices");
            return null;
        }
    }

    /// <summary>
    /// Erweiterte Public API-Methode die Response Headers ausliest (für Paginierung)
    /// Ähnlich zu GetAuthenticatedApiWithHeadersAsync, aber ohne Auth
    /// </summary>
    private async Task<EsiResponse<T>?> GetPublicApiWithHeadersAsync<T>(string endpoint, CancellationToken ct)
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

            var response = await client.GetAsync($"{_settings.EsiBaseUrl}{endpoint}", ct);

            var esiResponse = new EsiResponse<T>
            {
                StatusCode = (int)response.StatusCode,
                FetchedAt = DateTime.UtcNow
            };

            // Parse Response Headers
            esiResponse.RateLimit = ParseRateLimitHeaders(response.Headers);

            // Rohtransfergröße für die Messung (#67): Content-Length der Antwort.
            // null bei 304 (kein Body-Transfer) oder fehlendem/leerem Header.
            esiResponse.ContentLength = response.Content.Headers.ContentLength is > 0
                ? response.Content.Headers.ContentLength
                : null;

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

            // Handle status codes
            if (response.StatusCode == System.Net.HttpStatusCode.NotModified && cachedEntry != null)
            {
                _logger.LogInformation("ESI returned 304 Not Modified for {Endpoint} - using cached data", endpoint);
                esiResponse.Data = cachedEntry.Data;
                esiResponse.ETag = cachedEntry.ETag;
                esiResponse.Expires = cachedEntry.Expires;
                // Ursprünglicher Datenzeitpunkt des Snapshots bleibt erhalten
                esiResponse.FetchedAt = cachedEntry.CachedAt;
                return esiResponse;
            }

            response.EnsureSuccessStatusCode();

            // Parse response content
            var content = await response.Content.ReadAsStringAsync();

            if (!string.IsNullOrWhiteSpace(content))
            {
                esiResponse.Data = JsonSerializer.Deserialize<T>(content);

                // 3. Cache successful response with ETag
                if (esiResponse.Data != null && !string.IsNullOrEmpty(esiResponse.ETag))
                {
                    _cacheService.Set(endpoint, esiResponse.ETag, esiResponse.Data, esiResponse.Expires);
                    _logger.LogDebug("Cached {Endpoint} with ETag {ETag}, Expires: {Expires}",
                        endpoint, esiResponse.ETag, esiResponse.Expires);
                }
            }

            return esiResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling public ESI endpoint: {Endpoint}", endpoint);
            return null;
        }
    }
}
