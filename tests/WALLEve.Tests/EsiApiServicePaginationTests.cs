using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WALLEve.Configuration;
using WALLEve.Models.Authentication;
using WALLEve.Models.Esi;
using WALLEve.Models.Esi.Character;
using WALLEve.Models.Esi.Markets;
using WALLEve.Models.Esi.Wallet;
using WALLEve.Services.Authentication.Interfaces;
using WALLEve.Services.Esi;
using WALLEve.Services.Esi.Interfaces;

namespace WALLEve.Tests;

/// <summary>
/// Fake-HTTP-Tests für die atomare Veröffentlichung mehrseitiger ESI-Abrufe
/// (Issue #25): EsiApiService läuft über den ECHTEN Transportpfad mit
/// gestubbtem HttpMessageHandler. Fehler auf erster/mittlerer/letzter Seite
/// und Cancellation publizieren keine Teildaten (null); gültig leere
/// Gesamtergebnisse sind zulässig (leere Liste). Keine Live-ESI.
/// </summary>
public class EsiApiServicePaginationTests
{
    private const int RegionId = 10000002;
    private const int CharacterId = 90073315;
    private const string BaseUrl = "https://esi.local";

    // ------------------------------------------------------------------
    // Fake-HTTP-Infrastruktur
    // ------------------------------------------------------------------

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;
        private readonly List<string> _requestedUrls = new();
        private int _requestCount;

        public IReadOnlyList<string> RequestedUrls => _requestedUrls;
        public int RequestCount => Volatile.Read(ref _requestCount);

        public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
            => _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            lock (_requestedUrls)
            {
                _requestedUrls.Add(request.RequestUri?.PathAndQuery ?? "?");
            }
            return _handler(request, cancellationToken);
        }
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name)
            => new(_handler) { BaseAddress = new Uri(BaseUrl) };
    }

    private sealed class StubAuthService : IEveAuthenticationService
    {
        public Task<EveAuthState?> GetAuthStateAsync()
            => Task.FromResult<EveAuthState?>(new EveAuthState
            {
                AccessToken = "tok", RefreshToken = "ref",
                CharacterId = CharacterId, CharacterName = "Test"
            });

        public Task<bool> IsAuthenticatedAsync() => Task.FromResult(true);
        public string GetLoginUrl() => "http://login";
        public Task<bool> HandleCallbackAsync(string code, string state) => Task.FromResult(true);
        public Task<string?> GetAccessTokenAsync() => Task.FromResult<string?>("tok");
        public Task LogoutAsync() => Task.CompletedTask;
        public Task<List<KnownCharacter>> GetAllCharactersAsync() => Task.FromResult(new List<KnownCharacter>());
        public Task<bool> SwitchCharacterAsync(int characterId) => Task.FromResult(true);

        event EventHandler<bool>? IEveAuthenticationService.AuthenticationStateChanged
        {
            add { }
            remove { }
        }
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json, int? totalPages = null)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        if (totalPages.HasValue)
        {
            response.Headers.Add("X-Pages", totalPages.Value.ToString());
        }
        return response;
    }

    private static HttpResponseMessage Error(HttpStatusCode status)
        => new(status) { Content = new StringContent("{\"error\":\"boom\"}", Encoding.UTF8, "application/json") };

    private static (EsiApiService Service, EsiCacheService Cache, StubHttpMessageHandler Handler) CreateService(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    {
        var httpHandler = new StubHttpMessageHandler(handler);
        var factory = new StubHttpClientFactory(httpHandler);
        var cache = new EsiCacheService(NullLogger<EsiCacheService>.Instance);
        var settings = Options.Create(new EveOnlineSettings { EsiBaseUrl = BaseUrl });
        var appSettings = Options.Create(new ApplicationSettings());
        var service = new EsiApiService(settings, appSettings, new StubAuthService(), factory, cache,
            NullLogger<EsiApiService>.Instance);
        return (service, cache, httpHandler);
    }

    private static RegionalMarketOrder Order(long id) => new()
    {
        OrderId = id, TypeId = 34, LocationId = 60003760, SystemId = 30000142,
        Price = 100, VolumeTotal = 1, VolumeRemain = 1, MinVolume = 1,
        Duration = 90, IsBuyOrder = false, Issued = DateTime.UtcNow, Range = "region"
    };

    private static WalletJournalEntry JournalEntry(long id) => new()
    {
        Id = id, Date = DateTime.UtcNow, RefType = "buy", Description = $"entry {id}"
    };

    private static string RegionalUrl(int page)
        => $"/markets/{RegionId}/orders/?order_type=all&page={page}";

    private static string JournalUrl(int page)
        => $"/characters/{CharacterId}/wallet/journal/?page={page}";

    private static string Serialize<T>(IEnumerable<T> items) => JsonSerializer.Serialize(items);

    // ------------------------------------------------------------------
    // Erfolg: alle Seiten, genau einmal
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetAllRegionalMarketOrders_Success_MergesAllPagesExactlyOnce()
    {
        var page2Items = new[] { Order(4), Order(5) };
        var page3Items = new[] { Order(6) };

        var (service, _, handler) = CreateService((request, _) =>
        {
            var url = request.RequestUri?.PathAndQuery;
            if (url == RegionalUrl(1))
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, Serialize(new[] { Order(1), Order(2), Order(3) }), totalPages: 3));
            if (url == RegionalUrl(2))
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, Serialize(page2Items)));
            if (url == RegionalUrl(3))
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, Serialize(page3Items)));
            return Task.FromResult(Error(HttpStatusCode.NotFound));
        });

        var result = await service.GetAllRegionalMarketOrdersAsync(RegionId);

        Assert.NotNull(result);
        Assert.Equal(6, result!.Count);
        Assert.Equal(6, result.Select(o => o.OrderId).Distinct().Count());
        Assert.Equal(3, handler.RequestCount);
    }

    // ------------------------------------------------------------------
    // Fehler behalten alte Daten: Fehler auf erster/mittlerer/letzter Seite
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetAllRegionalMarketOrders_FirstPageFails_ReturnsNull()
    {
        var (service, _, _) = CreateService((_, _) => Task.FromResult(Error(HttpStatusCode.InternalServerError)));

        var result = await service.GetAllRegionalMarketOrdersAsync(RegionId);

        Assert.Null(result); // Aufrufer behält alten Snapshot — keine leere Liste als Ersatz
    }

    [Fact]
    public async Task GetAllRegionalMarketOrders_MiddlePageFails_ReturnsNull_NoPartialLeak()
    {
        var (service, _, _) = CreateService((request, _) =>
        {
            var url = request.RequestUri?.PathAndQuery;
            if (url == RegionalUrl(1))
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, Serialize(new[] { Order(1), Order(2) }), totalPages: 3));
            if (url == RegionalUrl(2))
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, Serialize(new[] { Order(3), Order(4) })));
            return Task.FromResult(Error(HttpStatusCode.InternalServerError)); // Seite 3
        });

        var result = await service.GetAllRegionalMarketOrdersAsync(RegionId);

        Assert.Null(result); // Seiten 1+2 wurden NICHT als Teilmenge veröffentlicht
    }

    [Fact]
    public async Task GetAllRegionalMarketOrders_LastPageFails_ReturnsNull()
    {
        var (service, _, _) = CreateService((request, _) =>
        {
            var url = request.RequestUri?.PathAndQuery;
            if (url == RegionalUrl(1))
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, Serialize(new[] { Order(1) }), totalPages: 2));
            return Task.FromResult(Error(HttpStatusCode.InternalServerError)); // Seite 2 (letzte)
        });

        var result = await service.GetAllRegionalMarketOrdersAsync(RegionId);

        Assert.Null(result);
    }

    // ------------------------------------------------------------------
    // Erfolgreiches Wiederholen ersetzt Daten genau einmal
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetAllRegionalMarketOrders_FailedThenRetry_ReturnsFullDataExactlyOnce()
    {
        var page2Fails = true;

        var (service, _, _) = CreateService((request, _) =>
        {
            var url = request.RequestUri?.PathAndQuery;
            if (url == RegionalUrl(1))
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, Serialize(new[] { Order(1), Order(2) }), totalPages: 2));
            if (url == RegionalUrl(2))
            {
                if (page2Fails)
                {
                    page2Fails = false;
                    return Task.FromResult(Error(HttpStatusCode.InternalServerError));
                }
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, Serialize(new[] { Order(3), Order(4) })));
            }
            return Task.FromResult(Error(HttpStatusCode.NotFound));
        });

        var firstAttempt = await service.GetAllRegionalMarketOrdersAsync(RegionId);
        Assert.Null(firstAttempt); // Fehler → keine Daten publiziert

        var retry = await service.GetAllRegionalMarketOrdersAsync(RegionId);
        Assert.NotNull(retry);
        Assert.Equal(4, retry!.Count);
        Assert.Equal(4, retry.Select(o => o.OrderId).Distinct().Count()); // genau einmal, keine Duplikate
    }

    // ------------------------------------------------------------------
    // Gültig leeres Gesamtergebnis ist zulässig
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetAllRegionalMarketOrders_EmptyFirstPage_ReturnsEmptyList_NotNull()
    {
        var (service, _, _) = CreateService((request, _) =>
        {
            Assert.StartsWith(RegionalUrl(1), request.RequestUri?.PathAndQuery);
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, Serialize(Array.Empty<RegionalMarketOrder>())));
        });

        var result = await service.GetAllRegionalMarketOrdersAsync(RegionId);

        Assert.NotNull(result);
        Assert.Empty(result!);
    }

    // ------------------------------------------------------------------
    // 304/Auth/Rate-Limit/5xx/ungültiges JSON im echten Transportpfad
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetAllRegionalMarketOrders_RateLimitOnPage2_ReturnsNull()
    {
        var (service, _, _) = CreateService((request, _) =>
        {
            var url = request.RequestUri?.PathAndQuery;
            if (url == RegionalUrl(1))
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, Serialize(new[] { Order(1) }), totalPages: 2));
            return Task.FromResult(Error(HttpStatusCode.TooManyRequests)); // 429
        });

        var result = await service.GetAllRegionalMarketOrdersAsync(RegionId);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetWalletJournal_Unauthorized_ReturnsNull()
    {
        var (service, _, _) = CreateService((request, _) =>
        {
            Assert.NotNull(request.Headers.Authorization); // authentifizierter Pfad
            return Task.FromResult(Error(HttpStatusCode.Unauthorized)); // 401
        });

        var result = await service.GetAllWalletJournalPagesAsync(CharacterId);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetWalletJournal_InvalidJson_ReturnsNull_NotCompleteEmpty()
    {
        var (service, _, _) = CreateService((request, _) =>
        {
            Assert.Equal(JournalUrl(1), request.RequestUri?.PathAndQuery);
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, "this is not json"));
        });

        var result = await service.GetAllWalletJournalPagesAsync(CharacterId);

        // Malformed-Payload ist ein Fehler, kein gültig leeres Ergebnis
        Assert.Null(result);
    }

    [Fact]
    public async Task GetWalletJournal_NotModified_WithCache_ReturnsCachedData()
    {
        var cachedEntries = new List<WalletJournalEntry> { JournalEntry(11), JournalEntry(12) };

        var (service, cache, _) = CreateService((request, _) =>
        {
            Assert.True(request.Headers.IfNoneMatch.Count > 0, "If-None-Match muss bei Cache-Eintrag gesendet werden");
            return Task.FromResult(JsonResponse(HttpStatusCode.NotModified, string.Empty));
        });

        // Cache mit ETag + Daten vorfüllen (erfolgreicher Vorlauf); ETags sind quoted
        cache.Set(JournalUrl(1), "\"v1\"", cachedEntries, DateTime.UtcNow.AddHours(1));

        var result = await service.GetAllWalletJournalPagesAsync(CharacterId);

        Assert.NotNull(result);
        Assert.Equal(new[] { 11L, 12L }, result!.Select(e => e.Id));
    }

    [Fact]
    public async Task GetWalletJournal_FailedResponse_IsNotCachedAsSuccess()
    {
        var page1Url = JournalUrl(1);
        var (service, cache, _) = CreateService((_, _) => Task.FromResult(Error(HttpStatusCode.InternalServerError)));

        var result = await service.GetAllWalletJournalPagesAsync(CharacterId);

        Assert.Null(result);
        Assert.Null(cache.Get<List<WalletJournalEntry>>(page1Url)); // Fehler wurde NICHT gecacht
    }

    // ------------------------------------------------------------------
    // Cancellation publiziert keine Teilmenge
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetAllRegionalMarketOrders_PreCancelled_ReturnsNull()
    {
        var (service, _, _) = CreateService((_, _) => Task.FromResult(Error(HttpStatusCode.InternalServerError)));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await service.GetAllRegionalMarketOrdersAsync(RegionId, ct: cts.Token);

        // Keine (Teil-)Daten publizieren; ob HttpClient die Request noch an den
        // Handler sendet, bevor die Cancellation greift, ist Implementierungsdetail.
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAllRegionalMarketOrders_CancelledMidFetch_ReturnsNull_NoPartialPublished()
    {
        // Seite 2 hängt, bis das CancellationToken feuert; Seite 3 wird nie angefragt
        var (service, _, handler) = CreateService((request, ct) =>
        {
            var url = request.RequestUri?.PathAndQuery;
            if (url == RegionalUrl(1))
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, Serialize(new[] { Order(1) }), totalPages: 3));
            if (url == RegionalUrl(2))
                return Task.Delay(Timeout.InfiniteTimeSpan, ct).ContinueWith(_ => (HttpResponseMessage)null!);
            return Task.FromResult(Error(HttpStatusCode.NotFound)); // Seite 3: darf nicht erreicht werden
        });

        using var cts = new CancellationTokenSource();
        var fetchTask = service.GetAllRegionalMarketOrdersAsync(RegionId, ct: cts.Token);
        await Task.Delay(150);
        cts.Cancel();

        var result = await fetchTask;

        Assert.Null(result); // keine Teilmenge publiziert
        Assert.DoesNotContain(handler.RequestedUrls, url => url == RegionalUrl(3));
    }

    // ------------------------------------------------------------------
    // Assets: Owner wird verlustfrei an jede Rohzeile geheftet (Issue #27)
    // ------------------------------------------------------------------

    private static string AssetsUrl(int page)
        => $"/characters/{CharacterId}/assets/?page={page}";

    [Fact]
    public async Task GetCharacterAssets_StampsOwnerCharacterId_OnEveryAsset()
    {
        var asset = new
        {
            item_id = 1001L,
            type_id = 1234,
            quantity = 5,
            location_id = 60003466L,
            location_type = "station",
            location_flag = "Hangar",
            is_singleton = false
        };
        var (service, _, handler) = CreateService((request, _) =>
        {
            var url = request.RequestUri?.PathAndQuery;
            if (url == AssetsUrl(1))
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, Serialize(new[] { asset }), totalPages: 1));
            return Task.FromResult(Error(HttpStatusCode.NotFound));
        });

        var result = await service.GetCharacterAssetsAsync(CharacterId);

        Assert.NotNull(result);
        var row = Assert.Single(result);
        Assert.Equal(CharacterId, row.OwnerCharacterId);
        Assert.Equal(60003466, row.LocationId);
        Assert.Equal("station", row.LocationType);
        Assert.Equal(1, handler.RequestCount);
    }
}