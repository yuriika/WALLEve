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
    private readonly EveOnlineSettings _settings;
    private readonly ITokenStorageService _tokenStorage;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<EveAuthenticationService> _logger;

    public event EventHandler<bool>? AuthenticationStateChanged;

    public EveAuthenticationService(
        IOptions<EveOnlineSettings> settings,
        ITokenStorageService tokenStorage,
        IHttpClientFactory httpClientFactory,
        ILogger<EveAuthenticationService> logger)
    {
        _settings = settings.Value;
        _tokenStorage = tokenStorage;
        _httpClientFactory = httpClientFactory;
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
        
        _logger.LogInformation("Generated login URL with state {State}", pkce.State);
        return loginUrl;
    }

    public async Task<bool> HandleCallbackAsync(string code, string state)
    {
        try
        {
            _logger.LogInformation("Handling OAuth callback with state {State}", state);
            
            var pkce = _tokenStorage.GetAndClearPkceChallenge(state);
            if (pkce == null)
            {
                _logger.LogError("No PKCE challenge found for state {State}", state);
                return false;
            }

            var tokenResponse = await ExchangeCodeForTokensAsync(code, pkce.CodeVerifier);
            if (tokenResponse == null)
            {
                _logger.LogError("Failed to exchange code for tokens");
                return false;
            }

            var jwtPayload = DecodeJwtPayload(tokenResponse.AccessToken);
            if (jwtPayload == null)
            {
                _logger.LogError("Failed to decode JWT payload");
                return false;
            }

            var authState = new EveAuthState
            {
                AccessToken = tokenResponse.AccessToken,
                RefreshToken = tokenResponse.RefreshToken,
                ExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn - 60),
                CharacterId = jwtPayload.GetCharacterId(),
                CharacterName = jwtPayload.Name,
                Scopes = jwtPayload.GetScopes()
            };

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
            _logger.LogInformation("Access token expired, refreshing...");
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
        }
        
        await _tokenStorage.ClearAuthStateAsync();
        
        _logger.LogInformation("User logged out");
        AuthenticationStateChanged?.Invoke(this, false);
    }

    private async Task<EveTokenResponse?> ExchangeCodeForTokensAsync(string code, string codeVerifier)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["client_id"] = _settings.ClientId,
                ["code_verifier"] = codeVerifier
            });

            var response = await client.PostAsync($"{_settings.SsoBaseUrl}/token", content);
            var responseContent = await response.Content.ReadAsStringAsync();
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Token exchange failed: {Status} - {Content}", 
                    response.StatusCode, responseContent);
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
            var client = _httpClientFactory.CreateClient();
            
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = state.RefreshToken,
                ["client_id"] = _settings.ClientId
            });

            var response = await client.PostAsync($"{_settings.SsoBaseUrl}/token", content);
            var responseContent = await response.Content.ReadAsStringAsync();
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Token refresh failed: {Status} - {Content}", 
                    response.StatusCode, responseContent);
                return false;
            }

            var tokenResponse = JsonSerializer.Deserialize<EveTokenResponse>(responseContent);
            if (tokenResponse == null)
            {
                return false;
            }

            state.AccessToken = tokenResponse.AccessToken;
            state.RefreshToken = tokenResponse.RefreshToken;
            state.ExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn - 60);
            
            await _tokenStorage.SaveAuthStateAsync(state);
            
            _logger.LogInformation("Successfully refreshed access token");
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
            var client = _httpClientFactory.CreateClient();
            
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["token"] = refreshToken,
                ["token_type_hint"] = "refresh_token",
                ["client_id"] = _settings.ClientId
            });

            await client.PostAsync($"{_settings.SsoBaseUrl}/revoke", content);
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

    private static EveJwtPayload? DecodeJwtPayload(string jwt)
    {
        try
        {
            var parts = jwt.Split('.');
            if (parts.Length != 3) return null;

            var payload = parts[1];
            switch (payload.Length % 4)
            {
                case 2: payload += "=="; break;
                case 3: payload += "="; break;
            }
            
            payload = payload.Replace('-', '+').Replace('_', '/');
            var payloadBytes = Convert.FromBase64String(payload);
            var payloadJson = Encoding.UTF8.GetString(payloadBytes);
            
            return JsonSerializer.Deserialize<EveJwtPayload>(payloadJson);
        }
        catch
        {
            return null;
        }
    }
}
