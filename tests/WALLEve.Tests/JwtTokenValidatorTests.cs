using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using WALLEve.Services.Authentication;

namespace WALLEve.Tests;

public class JwtTokenValidatorTests
{
    private static readonly RSA TestKey = RSA.Create(2048);
    private const string TestKeyKid = "test-sso-key";
    private const string TestClientId = "my-test-client-id";
    private const string ExpectedIssuer = "login.eveonline.com";

    private static readonly RsaSecurityKey RsaSecurityKey;

    static JwtTokenValidatorTests()
    {
        RsaSecurityKey = new RsaSecurityKey(TestKey) { KeyId = TestKeyKid };
    }

    /// <summary>Serialisiert den öffentlichen RSA-Schlüssel als JWK für die JWKS-Antwort.</summary>
    private static string GetJwksJson()
    {
        var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(RsaSecurityKey);
        jwk.Kid = TestKeyKid;
        jwk.Alg = SecurityAlgorithms.RsaSha256;
        // JsonWebKey serialisiert nur die öffentlichen Parameter (n, e)
        return JsonSerializer.Serialize(new { keys = new[] { jwk } }, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    private static string GetDiscoveryJson()
    {
        return JsonSerializer.Serialize(new
        {
            issuer = "https://login.eveonline.com",
            jwks_uri = "https://login.eveonline.com/oauth/jwks",
            authorization_endpoint = "https://login.eveonline.com/v2/oauth/authorize",
            token_endpoint = "https://login.eveonline.com/v2/oauth/token",
            response_types_supported = new[] { "code" },
            subject_types_supported = new[] { "public" },
            id_token_signing_alg_values_supported = new[] { "RS256", "ES256" }
        });
    }

    /// <summary>Erstellt ein signiertes JWT mit dem Test-RSA-Key.</summary>
    private static string CreateSignedJwt(
        string issuer = "login.eveonline.com",
        string? clientId = TestClientId,
        string[]? audience = null,
        DateTime? expires = null,
        DateTime? notBefore = null,
        string scopes = "esi-test.v1",
        string subject = "CHARACTER:EVE:12345",
        string name = "Test Character",
        bool signWithCorrectKey = true,
        string? customKid = null,
        string? algorithmOverride = null)
    {
        var key = signWithCorrectKey ? TestKey : RSA.Create(2048);
        var securityKey = new RsaSecurityKey(key) { KeyId = customKid ?? TestKeyKid };
        var signingAlgorithm = algorithmOverride ?? SecurityAlgorithms.RsaSha256;
        var creds = string.Equals(signingAlgorithm, SecurityAlgorithms.None, StringComparison.OrdinalIgnoreCase)
            ? null
            : new SigningCredentials(securityKey, signingAlgorithm);

        var claims = new List<Claim>
        {
            new("sub", subject),
            new("name", name),
            new("owner", "dGVzdC1vd25lcg=="),
            new("scp", scopes),
            new("jti", Guid.NewGuid().ToString()),
            new("azp", clientId ?? TestClientId),
            new("tenant", "tranquility"),
            new("tier", "live"),
            new("region", "world")
        };

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = issuer,
            Expires = expires ?? DateTime.UtcNow.AddHours(1),
            NotBefore = notBefore ?? DateTime.UtcNow.AddHours(-2),
            SigningCredentials = creds,
            Claims = new Dictionary<string, object>()
        };

        // Handle audience as array (EVE SSO sends ["client_id", "EVE Online"])
        var aud = audience ?? new[] { clientId ?? TestClientId, "EVE Online" };
        descriptor.Claims["aud"] = aud;

        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateToken(descriptor);
        return handler.WriteToken(token);
    }

    /// <summary>Erstellt einen gültigen JWT-Token mit Standard-Parametern.</summary>
    private static string CreateValidToken() => CreateSignedJwt();

    /// <summary>Baut eine JwtTokenValidator-Instanz mit gemocktem HTTP-Backend.</summary>
    private static (JwtTokenValidator Validator, MockHttpHandler Handler) CreateValidator()
    {
        var handler = new MockHttpHandler();
        var httpClientFactory = new StubHttpClientFactory(handler);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var validator = new JwtTokenValidator(httpClientFactory, cache, NullLogger<JwtTokenValidator>.Instance);
        return (validator, handler);
    }

    /// <summary>Base64url-Kodierung (kein Padding, - statt +, _ statt /).</summary>
    private static string Base64UrlEncode(string value)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public sealed class MockHttpHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, Func<string>> _responses = new();
        public List<string> RequestedUrls { get; } = new();

        public void AddResponse(string url, Func<string> responseFactory)
        {
            _responses[url] = responseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? "";
            RequestedUrls.Add(url);

            if (_responses.TryGetValue(url, out var factory))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(factory(), Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("Not found")
            });
        }
    }

    /// <summary>IHttpClientFactory, die immer den gemockten Handler zurückgibt.</summary>
    public sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(_handler);
        }
    }

    // ======================================================================
    // RED: Schreib die Tests — sie müssen fehlschlagen, weil es die Klasse
    // JwtTokenValidator noch nicht gibt.
    // ======================================================================

    [Fact]
    public async Task Validate_ValidEveToken_ReturnsValidWithPayload()
    {
        var (validator, handler) = CreateValidator();
        var discoveryJson = GetDiscoveryJson();
        var jwksJson = GetJwksJson();
        handler.AddResponse("https://login.eveonline.com/.well-known/oauth-authorization-server", () => discoveryJson);
        handler.AddResponse("https://login.eveonline.com/oauth/jwks", () => jwksJson);

        var token = CreateValidToken();
        var result = await validator.ValidateTokenAsync(token, TestClientId);

        Assert.True(result.IsValid);
        Assert.NotNull(result.Payload);
        Assert.Equal("Test Character", result.Payload!.Name);
        Assert.Equal(12345, result.Payload.GetCharacterId());
        Assert.Contains(ExpectedIssuer, result.Payload.Issuer);
    }

    [Fact]
    public async Task Validate_ExpiredToken_ReturnsInvalid()
    {
        var (validator, handler) = CreateValidator();
        var discoveryJson = GetDiscoveryJson();
        var jwksJson = GetJwksJson();
        handler.AddResponse("https://login.eveonline.com/.well-known/oauth-authorization-server", () => discoveryJson);
        handler.AddResponse("https://login.eveonline.com/oauth/jwks", () => jwksJson);

        // Token bereits vor 1 Stunde abgelaufen (notBefore muss vor expires liegen)
        var token = CreateSignedJwt(expires: DateTime.UtcNow.AddHours(-1), notBefore: DateTime.UtcNow.AddHours(-2));
        var result = await validator.ValidateTokenAsync(token, TestClientId);

        Assert.False(result.IsValid);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task Validate_WrongIssuer_ReturnsInvalid()
    {
        var (validator, handler) = CreateValidator();
        var discoveryJson = GetDiscoveryJson();
        var jwksJson = GetJwksJson();
        handler.AddResponse("https://login.eveonline.com/.well-known/oauth-authorization-server", () => discoveryJson);
        handler.AddResponse("https://login.eveonline.com/oauth/jwks", () => jwksJson);

        var token = CreateSignedJwt(issuer: "evil.example.com");
        var result = await validator.ValidateTokenAsync(token, TestClientId);

        Assert.False(result.IsValid);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task Validate_MissingClientAudience_ReturnsInvalid()
    {
        var (validator, handler) = CreateValidator();
        var discoveryJson = GetDiscoveryJson();
        var jwksJson = GetJwksJson();
        handler.AddResponse("https://login.eveonline.com/.well-known/oauth-authorization-server", () => discoveryJson);
        handler.AddResponse("https://login.eveonline.com/oauth/jwks", () => jwksJson);

        // aud enthält nur "EVE Online", aber nicht die Client-ID
        var token = CreateSignedJwt(audience: new[] { "EVE Online" });
        var result = await validator.ValidateTokenAsync(token, TestClientId);

        Assert.False(result.IsValid);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task Validate_MissingEveOnlineAudience_ReturnsInvalid()
    {
        var (validator, handler) = CreateValidator();
        var discoveryJson = GetDiscoveryJson();
        var jwksJson = GetJwksJson();
        handler.AddResponse("https://login.eveonline.com/.well-known/oauth-authorization-server", () => discoveryJson);
        handler.AddResponse("https://login.eveonline.com/oauth/jwks", () => jwksJson);

        // aud enthält die Client-ID, aber nicht "EVE Online"
        var token = CreateSignedJwt(audience: new[] { TestClientId });
        var result = await validator.ValidateTokenAsync(token, TestClientId);

        Assert.False(result.IsValid);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task Validate_TamperedSignature_ReturnsInvalid()
    {
        var (validator, handler) = CreateValidator();
        var discoveryJson = GetDiscoveryJson();
        var jwksJson = GetJwksJson();
        handler.AddResponse("https://login.eveonline.com/.well-known/oauth-authorization-server", () => discoveryJson);
        handler.AddResponse("https://login.eveonline.com/oauth/jwks", () => jwksJson);

        var token = CreateValidToken();
        // Manipuliere die Signatur (letzter Teil des JWT)
        var parts = token.Split('.');
        var tamperedToken = $"{parts[0]}.{parts[1]}.InvalidBase64Signature";
        var result = await validator.ValidateTokenAsync(tamperedToken, TestClientId);

        Assert.False(result.IsValid);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task Validate_WrongSigningKey_ReturnsInvalid()
    {
        var (validator, handler) = CreateValidator();
        var discoveryJson = GetDiscoveryJson();
        var jwksJson = GetJwksJson();
        handler.AddResponse("https://login.eveonline.com/.well-known/oauth-authorization-server", () => discoveryJson);
        handler.AddResponse("https://login.eveonline.com/oauth/jwks", () => jwksJson);

        // Mit anderem Schlüssel signiert (signWithCorrectKey: false)
        var token = CreateSignedJwt(signWithCorrectKey: false, customKid: "different-key");
        var result = await validator.ValidateTokenAsync(token, TestClientId);

        Assert.False(result.IsValid);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task Validate_JwksIsCached_AfterFirstFetch()
    {
        var (validator, handler) = CreateValidator();
        var discoveryJson = GetDiscoveryJson();
        var jwksJson = GetJwksJson();
        handler.AddResponse("https://login.eveonline.com/.well-known/oauth-authorization-server", () => discoveryJson);
        handler.AddResponse("https://login.eveonline.com/oauth/jwks", () => jwksJson);

        var token = CreateValidToken();

        // Erste Validierung: fetcht Discovery + JWKS
        var result1 = await validator.ValidateTokenAsync(token, TestClientId);
        Assert.True(result1.IsValid);
        Assert.Equal(2, handler.RequestedUrls.Count); // discovery + jwks

        // Zweite Validierung: sollte Cache nutzen (keine neuen HTTP-Aufrufe)
        handler.RequestedUrls.Clear();
        var result2 = await validator.ValidateTokenAsync(token, TestClientId);
        Assert.True(result2.IsValid);
        Assert.Empty(handler.RequestedUrls); // keine HTTP-Aufrufe mehr
    }

    // ======================================================================
    // Regression tests for review fixes (#192)
    // ======================================================================

    [Fact]
    public async Task Validate_MultiScopeArray_ParsesAllScopes()
    {
        var (validator, handler) = CreateValidator();
        var discoveryJson = GetDiscoveryJson();
        var jwksJson = GetJwksJson();
        handler.AddResponse("https://login.eveonline.com/.well-known/oauth-authorization-server", () => discoveryJson);
        handler.AddResponse("https://login.eveonline.com/oauth/jwks", () => jwksJson);

        // scp als JSON-Array: ["esi-test.v1","esi-search.v1","esi-wallet.v1"]
        var multiScopeJson = @"[""esi-test.v1"",""esi-search.v1"",""esi-wallet.v1""]";
        var token = CreateSignedJwt(scopes: multiScopeJson);
        var result = await validator.ValidateTokenAsync(token, TestClientId);

        Assert.True(result.IsValid);
        Assert.NotNull(result.Payload);
        var scopes = result.Payload!.GetScopes();
        Assert.Equal(3, scopes.Count);
        Assert.Contains("esi-test.v1", scopes);
        Assert.Contains("esi-search.v1", scopes);
        Assert.Contains("esi-wallet.v1", scopes);
    }

    [Fact]
    public async Task Validate_AlgorithmNone_ReturnsInvalid()
    {
        var (validator, handler) = CreateValidator();
        var discoveryJson = GetDiscoveryJson();
        var jwksJson = GetJwksJson();
        handler.AddResponse("https://login.eveonline.com/.well-known/oauth-authorization-server", () => discoveryJson);
        handler.AddResponse("https://login.eveonline.com/oauth/jwks", () => jwksJson);

        // alg=none signiert → keine Signatur → ValidateToken wirft SecurityTokenInvalidSignatureException
        // (die Signaturvalidierung greift vor dem AlgorithmValidator)
        var token = CreateSignedJwt(algorithmOverride: SecurityAlgorithms.None);
        var result = await validator.ValidateTokenAsync(token, TestClientId);

        Assert.False(result.IsValid);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task Validate_AlgorithmHS256_ReturnsInvalid()
    {
        var (validator, handler) = CreateValidator();
        var discoveryJson = GetDiscoveryJson();
        var jwksJson = GetJwksJson();
        handler.AddResponse("https://login.eveonline.com/.well-known/oauth-authorization-server", () => discoveryJson);
        handler.AddResponse("https://login.eveonline.com/oauth/jwks", () => jwksJson);

        // HS256-Token mit symmetrischem HMAC-Key → Key nicht im JWKS → SecurityTokenSignatureKeyNotFoundException
        // (die AlgorithmValidator wird nicht erreicht, der Token wird trotzdem abgewiesen)
        var hmacKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
            System.Text.Encoding.UTF8.GetBytes("this-is-a-test-hmac-key-that-is-long-enough-for-hs256"));
        var handler2 = new JwtSecurityTokenHandler();
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim("sub", "CHARACTER:EVE:12345"),
                new Claim("scp", "esi-test.v1")
            }),
            Issuer = "login.eveonline.com",
            Expires = DateTime.UtcNow.AddHours(1),
            NotBefore = DateTime.UtcNow.AddHours(-2),
            SigningCredentials = new SigningCredentials(hmacKey, SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                ["aud"] = new[] { TestClientId, "EVE Online" }
            }
        };
        var token = handler2.WriteToken(handler2.CreateToken(descriptor));
        var result = await validator.ValidateTokenAsync(token, TestClientId);

        Assert.False(result.IsValid);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task Validate_MissingExpiration_ReturnsInvalid()
    {
        var (validator, handler) = CreateValidator();
        var discoveryJson = GetDiscoveryJson();
        var jwksJson = GetJwksJson();
        handler.AddResponse("https://login.eveonline.com/.well-known/oauth-authorization-server", () => discoveryJson);
        handler.AddResponse("https://login.eveonline.com/oauth/jwks", () => jwksJson);

        // Token manuell ohne exp-Anspruch konstruieren
        var header = "{\"alg\":\"RS256\",\"kid\":\"test-sso-key\",\"typ\":\"JWT\"}";
        var payload = "{\"sub\":\"CHARACTER:EVE:12345\",\"scp\":\"esi-test.v1\",\"iss\":\"login.eveonline.com\",\"aud\":[\"" + TestClientId + "\",\"EVE Online\"]}";
        var headerB64 = Base64UrlEncode(header);
        var payloadB64 = Base64UrlEncode(payload);

        // Mit gültigem Test-Key signieren
        var signingInput = headerB64 + "." + payloadB64;
        var signature = TestKey.SignData(System.Text.Encoding.UTF8.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var signatureB64 = Base64UrlEncode(signature);
        var token = signingInput + "." + signatureB64;

        var result = await validator.ValidateTokenAsync(token, TestClientId);

        Assert.False(result.IsValid);
        Assert.NotNull(result.Error);
        Assert.Contains("expiration", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Validate_MalformedToken_ReturnsInvalid()
    {
        var (validator, handler) = CreateValidator();
        var discoveryJson = GetDiscoveryJson();
        var jwksJson = GetJwksJson();
        handler.AddResponse("https://login.eveonline.com/.well-known/oauth-authorization-server", () => discoveryJson);
        handler.AddResponse("https://login.eveonline.com/oauth/jwks", () => jwksJson);

        var result = await validator.ValidateTokenAsync("this.is.not.a.jwt", TestClientId);

        Assert.False(result.IsValid);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task Validate_MalformedDiscoveryResponse_ReturnsInvalid()
    {
        var (validator, handler) = CreateValidator();
        handler.AddResponse("https://login.eveonline.com/.well-known/oauth-authorization-server", () => "{invalid json");
        handler.AddResponse("https://login.eveonline.com/oauth/jwks", () => GetJwksJson());

        var token = CreateValidToken();
        var result = await validator.ValidateTokenAsync(token, TestClientId);

        Assert.False(result.IsValid);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task Validate_MalformedJwksResponse_ReturnsInvalid()
    {
        var (validator, handler) = CreateValidator();
        var discoveryJson = GetDiscoveryJson();
        handler.AddResponse("https://login.eveonline.com/.well-known/oauth-authorization-server", () => discoveryJson);
        handler.AddResponse("https://login.eveonline.com/oauth/jwks", () => "{invalid json");

        var token = CreateValidToken();
        var result = await validator.ValidateTokenAsync(token, TestClientId);

        Assert.False(result.IsValid);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task Validate_SingleScope_ReturnsSingleScope()
    {
        var (validator, handler) = CreateValidator();
        var discoveryJson = GetDiscoveryJson();
        var jwksJson = GetJwksJson();
        handler.AddResponse("https://login.eveonline.com/.well-known/oauth-authorization-server", () => discoveryJson);
        handler.AddResponse("https://login.eveonline.com/oauth/jwks", () => jwksJson);

        var token = CreateSignedJwt(scopes: "esi-test.v1");
        var result = await validator.ValidateTokenAsync(token, TestClientId);

        Assert.True(result.IsValid);
        var scopes = result.Payload!.GetScopes();
        Assert.Single(scopes);
        Assert.Equal("esi-test.v1", scopes[0]);
    }
}