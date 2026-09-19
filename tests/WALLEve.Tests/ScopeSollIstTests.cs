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
/// Regressionstests für den Soll/Ist-Abgleich autorisierter Scopes beim Login
/// (Issue #197). Ein fehlender Scope macht den Login NICHT ungültig, sondern wird
/// als <see cref="EveAuthState.MissingScopes"/> festgehalten und geloggt.
/// </summary>
public class ScopeSollIstTests
{
    private static readonly string[] DefaultScopes =
    {
        "esi-characters.read_standings.v1",
        "esi-skills.read_skills.v1",
        "esi-skills.read_skillqueue.v1",
        "esi-wallet.read_character_wallet.v1",
        "esi-location.read_location.v1",
        "esi-location.read_online.v1",
        "esi-location.read_ship_type.v1",
        "esi-markets.read_character_orders.v1",
        "esi-assets.read_assets.v1",
        "esi-universe.read_structures.v1",
        "esi-characters.read_blueprints.v1"
    };

    private sealed class CallbackTokenStorage : ITokenStorageService
    {
        public PkceChallenge Challenge { get; set; } =
            new() { CodeVerifier = "verifier", CodeChallenge = "challenge", State = "state123" };
        public EveAuthState? Saved { get; private set; }

        public Task SaveAuthStateAsync(EveAuthState state) { Saved = state; return Task.CompletedTask; }
        public Task<EveAuthState?> GetAuthStateAsync() => Task.FromResult<EveAuthState?>(Saved);
        public Task<List<KnownCharacter>> GetAllCharactersAsync() => Task.FromResult(new List<KnownCharacter>());
        public Task SetActiveCharacterAsync(int characterId) => Task.CompletedTask;
        public Task<bool> RemoveCharacterAsync(int characterId) => Task.FromResult(false);
        public Task ClearAuthStateAsync() => Task.CompletedTask;
        public void StorePkceChallenge(PkceChallenge challenge) => Challenge = challenge;
        public PkceChallenge? GetAndClearPkceChallenge(string state)
            => state == Challenge.State ? Challenge : null;
    }

    /// <summary>Konfigurierbarer JWT-Validator mit den zugestandenen Scopes.</summary>
    private sealed class GrantsScopeValidator : IJwtTokenValidator
    {
        public List<string> GrantedScopes { get; set; } = new();

        public Task<JwtValidationResult> ValidateTokenAsync(string accessToken, string expectedClientId)
            => Task.FromResult(JwtValidationResult.Valid(new EveJwtPayload
            {
                Subject = "CHARACTER:EVE:12345",
                Name = "Test Character",
                Issuer = "login.eveonline.com",
                Expiration = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
                Scopes = GrantedScopes
            }));
    }

    private sealed class TokenEndpointHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"access_token\":\"tok\",\"token_type\":\"Bearer\",\"expires_in\":1200,\"refresh_token\":\"rt\"}",
                    Encoding.UTF8, "application/json")
            });
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler);
    }

    private static async Task<EveAuthState?> RunCallbackAsync(List<string> grantedScopes)
    {
        var storage = new CallbackTokenStorage();
        var service = new EveAuthenticationService(
            Options.Create(new EveOnlineSettings { ClientId = "client", Scopes = DefaultScopes.ToList() }),
            storage,
            new StubHttpClientFactory(new TokenEndpointHandler()),
            new GrantsScopeValidator { GrantedScopes = grantedScopes },
            NullLogger<EveAuthenticationService>.Instance);

        var ok = await service.HandleCallbackAsync("code", "state123");
        Assert.True(ok, "Login muss auch mit fehlenden Scopes erfolgreich sein.");
        return storage.Saved;
    }

    [Fact]
    public async Task Callback_AllRequiredScopes_NoMissing()
    {
        var saved = await RunCallbackAsync(DefaultScopes.ToList());

        Assert.Empty(saved!.MissingScopes);
        Assert.Equal(DefaultScopes.Length, saved.Scopes.Count);
    }

    [Fact]
    public async Task Callback_OneMissingScope_FlaggedExactly()
    {
        // Alle außer read_blueprints
        var granted = DefaultScopes.Where(s => s != "esi-characters.read_blueprints.v1").ToList();
        var saved = await RunCallbackAsync(granted);

        Assert.Single(saved!.MissingScopes);
        Assert.Equal("esi-characters.read_blueprints.v1", saved.MissingScopes[0]);
    }

    [Fact]
    public async Task Callback_ScopeCaseSensitive_FlagsCaseMismatch()
    {
        // read_blueprints mit falscher Großschreibung → gilt als fehlend
        var granted = DefaultScopes.Where(s => s != "esi-characters.read_blueprints.v1")
            .Append("esi-characters.READ_BLUEPRINTS.v1")
            .ToList();
        var saved = await RunCallbackAsync(granted);

        Assert.Contains("esi-characters.read_blueprints.v1", saved!.MissingScopes);
    }

    [Fact]
    public async Task Callback_AllScopesMissing_AllFlaggedDistinct()
    {
        var saved = await RunCallbackAsync(new List<string>());

        Assert.Equal(DefaultScopes.Length, saved!.MissingScopes.Count);
        Assert.Equal(DefaultScopes.Length, saved.MissingScopes.Distinct().Count());
    }
}