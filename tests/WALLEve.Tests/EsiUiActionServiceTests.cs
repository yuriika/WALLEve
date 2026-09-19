using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WALLEve.Configuration;
using WALLEve.Models.Authentication;
using WALLEve.Services.Authentication.Interfaces;
using WALLEve.Services.Esi;
using WALLEve.Services.Esi.Interfaces;

namespace WALLEve.Tests;

/// <summary>
/// Tests der EVE-UI-Hilfsaktionen (Issue #66): Marktdetails öffnen und Wegpunkt setzen
/// laufen ausschließlich über die erlaubten ESI-UI-Endpunkte mit Scope
/// esi-ui.open_window.v1. Ohne gültigen Charakter bzw. ohne autorisierten Scope wird
/// KEIN HTTP-Call abgesetzt und der fehlende Berechtigungszustand sichtbar geliefert
/// (NotAuthenticated/MissingScope). Fehler (403/5xx/Netz) werden als Ergebnis geliefert,
/// nie geworfen — eine abgelehnte Hilfsaktion verändert keine Empfehlung. Keine Live-ESI.
/// </summary>
public class EsiUiActionServiceTests
{
    private const string BaseUrl = "https://esi.local/latest";
    private const string UiScope = "esi-ui.open_window.v1";

    // ------------------------------------------------------------------
    // Fake-HTTP-Infrastruktur
    // ------------------------------------------------------------------

    private sealed class CapturedRequest
    {
        public string? PathAndQuery { get; init; }
        public string? Method { get; init; }
        public string? Authorization { get; init; }
        public string? Body { get; init; }
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;
        private readonly List<CapturedRequest> _requests = new();
        private int _requestCount;

        public IReadOnlyList<CapturedRequest> Requests => _requests;
        public int RequestCount => Volatile.Read(ref _requestCount);

        public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
            => _handler = handler;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            lock (_requests)
            {
                _requests.Add(new CapturedRequest
                {
                    PathAndQuery = request.RequestUri?.PathAndQuery,
                    Method = request.Method.Method,
                    Authorization = request.Headers.Authorization?.ToString(),
                    Body = body
                });
            }
            return await _handler(request, cancellationToken);
        }
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name)
            => new(_handler) { BaseAddress = new Uri("https://esi.local") };
    }

    private sealed class StubAuthService : IEveAuthenticationService
    {
        public EveAuthState? State { get; set; }

        public Task<EveAuthState?> GetAuthStateAsync() => Task.FromResult(State);

        public Task<bool> IsAuthenticatedAsync() => Task.FromResult(State?.IsValid == true);

        public string GetLoginUrl() => "http://login";

        public Task<bool> HandleCallbackAsync(string code, string state) => Task.FromResult(true);

        public Task<string?> GetAccessTokenAsync() => Task.FromResult<string?>("test-token");

        public Task LogoutAsync() => Task.CompletedTask;

        public Task<List<KnownCharacter>> GetAllCharactersAsync() => Task.FromResult(new List<KnownCharacter>());

        public Task<bool> ForceRefreshAccessTokenAsync() => Task.FromResult(State?.IsValid == true);

        public Task<bool> SwitchCharacterAsync(int characterId) => Task.FromResult(true);

        event EventHandler<bool>? IEveAuthenticationService.AuthenticationStateChanged
        {
            add { }
            remove { }
        }
    }

    private static EveAuthState ValidAuth(params string[] scopes) => new()
    {
        AccessToken = "tok",
        RefreshToken = "ref",
        CharacterId = 90073315,
        CharacterName = "Test",
        Scopes = scopes.ToList()
    };

    private static IEsiUiActionService CreateService(
        StubAuthService auth,
        StubHttpMessageHandler handler)
    {
        var factory = new StubHttpClientFactory(handler);
        var settings = Options.Create(new EveOnlineSettings { EsiBaseUrl = BaseUrl });
        return new EsiUiActionService(settings, auth, factory, NullLogger<EsiUiActionService>.Instance);
    }

    private static HttpResponseMessage EmptyOk() => new(HttpStatusCode.NoContent);

    // ------------------------------------------------------------------
    // Berechtigungs-/Authentifizierungs-Gates (kein HTTP-Call)
    // ------------------------------------------------------------------

    [Fact]
    public async Task OpenMarketDetails_NotAuthenticated_ReturnsNotAuthenticated_NoHttpCall()
    {
        var auth = new StubAuthService { State = null };
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(EmptyOk()));
        var service = CreateService(auth, handler);

        var result = await service.OpenMarketDetailsAsync(1234);

        Assert.Equal(EsiUiActionResultStatus.NotAuthenticated, result.Status);
        Assert.False(result.IsSuccess);
        Assert.Contains("angemeldeten Charakter", result.Message);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task OpenMarketDetails_MissingScope_ReturnsMissingScope_NoHttpCall()
    {
        var auth = new StubAuthService { State = ValidAuth("esi-markets.read_character_orders.v1") };
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(EmptyOk()));
        var service = CreateService(auth, handler);

        var result = await service.OpenMarketDetailsAsync(1234);

        Assert.Equal(EsiUiActionResultStatus.MissingScope, result.Status);
        Assert.Contains(UiScope, result.Message);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task SetWaypoint_MissingScope_ReturnsMissingScope_NoHttpCall()
    {
        var auth = new StubAuthService { State = ValidAuth() };
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(EmptyOk()));
        var service = CreateService(auth, handler);

        var result = await service.SetWaypointAsync(60003760);

        Assert.Equal(EsiUiActionResultStatus.MissingScope, result.Status);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task SetWaypoint_NotAuthenticated_ReturnsNotAuthenticated_NoHttpCall()
    {
        var auth = new StubAuthService { State = new EveAuthState() }; // leere Tokens => IsValid false
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(EmptyOk()));
        var service = CreateService(auth, handler);

        var result = await service.SetWaypointAsync(60003760);

        Assert.Equal(EsiUiActionResultStatus.NotAuthenticated, result.Status);
        Assert.Equal(0, handler.RequestCount);
    }

    // ------------------------------------------------------------------
    // Erfolgspfad: Endpunkt, Scope-Bearer, Body
    // ------------------------------------------------------------------

    [Fact]
    public async Task OpenMarketDetails_Success_PostsMarketDetailsWithTypeIdAndBearer()
    {
        var auth = new StubAuthService { State = ValidAuth(UiScope) };
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(EmptyOk()));
        var service = CreateService(auth, handler);

        var result = await service.OpenMarketDetailsAsync(1234);

        Assert.Equal(EsiUiActionResultStatus.Success, result.Status);
        Assert.True(result.IsSuccess);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Contains("/ui/openwindow/marketdetails/", request.PathAndQuery);
        Assert.Contains("type_id=1234", request.PathAndQuery);
        Assert.Equal("Bearer test-token", request.Authorization);
    }

    [Fact]
    public async Task SetWaypoint_Success_PostsJsonBodyWithDestination()
    {
        var auth = new StubAuthService { State = ValidAuth(UiScope) };
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(EmptyOk()));
        var service = CreateService(auth, handler);

        var result = await service.SetWaypointAsync(destinationId: 60003760, clearOtherWaypoints: true);

        Assert.Equal(EsiUiActionResultStatus.Success, result.Status);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Contains("/ui/openwindow/waypoint/", request.PathAndQuery);
        Assert.Equal("Bearer test-token", request.Authorization);

        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal(60003760, body.RootElement.GetProperty("destination_id").GetInt64());
        Assert.True(body.RootElement.GetProperty("clear_other_waypoints").GetBoolean());
        Assert.False(body.RootElement.GetProperty("add_to_beginning").GetBoolean());
    }

    // ------------------------------------------------------------------
    // Fehlerpfad: sichtbare Fehler, kein Wurf, keine Zustandsänderung
    // ------------------------------------------------------------------

    [Fact]
    public async Task OpenMarketDetails_Forbidden_ReturnsErrorWithHint()
    {
        var auth = new StubAuthService { State = ValidAuth(UiScope) };
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)));
        var service = CreateService(auth, handler);

        var result = await service.OpenMarketDetailsAsync(1234);

        Assert.Equal(EsiUiActionResultStatus.Error, result.Status);
        Assert.Contains("403", result.Message);
    }

    [Fact]
    public async Task SetWaypoint_ServerError_ReturnsError()
    {
        var auth = new StubAuthService { State = ValidAuth(UiScope) };
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        var service = CreateService(auth, handler);

        var result = await service.SetWaypointAsync(60003760);

        Assert.Equal(EsiUiActionResultStatus.Error, result.Status);
        Assert.Contains("500", result.Message);
    }

    [Fact]
    public async Task OpenMarketDetails_NetworkFailure_ReturnsErrorNeverThrows()
    {
        var auth = new StubAuthService { State = ValidAuth(UiScope) };
        var handler = new StubHttpMessageHandler((_, _) => throw new HttpRequestException("connection refused"));
        var service = CreateService(auth, handler);

        var result = await service.OpenMarketDetailsAsync(1234);

        Assert.Equal(EsiUiActionResultStatus.Error, result.Status);
        Assert.Contains("connection refused", result.Message);
    }
}