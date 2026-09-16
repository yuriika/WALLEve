using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WALLEve.Configuration;
using WALLEve.Models.Authentication;
using WALLEve.Models.Esi;
using WALLEve.Models.Esi.Markets;
using WALLEve.Services.Authentication.Interfaces;
using WALLEve.Services.Esi;
using WALLEve.Services.Esi.Interfaces;
using WALLEve.Services.Market;
using WALLEve.Services.Market.Interfaces;

namespace WALLEve.Tests;

/// <summary>
/// Regionen-Cache (#69): Eine Region wird pro Cachefenster EINMAL vollständig
/// gescannt, Item-Abfragen werden lokal gefiltert (Request-Count-Fixture beweist:
/// keine Vollregionsschleife pro Item). Abgelaufene Fenster lösen genau einen
/// neuen Scan aus; fehlgeschlagene/abgebrochene Refreshes publizieren keine
/// Teildaten — der letzte vollständige Stand bleibt aktiv. Keine Live-ESI.
/// </summary>
public class RegionalMarketCacheServiceTests
{
    private const int RegionId = 10000002;
    private const string BaseUrl = "https://esi.local";

    // ------------------------------------------------------------------
    // Fake-HTTP-Infrastruktur
    // ------------------------------------------------------------------

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;
        private int _requestCount;

        public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
            => _handler = handler;

        public int RequestCount => Volatile.Read(ref _requestCount);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            return await _handler(request, cancellationToken);
        }
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) => new(_handler);
    }

    private sealed class StubAuthService : IEveAuthenticationService
    {
        public Task<EveAuthState?> GetAuthStateAsync()
            => Task.FromResult<EveAuthState?>(new EveAuthState
            {
                AccessToken = "tok",
                RefreshToken = "ref",
                CharacterId = 90073315,
                CharacterName = "Test"
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

    private static (RegionalMarketCacheService Cache, StubHttpMessageHandler Handler) CreateService(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler,
        TimeSpan? cacheWindow = null)
    {
        var world = BuildWorld(handler, cacheWindow);
        return (world.Cache, world.Handler);
    }

    /// <summary>
    /// Baut eine echte ServiceProvider-Welt (Review-Fix #69): Der Regionen-Cache ist
    /// als Singleton registriert, IEsiApiService als scoped — wie in Program.cs.
    /// Damit lassen sich Collector-Scope und UI-Scope realistisch abbilden.
    /// </summary>
    private static (ServiceProvider Provider, RegionalMarketCacheService Cache, StubHttpMessageHandler Handler) BuildWorld(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler,
        TimeSpan? cacheWindow = null)
    {
        var httpHandler = new StubHttpMessageHandler(handler);
        var factory = new StubHttpClientFactory(httpHandler);
        var esiCache = new EsiCacheService(NullLogger<EsiCacheService>.Instance);
        var settings = Options.Create(new EveOnlineSettings { EsiBaseUrl = BaseUrl });
        var appSettings = Options.Create(new ApplicationSettings());

        var services = new ServiceCollection();
        services.AddSingleton<IHttpClientFactory>(factory);
        services.AddSingleton<IEsiCacheService>(esiCache);
        services.AddSingleton(settings);
        services.AddSingleton(appSettings);
        services.AddScoped<IEveAuthenticationService>(_ => new StubAuthService());
        services.AddSingleton<ILogger<EsiApiService>>(NullLogger<EsiApiService>.Instance);
        services.AddScoped<IEsiApiService, EsiApiService>();
        services.AddSingleton<ILogger<RegionalMarketCacheService>>(NullLogger<RegionalMarketCacheService>.Instance);
        services.AddSingleton<IRegionalMarketCacheService>(sp =>
            new RegionalMarketCacheService(
                sp.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<RegionalMarketCacheService>.Instance,
                cacheWindow));

        var provider = services.BuildServiceProvider();
        var cache = (RegionalMarketCacheService)provider.GetRequiredService<IRegionalMarketCacheService>();
        return (provider, cache, httpHandler);
    }

    private static RegionalMarketOrder Order(long id, int typeId, double price, bool buy = false) => new()
    {
        OrderId = id,
        TypeId = typeId,
        LocationId = 60003760,
        SystemId = 30000142,
        Price = price,
        VolumeTotal = 1,
        VolumeRemain = 1,
        MinVolume = 1,
        Duration = 90,
        IsBuyOrder = buy,
        Issued = DateTime.UtcNow,
        Range = "region"
    };

    private static string RegionalUrl(int page) => $"/markets/{RegionId}/orders/?order_type=all&page={page}";

    private static string Serialize<T>(IEnumerable<T> items) => JsonSerializer.Serialize(items);

    // ------------------------------------------------------------------
    // AK1: Request-Count-Fixture — keine Vollregionsschleife pro Item
    // ------------------------------------------------------------------

    [Fact]
    public async Task MultipleTypeQueries_TriggerExactlyOneFullRegionScan()
    {
        // 2 Seiten Regionalscan, Orders für drei verschiedene Type-IDs verteilt.
        var (cache, handler) = CreateService((request, _) =>
        {
            var url = request.RequestUri?.PathAndQuery;
            if (url == RegionalUrl(1))
                return Task.FromResult(JsonResponse(HttpStatusCode.OK,
                    Serialize(new[] { Order(1, 34, 100), Order(2, 35, 200), Order(3, 36, 300) }), totalPages: 2));
            if (url == RegionalUrl(2))
                return Task.FromResult(JsonResponse(HttpStatusCode.OK,
                    Serialize(new[] { Order(4, 34, 110), Order(5, 35, 210), Order(6, 36, 310) })));
            return Task.FromResult(JsonResponse(HttpStatusCode.NotFound, "{\"error\":\"boom\"}"));
        });

        var type34 = await cache.GetOrdersForTypeAsync(RegionId, 34);
        var type35 = await cache.GetOrdersForTypeAsync(RegionId, 35);
        var type36 = await cache.GetOrdersForTypeAsync(RegionId, 36);

        // 3 Item-Abfragen ≠ 3 Regionalscans: genau EIN Scan (2 Seiten) für alle Items.
        Assert.Equal(2, handler.RequestCount);

        Assert.Equal(2, type34.Count);
        Assert.All(type34, o => Assert.Equal(34, o.TypeId));
        Assert.Equal(2, type35.Count);
        Assert.Equal(2, type36.Count);
    }

    [Fact]
    public async Task SingleFlight_ConcurrentReadersShareOneScan()
    {
        var (cache, handler) = CreateService((request, _) =>
        {
            var url = request.RequestUri?.PathAndQuery;
            if (url == RegionalUrl(1))
                return Task.FromResult(JsonResponse(HttpStatusCode.OK,
                    Serialize(new[] { Order(1, 34, 100) }), totalPages: 1));
            return Task.FromResult(JsonResponse(HttpStatusCode.NotFound, "{\"error\":\"boom\"}"));
        });

        var results = await Task.WhenAll(
            cache.GetRegionOrdersAsync(RegionId),
            cache.GetRegionOrdersAsync(RegionId),
            cache.GetRegionOrdersAsync(RegionId));

        Assert.Equal(1, handler.RequestCount);
        Assert.All(results, r => Assert.Single(r));
    }

    // ------------------------------------------------------------------
    // AK2: Cachefenster verhindert redundante Scans; kein Teildaten-Publish
    // ------------------------------------------------------------------

    [Fact]
    public async Task WithinCacheWindow_NoRedundantScan()
    {
        var (cache, handler) = CreateService((request, _) =>
        {
            var url = request.RequestUri?.PathAndQuery;
            if (url == RegionalUrl(1))
                return Task.FromResult(JsonResponse(HttpStatusCode.OK,
                    Serialize(new[] { Order(1, 34, 100) }), totalPages: 1));
            return Task.FromResult(JsonResponse(HttpStatusCode.NotFound, "{\"error\":\"boom\"}"));
        }, cacheWindow: TimeSpan.FromMinutes(5));

        var first = await cache.GetRegionOrdersAsync(RegionId);
        var second = await cache.GetRegionOrdersAsync(RegionId);
        var third = await cache.GetRegionOrdersAsync(RegionId);

        Assert.Single(first);
        Assert.Equal(1, handler.RequestCount);

        var info = cache.GetCacheInfo(RegionId);
        Assert.NotNull(info);
        Assert.True(info!.FromCacheWindow);
        Assert.Equal(1, info.OrderCount);
        Assert.Equal(1, info.PageCount);
    }

    [Fact]
    public async Task AfterWindowExpiry_ExactlyOneNewScan()
    {
        var (cache, handler) = CreateService((request, _) =>
        {
            var url = request.RequestUri?.PathAndQuery;
            if (url == RegionalUrl(1))
                return Task.FromResult(JsonResponse(HttpStatusCode.OK,
                    Serialize(new[] { Order(1, 34, 100) }), totalPages: 1));
            return Task.FromResult(JsonResponse(HttpStatusCode.NotFound, "{\"error\":\"boom\"}"));
        }, cacheWindow: TimeSpan.FromMilliseconds(50));

        _ = await cache.GetRegionOrdersAsync(RegionId);
        Assert.Equal(1, handler.RequestCount);

        await Task.Delay(80);

        // Fenster abgelaufen — Messgrundlage veraltet, noch kein neuer Scan angestoßen.
        Assert.False(cache.GetCacheInfo(RegionId)!.FromCacheWindow);

        _ = await cache.GetRegionOrdersAsync(RegionId);

        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task FailedRefresh_KeepsPreviousCompleteData()
    {
        var failRefresh = true;
        var (cache, handler) = CreateService((request, _) =>
        {
            var url = request.RequestUri?.PathAndQuery;
            if (url == RegionalUrl(1) && failRefresh)
                return Task.FromResult(JsonResponse(HttpStatusCode.OK,
                    Serialize(new[] { Order(1, 34, 100), Order(2, 35, 200) }), totalPages: 2));
            if (url == RegionalUrl(2) && failRefresh)
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, Serialize(new[] { Order(3, 36, 300) })));
            if (url == RegionalUrl(1))
                return Task.FromResult(JsonResponse(HttpStatusCode.OK,
                    Serialize(new[] { Order(1, 34, 100), Order(2, 35, 200) }), totalPages: 2));
            // Seite 2 schlägt beim Refresh fehl -> atomar verwerfen
            return Task.FromResult(JsonResponse(HttpStatusCode.InternalServerError, "{\"error\":\"boom\"}"));
        }, cacheWindow: TimeSpan.FromMilliseconds(50));

        var first = await cache.GetRegionOrdersAsync(RegionId);
        Assert.Equal(3, first.Count);
        Assert.Equal(2, handler.RequestCount); // 2 Seiten beim ersten vollständigen Scan

        await Task.Delay(80);
        failRefresh = false;

        // Refresh scheitert auf Seite 2 -> der vollständige alte Stand bleibt aktiv.
        var after = await cache.GetRegionOrdersAsync(RegionId);

        Assert.Equal(4, handler.RequestCount); // 2 alte + 2 Refresh-Versuche
        Assert.Equal(3, after.Count);
        Assert.Equal(new long[] { 1, 2, 3 }, after.Select(o => o.OrderId).OrderBy(id => id).ToArray());
        Assert.Equal(3, cache.GetCacheInfo(RegionId)!.OrderCount);
    }

    // ------------------------------------------------------------------
    // Review-Fix: Singleton-Lebensdauer — Collector- und UI-Scope teilen EINE Instanz
    // ------------------------------------------------------------------

    [Fact]
    public async Task CollectorAndUiScopes_ShareOneCacheAndObserveSameScanBasis()
    {
        // Der Review beanstandete die scoped-Registrierung: Jeder Collector-Loop
        // (frischer Scope je 5-Minuten-Zyklus) und jeder UI-Request hätten eine eigene
        // Cache-Instanz erhalten — das Fenster griff nicht und die UI sah nie die
        // Messgrundlage. Als Singleton muss AUF EINER Provider-Welt gelten:
        // 1) aufeinanderfolgende Collector-Scopes liefern dieselbe Instanz,
        // 2) der zweite Loop stößt keinen zweiten Scan an (Fenster über Scope-Grenzen),
        // 3) ein UI-Scope reflektiert den Scan-Basis des Collectors (GetCacheInfo).
        var world = BuildWorld((request, _) =>
        {
            var url = request.RequestUri?.PathAndQuery;
            if (url == RegionalUrl(1))
                return Task.FromResult(JsonResponse(HttpStatusCode.OK,
                    Serialize(new[] { Order(1, 34, 100) }), totalPages: 1));
            return Task.FromResult(JsonResponse(HttpStatusCode.NotFound, "{\"error\":\"boom\"}"));
        }, cacheWindow: TimeSpan.FromMinutes(5));
        using var provider = world.Provider;

        IRegionalMarketCacheService firstLoopCache;
        using (var collectorScope1 = provider.CreateScope())
        {
            firstLoopCache = collectorScope1.ServiceProvider.GetRequiredService<IRegionalMarketCacheService>();
            _ = await firstLoopCache.GetRegionOrdersAsync(RegionId); // 1 vollständiger Scan
        }

        using var collectorScope2 = provider.CreateScope();
        var secondLoopCache = collectorScope2.ServiceProvider.GetRequiredService<IRegionalMarketCacheService>();

        // (1) Dieselbe Instanz über Scope-Grenzen hinweg.
        Assert.Same(firstLoopCache, secondLoopCache);

        // (2) Das 5-Minuten-Fenster verhindert einen redundanten zweiten Scan.
        _ = await secondLoopCache.GetRegionOrdersAsync(RegionId);
        Assert.Equal(1, world.Handler.RequestCount);

        // (3) UI-Scope (wie MarketDataService in einem Request-Circuit) sieht den
        // vom Collector geschriebenen Scan-Basis und die Messgrundlage.
        using var uiScope = provider.CreateScope();
        var uiCache = uiScope.ServiceProvider.GetRequiredService<IRegionalMarketCacheService>();
        Assert.Same(firstLoopCache, uiCache);
        var info = uiCache.GetCacheInfo(RegionId);
        Assert.NotNull(info);
        Assert.Equal(1, info!.OrderCount);
        Assert.Equal(1, info.PageCount);
        Assert.True(info.FromCacheWindow);
    }
}