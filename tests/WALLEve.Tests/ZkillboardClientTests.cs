using System.Diagnostics;
using System.Net;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WALLEve.Configuration;
using WALLEve.Services.Risk;

namespace WALLEve.Tests;

/// <summary>
/// Fake-HTTP-Tests für den zKillboard-Adapter (#73): Rate-Limit (429),
/// Timeout, fehlende Daten (404), ungültige Antworten, lokaler Cache,
/// Request-Abstand und Provider-Health (Cooldown ohne Requests).
/// Keine Live-zKillboard-Abhängigkeit.
/// </summary>
public class ZkillboardClientTests
{
    private const string BaseUrl = "https://zkboard.local";

    // ------------------------------------------------------------------
    // Fake-HTTP-Infrastruktur
    // ------------------------------------------------------------------

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;
        private readonly List<long> _requestTimestamps = new();
        private readonly List<string> _userAgents = new();
        private int _requestCount;

        public int RequestCount => Volatile.Read(ref _requestCount);

        public IReadOnlyList<string> UserAgents => _userAgents;

        public IReadOnlyList<long> RequestTimestamps => _requestTimestamps;

        public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
            => _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            lock (_requestTimestamps)
            {
                _requestTimestamps.Add(Stopwatch.GetTimestamp());
            }
            if (request.Headers.TryGetValues("User-Agent", out var values))
            {
                _userAgents.Add(string.Join(",", values));
            }
            return _handler(request, cancellationToken);
        }
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json)
        => new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private static ZkillboardSettings DefaultSettings() => new()
    {
        Enabled = true,
        BaseUrl = BaseUrl,
        UserAgent = "WALLEve/1.0 (EVE Companion App)",
        MinRequestIntervalMs = 0,
        CacheTtlMinutes = 15,
        FailureThreshold = 2,
        UnhealthyCooldownSeconds = 60
    };

    private static ZkillboardClient CreateClient(
        ZkillboardSettings settings,
        StubHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(BaseUrl)
        };
        // Wie in Program.cs: User-Agent wird am benannten Client gesetzt.
        httpClient.DefaultRequestHeaders.Add("User-Agent", settings.UserAgent);
        return new ZkillboardClient(
            httpClient,
            Options.Create(settings),
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<ZkillboardClient>.Instance);
    }

    // ------------------------------------------------------------------
    // Erfolg, Cache, User-Agent
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetLosses_ValidResponse_ReturnsLossCountAndCaches()
    {
        var json = "[{\"victim\":{}},{\"victim\":{}},{\"victim\":{}}]";
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse(HttpStatusCode.OK, json)));
        var client = CreateClient(DefaultSettings(), handler);

        var first = await client.GetLossesAsync(30005001);
        var second = await client.GetLossesAsync(30005001); // Cache-Treffer

        Assert.NotNull(first);
        Assert.Equal(3, first!.Losses);
        Assert.Equal(30005001, first.SystemId);
        Assert.True(first.IsDelayed);
        Assert.NotNull(second);
        Assert.Equal(3, second!.Losses);
        // Zweiter Abruf derselben System-ID kommt aus dem Cache: genau 1 HTTP-Request.
        Assert.Equal(1, handler.RequestCount);
        Assert.Single(handler.UserAgents);
        Assert.Contains("WALLEve/1.0", handler.UserAgents[0]);
    }

    [Fact]
    public async Task GetLosses_EmptyArray_IsValidZeroData()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse(HttpStatusCode.OK, "[]")));
        var client = CreateClient(DefaultSettings(), handler);

        var result = await client.GetLossesAsync(30005002);

        // Valide leere Antwort ist ein echtes Datum (0 Verluste), kein Fehler.
        Assert.NotNull(result);
        Assert.Equal(0, result!.Losses);
    }

    // ------------------------------------------------------------------
    // Rate-Limit, Timeout, fehlende Daten, ungültige Antwort
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetLosses_RateLimit_ReturnsNull_AndCooldownStopsRequests()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse(HttpStatusCode.TooManyRequests, "{}")));
        var client = CreateClient(DefaultSettings(), handler);

        var first = await client.GetLossesAsync(30005003);
        var second = await client.GetLossesAsync(30005004);
        Assert.Null(first);
        Assert.Null(second);

        // Schwellwert erreicht (2 Fehler): Provider im Cooldown → dritter Request
        // wird ohne HTTP-Aufruf abgewiesen.
        var third = await client.GetLossesAsync(30005005);
        Assert.Null(third);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task GetLosses_Timeout_ReturnsNullWithoutThrowing()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromCanceled<HttpResponseMessage>(new CancellationToken(true)));
        var client = CreateClient(DefaultSettings(), handler);

        var result = await client.GetLossesAsync(30005006);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetLosses_HttpRequestFailure_ReturnsNullWithoutThrowing()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("connection refused")));
        var client = CreateClient(DefaultSettings(), handler);

        var result = await client.GetLossesAsync(30005007);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetLosses_MissingData_404_ReturnsNull()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse(HttpStatusCode.NotFound, "{}")));
        var client = CreateClient(DefaultSettings(), handler);

        var result = await client.GetLossesAsync(30005008);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetLosses_MalformedResponse_ReturnsNull()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse(HttpStatusCode.OK, "{\"no\":\"array\"}")));
        var client = CreateClient(DefaultSettings(), handler);

        var result = await client.GetLossesAsync(30005009);

        Assert.Null(result);
    }

    // ------------------------------------------------------------------
    // Optionale Quelle und Request-Abstand
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetLosses_Disabled_ReturnsNullWithoutRequest()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse(HttpStatusCode.OK, "[]")));
        var settings = DefaultSettings();
        settings.Enabled = false;
        var client = CreateClient(settings, handler);

        var result = await client.GetLossesAsync(30005010);

        Assert.Null(result);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task GetLosses_RespectsMinimumRequestInterval()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse(HttpStatusCode.OK, "[]")));
        var settings = DefaultSettings();
        settings.MinRequestIntervalMs = 120;
        var client = CreateClient(settings, handler);

        var first = await client.GetLossesAsync(30005011);
        var second = await client.GetLossesAsync(30005012);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(2, handler.RequestCount);

        // Zweiter Request darf frühestens nach dem Mindestabstand starten.
        var timestamps = handler.RequestTimestamps;
        var gapMs = TimeSpan.FromTicks(timestamps[1] - timestamps[0]).TotalMilliseconds;
        Assert.True(gapMs >= 110, $"Request-Abstand {gapMs:F0}ms < Konfiguration (120ms)");
    }
}