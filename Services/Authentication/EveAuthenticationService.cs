using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using WALLEve.Configuration;
using WALLEve.Models.Authentication;
using WALLEve.Services.Authentication.Interfaces;

namespace WALLEve.Services.Authentication;

public class EveAuthenticationService : IEveAuthenticationService
{
    private const string SsoHttpClientName = "EveSso";
    private const int SsoTokenTimeoutSeconds = 15;

    private readonly EveOnlineSettings _settings;
    private readonly ITokenStorageService _tokenStorage;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IJwtTokenValidator _jwtValidator;
    private readonly ILogger<EveAuthenticationService> _logger;

    public event EventHandler<bool>? AuthenticationStateChanged;

    public EveAuthenticationService(
        IOptions<EveOnlineSettings> settings,
        ITokenStorageService tokenStorage,
        IHttpClientFactory httpClientFactory,
        IJwtTokenValidator jwtValidator,
        ILogger<EveAuthenticationService> logger)
    {
        _settings = settings.Value;
        _tokenStorage = tokenStorage;
        _httpClientFactory = httpClientFactory;
        _jwtValidator = jwtValidator;
        _logger = logger;
    }

    public async Task<EveAuthState?> GetAuthStateAsync()
    {
        return await _tokenStorage.GetAuthStateAsync();
    }

    public async Task<bool> IsAuthenticatedAsync()
    {
        var state = await _tokenStorage.GetAuthStateAsync();
        return state?.IsValid == true;
    }

    public string GetLoginUrl()
    {
        var pkce = GeneratePkceChallenge();
        _tokenStorage.StorePkceChallenge(pkce);

        var queryParams = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["redirect_uri"] = _settings.CallbackUrl,
            ["client_id"] = _settings.ClientId,
            ["scope"] = _settings.ScopesString,
            ["state"] = pkce.State,
            ["code_challenge"] = pkce.CodeChallenge,
            ["code_challenge_method"] = "S256"
        };

        var queryString = string.Join("&",
            queryParams.Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));

        var loginUrl = $"{_settings.SsoBaseUrl}/authorize?{queryString}";

        return loginUrl;
    }

    public async Task<bool> HandleCallbackAsync(string code, string state)
    {
        try
        {
            var pkce = _tokenStorage.GetAndClearPkceChallenge(state);
            if (pkce == null)
            {
                _logger.LogError("No PKCE challenge found for state");
                return false;
            }

            var tokenResponse = await ExchangeCodeForTokensAsync(code, pkce.CodeVerifier);
            if (tokenResponse == null)
            {
                _logger.LogError("Failed to exchange code for tokens");
                return false;
            }

            // JWT-Signatur, Issuer, Audience und Expiry validieren
            var validationResult = await _jwtValidator.ValidateTokenAsync(
                tokenResponse.AccessToken, _settings.ClientId);
            if (!validationResult.IsValid)
            {
                _logger.LogError("JWT validation failed: {Error}", validationResult.Error);
                return false;
            }

            var jwtPayload = validationResult.Payload!;
            if (jwtPayload == null)
            {
                _logger.LogError("JWT validation returned no payload");
                return false;
            }

            var authState = new EveAuthState
            {
                AccessToken = tokenResponse.AccessToken,
                RefreshToken = tokenResponse.RefreshToken,
                ExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn - 60),
                CharacterId = jwtPayload.GetCharacterId(),
                CharacterName = jwtPayload.Name,
                Scopes = jwtPayload.GetScopes(),
                MissingScopes = ComputeMissingScopes(jwtPayload.GetScopes())
            };

            if (authState.MissingScopes.Count > 0)
            {
                _logger.LogWarning("Character {CharacterName} (ID: {CharacterId}) ist ohne benötigte Scopes autorisiert. Fehlend: {MissingScopes}. Nach vollständigem Logout und erneutem Login verfügbar.",
                    authState.CharacterName, authState.CharacterId,
                    string.Join(", ", authState.MissingScopes));
            }

            await _tokenStorage.SaveAuthStateAsync(authState);

            _logger.LogInformation("Successfully authenticated character {CharacterName} (ID: {CharacterId})",
                authState.CharacterName, authState.CharacterId);

            AuthenticationStateChanged?.Invoke(this, true);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling OAuth callback");
            return false;
        }
    }

    public async Task<string?> GetAccessTokenAsync()
    {
        var state = await _tokenStorage.GetAuthStateAsync();
        if (state == null || !state.IsValid)
        {
            return null;
        }

        if (state.IsExpired)
        {
            var refreshed = await RefreshTokenAsync(state);
            if (!refreshed)
            {
                _logger.LogWarning("Token refresh failed");
                return null;
            }

            state = await _tokenStorage.GetAuthStateAsync();
        }

        return state?.AccessToken;
    }

    public async Task LogoutAsync()
    {
        var state = await _tokenStorage.GetAuthStateAsync();
        if (state != null)
        {
            await TryRevokeTokenAsync(state.RefreshToken);
            await _tokenStorage.RemoveCharacterAsync(state.CharacterId);
        }
        else
        {
            await _tokenStorage.ClearAuthStateAsync();
        }

        _logger.LogInformation("User logged out (active character removed)");
        // Gibt es noch andere gespeicherte Chars? → nächster wird aktiv, sonst abgemeldet.
        var remaining = await _tokenStorage.GetAllCharactersAsync();
        AuthenticationStateChanged?.Invoke(this, remaining.Count > 0);
    }

    public async Task<List<KnownCharacter>> GetAllCharactersAsync()
    {
        var chars = await _tokenStorage.GetAllCharactersAsync();
        var active = await _tokenStorage.GetAuthStateAsync();
        foreach (var c in chars)
        {
            c.IsActive = active != null && c.CharacterId == active.CharacterId;
        }
        return chars;
    }

    public async Task<bool> SwitchCharacterAsync(int characterId)
    {
        var chars = await _tokenStorage.GetAllCharactersAsync();
        if (!chars.Any(c => c.CharacterId == characterId))
        {
            _logger.LogWarning("Cannot switch to unknown character {CharacterId}", characterId);
            return false;
        }

        await _tokenStorage.SetActiveCharacterAsync(characterId);
        AuthenticationStateChanged?.Invoke(this, true);
        return true;
    }

    private async Task<EveTokenResponse?> ExchangeCodeForTokensAsync(string code, string codeVerifier)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(SsoHttpClientName);

            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["client_id"] = _settings.ClientId,
                ["code_verifier"] = codeVerifier
            });

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(SsoTokenTimeoutSeconds));
            var response = await client.PostAsync($"{_settings.SsoBaseUrl}/token", content, cts.Token);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Token exchange failed: {Status}", response.StatusCode);
                return null;
            }

            return JsonSerializer.Deserialize<EveTokenResponse>(responseContent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exchanging code for tokens");
            return null;
        }
    }

    private async Task<bool> RefreshTokenAsync(EveAuthState state)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(SsoHttpClientName);

            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = state.RefreshToken,
                ["client_id"] = _settings.ClientId
            });

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(SsoTokenTimeoutSeconds));
            var response = await client.PostAsync($"{_settings.SsoBaseUrl}/token", content, cts.Token);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Token refresh failed: {Status}", response.StatusCode);
                return false;
            }

            var tokenResponse = JsonSerializer.Deserialize<EveTokenResponse>(responseContent);
            if (tokenResponse == null)
            {
                return false;
            }

            // Vertrauensgrenze wie beim initialen Login: Das neue Access-Token erst
            // nach erfolgreicher JWKS-Validierung (Signatur, Issuer, Audience, exp)
            // persistieren. Ein ungültiges/vertauschtes Refresh-Token wird verworfen.
            var validationResult = await _jwtValidator.ValidateTokenAsync(
                tokenResponse.AccessToken, _settings.ClientId);
            if (!validationResult.IsValid)
            {
                _logger.LogError("JWT validation failed after token refresh: {Error}",
                    validationResult.Error);
                return false;
            }

            state.AccessToken = tokenResponse.AccessToken;
            state.RefreshToken = tokenResponse.RefreshToken;
            state.ExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn - 60);
            state.Scopes = validationResult.Payload?.GetScopes() ?? state.Scopes;

            await _tokenStorage.SaveAuthStateAsync(state);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing token");
            return false;
        }
    }

    private async Task TryRevokeTokenAsync(string refreshToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(SsoHttpClientName);

            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["token"] = refreshToken,
                ["token_type_hint"] = "refresh_token",
                ["client_id"] = _settings.ClientId
            });

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(SsoTokenTimeoutSeconds));
            await client.PostAsync($"{_settings.SsoBaseUrl}/revoke", content, cts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error revoking token");
        }
    }

    private static PkceChallenge GeneratePkceChallenge()
    {
        var randomBytes = new byte[32];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(randomBytes);
        }

        var codeVerifier = Base64UrlEncode(randomBytes);

        using var sha256 = SHA256.Create();
        var challengeBytes = sha256.ComputeHash(Encoding.ASCII.GetBytes(codeVerifier));
        var codeChallenge = Base64UrlEncode(challengeBytes);

        var stateBytes = new byte[16];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(stateBytes);
        }
        var state = Base64UrlEncode(stateBytes);

        return new PkceChallenge
        {
            CodeVerifier = codeVerifier,
            CodeChallenge = codeChallenge,
            State = state
        };
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary>
    /// Ordinaler Soll/Ist-Abgleich: welche konfigurierten (benötigten) Scopes
    /// wurden im Token nicht zugestanden? EVE-Scope-Namen sind case-sensitiv.
    /// </summary>
    private List<string> ComputeMissingScopes(IReadOnlyCollection<string> grantedScopes)
    {
        var granted = new HashSet<string>(grantedScopes, StringComparer.Ordinal);
        return _settings.Scopes
            .Where(required => !granted.Contains(required))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}