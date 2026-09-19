using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.WebUtilities;
using WALLEve.Configuration;
using WALLEve.Models.Authentication;
using WALLEve.Services.Authentication;
using WALLEve.Services.Authentication.Interfaces;

namespace WALLEve.Tests;

public class EveOnlineSettingsTests
{
    private const string BlueprintScope = "esi-characters.read_blueprints.v1";

    private sealed class StubTokenStorage : ITokenStorageService
    {
        public PkceChallenge? StoredChallenge { get; private set; }

        public Task SaveAuthStateAsync(EveAuthState state) => Task.CompletedTask;
        public Task<EveAuthState?> GetAuthStateAsync() => Task.FromResult<EveAuthState?>(null);
        public Task<List<KnownCharacter>> GetAllCharactersAsync() => Task.FromResult(new List<KnownCharacter>());
        public Task SetActiveCharacterAsync(int characterId) => Task.CompletedTask;
        public Task<bool> RemoveCharacterAsync(int characterId) => Task.FromResult(false);
        public Task ClearAuthStateAsync() => Task.CompletedTask;
        public void StorePkceChallenge(PkceChallenge challenge) => StoredChallenge = challenge;
        public PkceChallenge? GetAndClearPkceChallenge(string state) => null;
    }

    private sealed class UnusedHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new NotSupportedException();
    }

    [Fact]
    public void DefaultScopes_ContainBlueprintReadScope()
    {
        var settings = new EveOnlineSettings();

        Assert.Contains(BlueprintScope, settings.Scopes);
    }

    [Fact]
    public void CommittedAppSettings_ContainBlueprintReadScope()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var config = new ConfigurationBuilder()
            .SetBasePath(repoRoot)
            .AddJsonFile("appsettings.json")
            .Build();
        var settings = config.GetSection("EveOnline").Get<EveOnlineSettings>() ?? new();

        Assert.Contains(BlueprintScope, settings.Scopes);
    }

    [Fact]
    public void LoginUrl_RequestsBlueprintReadScope()
    {
        var tokenStorage = new StubTokenStorage();
        var service = new EveAuthenticationService(
            Options.Create(new EveOnlineSettings { ClientId = "client" }),
            tokenStorage,
            new UnusedHttpClientFactory(),
            NullLogger<EveAuthenticationService>.Instance);

        var query = QueryHelpers.ParseQuery(new Uri(service.GetLoginUrl()).Query);
        var requestedScopes = query["scope"].ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        Assert.Contains(BlueprintScope, requestedScopes);
        Assert.NotNull(tokenStorage.StoredChallenge);
    }
}