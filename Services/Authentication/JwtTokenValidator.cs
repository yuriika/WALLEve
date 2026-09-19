using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using WALLEve.Models.Authentication;
using WALLEve.Services.Authentication.Interfaces;

namespace WALLEve.Services.Authentication;

/// <summary>
/// Validiert EVE SSO JWT-Zugriffstokens über das OAuth Authorization Server Metadata
/// Discovery-Dokument und den JWKS-Endpunkt. Die Discovery- und JWKS-Ergebnisse werden
 /// mit begrenzter Lebensdauer zwischengespeichert.
/// </summary>
public class JwtTokenValidator : IJwtTokenValidator
{
    private const string SsoClientName = "EveSso";
    private const string DiscoveryCacheKey = "EveSsoDiscovery_Url";
    private const string JwksCacheKey = "EveSsoJwks";
    private static readonly Uri DiscoveryUrl = new("https://login.eveonline.com/.well-known/oauth-authorization-server");
    private static readonly TimeSpan DiscoveryCacheDuration = TimeSpan.FromHours(24);
    private static readonly TimeSpan JwksCacheDuration = TimeSpan.FromHours(1);

    private static readonly string[] AllowedIssuers =
        ["login.eveonline.com", "https://login.eveonline.com"];

    private const string EveOnlineAudienceValue = "EVE Online";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<JwtTokenValidator> _logger;

    public JwtTokenValidator(
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        ILogger<JwtTokenValidator> logger)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _logger = logger;
    }

    public async Task<JwtValidationResult> ValidateTokenAsync(string accessToken, string expectedClientId)
    {
        try
        {
            var jwksUri = await GetJwksUriAsync();
            if (jwksUri == null)
            {
                return JwtValidationResult.Invalid("Failed to retrieve SSO metadata endpoint");
            }

            var jwks = await GetJwksAsync(jwksUri);
            if (jwks == null || jwks.Keys.Count == 0)
            {
                return JwtValidationResult.Invalid("Failed to retrieve JWKS keys");
            }

            var handler = new JwtSecurityTokenHandler();

            var validationParameters = new TokenValidationParameters
            {
                IssuerSigningKeys = jwks.Keys,
                ValidIssuers = AllowedIssuers,
                ValidateIssuer = true,
                ValidateLifetime = true,
                ValidateAudience = false, // Wird benutzerdefiniert über AudienceValidator geprüft
                ClockSkew = TimeSpan.Zero,
                AudienceValidator = (audiences, token, parameters) =>
                {
                    var audList = audiences as IEnumerable<string> ?? [];
                    var audSet = new HashSet<string>(audList, StringComparer.Ordinal);
                    return audSet.Contains(expectedClientId) && audSet.Contains(EveOnlineAudienceValue);
                }
            };

            var principal = handler.ValidateToken(accessToken, validationParameters, out var validatedToken);

            if (validatedToken is not JwtSecurityToken jwt)
            {
                return JwtValidationResult.Invalid("Token is not a valid JWT");
            }

            var payload = new EveJwtPayload
            {
                Subject = jwt.Subject ?? "",
                Name = jwt.Claims.FirstOrDefault(c => c.Type == "name")?.Value ?? "",
                Owner = jwt.Claims.FirstOrDefault(c => c.Type == "owner")?.Value ?? "",
                Expiration = long.TryParse(
                    jwt.Claims.FirstOrDefault(c => c.Type == "exp")?.Value, out var exp) ? exp : 0,
                Issuer = jwt.Issuer ?? "",
                Scopes = ParseScopes(jwt)
            };

            return JwtValidationResult.Valid(payload);
        }
        catch (SecurityTokenExpiredException)
        {
            return JwtValidationResult.Invalid("Token has expired");
        }
        catch (SecurityTokenInvalidIssuerException)
        {
            return JwtValidationResult.Invalid("Token has an invalid issuer");
        }
        catch (SecurityTokenInvalidAudienceException)
        {
            return JwtValidationResult.Invalid("Token has an invalid audience");
        }
        catch (SecurityTokenSignatureKeyNotFoundException)
        {
            return JwtValidationResult.Invalid("Token signature key not found in JWKS");
        }
        catch (SecurityTokenInvalidSignatureException)
        {
            return JwtValidationResult.Invalid("Token has an invalid signature");
        }
        catch (ArgumentException ex) when (ex.Message.Contains("JWT") || ex.Message.Contains("token"))
        {
            return JwtValidationResult.Invalid($"Token format error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected JWT validation error");
            return JwtValidationResult.Invalid($"JWT validation error: {ex.Message}");
        }
    }

    /// <summary>Ermittelt die JWKS-URI aus dem Discovery-Dokument (mit Cache).</summary>
    private async Task<Uri?> GetJwksUriAsync()
    {
        if (_cache.TryGetValue(DiscoveryCacheKey, out string? cachedUrl) && cachedUrl != null)
        {
            return new Uri(cachedUrl);
        }

        try
        {
            var client = _httpClientFactory.CreateClient(SsoClientName);
            var response = await client.GetAsync(DiscoveryUrl);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("SSO discovery returned {StatusCode}", response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var jwksUri = doc.RootElement.GetProperty("jwks_uri").GetString();

            if (string.IsNullOrEmpty(jwksUri))
            {
                return null;
            }

            _cache.Set(DiscoveryCacheKey, jwksUri, DiscoveryCacheDuration);
            return new Uri(jwksUri);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch SSO discovery document");
            return null;
        }
    }

    /// <summary>Lädt den JWKS-Schlüsselsatz (mit Cache).</summary>
    private async Task<JsonWebKeySet?> GetJwksAsync(Uri jwksUri)
    {
        if (_cache.TryGetValue(JwksCacheKey, out JsonWebKeySet? cached) && cached != null)
        {
            return cached;
        }

        try
        {
            var client = _httpClientFactory.CreateClient(SsoClientName);
            var response = await client.GetAsync(jwksUri);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("JWKS endpoint returned {StatusCode}", response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var jwks = new JsonWebKeySet(json);

            _cache.Set(JwksCacheKey, jwks, JwksCacheDuration);
            return jwks;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch JWKS keys");
            return null;
        }
    }

    /// <summary>Parst den SCP-Anspruch (Scopes) aus dem JWT, der als einzelner String oder Array vorliegen kann.</summary>
    private static object? ParseScopes(JwtSecurityToken jwt)
    {
        var scpClaim = jwt.Claims.FirstOrDefault(c => c.Type == "scp");
        if (scpClaim == null) return null;

        // Der SCP-Wert liegt als JSON-String vor (entweder "scope1" oder ["scope1","scope2"])
        var raw = scpClaim.Value;
        if (raw.StartsWith('['))
        {
            using var doc = JsonDocument.Parse(raw);
            var elements = new List<string>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                elements.Add(el.GetString() ?? "");
            }
            return JsonSerializer.Serialize(elements);
        }

        return raw;
    }
}