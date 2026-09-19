using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WALLEve.Configuration;
using WALLEve.Models.Authentication;
using WALLEve.Services.Authentication;
using WALLEve.Services.Authentication.Interfaces;

namespace WALLEve.Tests;

/// <summary>
/// Regressionstests für den SSO-Refresh-Pfad (Issue #196): Das neue Access-Token
/// wird genau wie beim initialen Login gegen die JWKS validiert, BEVOR es
/// persistiert wird. Ein ungültiges/vertauschtes Refresh-Token darf den
/// gespeicherten Auth-Zustand nicht verändern.
/// </summary>
public class RefreshTokenValidationTests
{
    private static readonly DateTime Past = DateTime.UtcNow.AddHours(-1);

    private const string SsoTokenUrl = "https://login.eveonline.com/v2/oauth/token";

    /// <summary>Token-Storage, der einen abgelaufenen Zustand hält und Save-Aufrufe aufzeichnet.</summary>
    private sealed class RecordingTokenStorage : ITokenStorageService
    {
        public EveAuthState State { get; set; } = new();
        public int SaveCount { get; private set; }
        public EveAuthState? LastSaved { get; private set; }
        public int StorePkceCount { get; private set; }

        public Task SaveAuthStateAsync(EveAuthState state)
        {
            SaveCount++;
            LastSaved = state;
            // Der echte Storage würde das Mut-Objekt nach dem Save übernehmen.
            return Task.CompletedTask;
        }

        public Task<EveAuthState?> GetAuthStateAsync() => Task.FromResult<EveAuthState?>(State);

        public Task<List<KnownCharacter>> GetAllCharactersAsync() => Task.FromResult(new List<KnownCharacter>());

        public Task SetActiveCharacterAsync(int characterId) => Task.CompletedTask;

        public Task<bool> RemoveCharacterAsync(int characterId) => Task.FromResult(false);

        public Task ClearAuthStateAsync() => Task.CompletedTask;

        public void StorePkceChallenge(PkceChallenge challenge) => StorePkceCount++;

        public PkceChallenge? GetAndClearPkceChallenge(string state) => null;
    }

    /// <summary>Konfigurierbarer JWT-Validator (gültig/ungültig) mit Aufruf-Zähler.</summary>
    private sealed class ConfigurableJwtValidator : IJwtTokenValidator
    {
        public bool ResultValid { get; set; } = true;
        public int ValidateCount { get; private set; }
        public string? LastAccessToken { get; private set; }

        public Task<JwtValidationResult> ValidateTokenAsync(string accessToken, string expectedClientId)
        {
            ValidateCount++;
            LastAccessToken = accessToken;
            return Task.FromResult(ResultValid
                ? JwtValidationResult.Valid(new EveJwtPayload
                {
                    Subject = "CHARACTER:EVE:12345",
                    Name = "Test Character",
                    Issuer = "login.eveonline.com",
                    Expiration = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
                    Scopes = new List<string> { "esi-new.scope.v1" }
                })
                : JwtValidationResult.Invalid("signature mismatch"));
        }
    }

    /// <summary>Stub-HTTP-Handler, der die Token-Endpunkt-Antwort liefert und POSTs aufzeichnet.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _responseJson;
        public int PostCount { get; private set; }

        public StubHandler(string responseJson) => _responseJson = responseJson;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post && request.RequestUri?.ToString() == SsoTokenUrl)
            {
                PostCount++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_responseJson, Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler);
    }

    private static string SuccessTokenJson(string accessToken = "new-access-token",
        string refreshToken = "new-refresh-token", int expiresIn = 1200)
        => $"{{\"access_token\":\"{accessToken}\",\"token_type\":\"Bearer\",\"expires_in\":{expiresIn},\"refresh_token\":\"{refreshToken}\"}}";

    private static EveAuthenticationService CreateService(
        RecordingTokenStorage storage, ConfigurableJwtValidator validator, StubHandler handler)
        => new(
            Options.Create(new EveOnlineSettings { ClientId = "client" }),
            storage,
            new StubHttpClientFactory(handler),
            validator,
            NullLogger<EveAuthenticationService>.Instance);

    private static EveAuthState CreateExpiredState() => new()
    {
        AccessToken = "old-access-token",
        RefreshToken = "old-refresh-token",
        ExpiresAt = Past,
        CharacterId = 12345,
        CharacterName = "Test Character",
        Scopes = new List<string> { "esi-old.scope.v1" }
    };

    [Fact]
    public async Task GetAccessToken_RefreshReturnsInvalidJwt_DoesNotPersistNewTokens()
    {
        var storage = new RecordingTokenStorage { State = CreateExpiredState() };
        var validator = new ConfigurableJwtValidator { ResultValid = false };
        var handler = new StubHandler(SuccessTokenJson("new-access-token", "new-refresh-token"));

        var service = CreateService(storage, validator, handler);
        var result = await service.GetAccessTokenAsync();

        // Ungültiges JWT → Refresh schlägt fehl → kein neues Token für den Aufrufer.
        Assert.Null(result);
        Assert.Equal(1, handler.PostCount);          // Refresh wurde angefragt
        Assert.Equal(1, validator.ValidateCount);    // Validator wurde im Refresh aufgerufen
        Assert.Equal("new-access-token", validator.LastAccessToken);
        Assert.Equal(0, storage.SaveCount);          // Nichts persistiert
    }

    [Fact]
    public async Task GetAccessToken_RefreshReturnsValidJwt_PersistsNewState()
    {
        var storage = new RecordingTokenStorage { State = CreateExpiredState() };
        var validator = new ConfigurableJwtValidator { ResultValid = true };
        var handler = new StubHandler(SuccessTokenJson("new-access-token", "new-refresh-token"));

        var service = CreateService(storage, validator, handler);
        var result = await service.GetAccessTokenAsync();

        Assert.NotNull(result);
        Assert.Equal("new-access-token", result);
        Assert.Equal(1, handler.PostCount);
        Assert.Equal(1, validator.ValidateCount);
        Assert.Equal(1, storage.SaveCount);
        Assert.Equal("new-access-token", storage.LastSaved!.AccessToken);
        Assert.Equal("new-refresh-token", storage.LastSaved.RefreshToken);
        Assert.NotEqual(Past, storage.LastSaved.ExpiresAt);
        Assert.Contains("esi-new.scope.v1", storage.LastSaved.Scopes); // Scopes aus geprüftem JWT übernommen
    }

    [Fact]
    public async Task GetAccessToken_NotExpired_DoesNotCallRefreshOrValidator()
    {
        var storage = new RecordingTokenStorage
        {
            State = new EveAuthState
            {
                AccessToken = "current-token",
                RefreshToken = "current-refresh",
                ExpiresAt = DateTime.UtcNow.AddHours(1), // noch gültig
                CharacterId = 12345,
                CharacterName = "Test Character"
            }
        };
        var validator = new ConfigurableJwtValidator { ResultValid = true };
        var handler = new StubHandler(SuccessTokenJson());

        var service = CreateService(storage, validator, handler);
        var result = await service.GetAccessTokenAsync();

        Assert.Equal("current-token", result);
        Assert.Equal(0, handler.PostCount);      // kein Refresh nötig
        Assert.Equal(0, validator.ValidateCount); // kein JWT-Check
        Assert.Equal(0, storage.SaveCount);
    }
}