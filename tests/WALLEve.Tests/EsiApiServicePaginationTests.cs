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
        public bool RefreshSucceeds { get; set; } = true;
        public int ForceRefreshCalls { get; private set; }

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

        public Task<bool> ForceRefreshAccessTokenAsync()
        {
            ForceRefreshCalls++;
            return Task.FromResult(RefreshSucceeds);
        }

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
        => CreateService(handler, new StubAuthService());

    private static (EsiApiService Service, EsiCacheService Cache, StubHttpMessageHandler Handler) CreateService(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler, StubAuthService auth)
    {
        var httpHandler = new StubHttpMessageHandler(handler);
        var factory = new StubHttpClientFactory(httpHandler);
        var cache = new EsiCacheService(NullLogger<EsiCacheService>.Instance);
        var settings = Options.Create(new EveOnlineSettings { EsiBaseUrl = BaseUrl });
        var appSettings = Options.Create(new ApplicationSettings());
        var service = new EsiApiService(settings, appSettings, auth, factory, cache,
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
        // Deterministisch ohne Wall-Clock-Rennen gegen den internen 250-ms-Stagger:
        // Die Reihenfolge, in der Task.Run die Staffelungen von Seite 2 und Seite 3
        // startet, ist Scheduler-abhängig — Seite 3 kann vor Seite 2 auf der Leitung
        // sein. Deshalb handshaked der Seite-3-Handler mit dem Seite-2-Handler: Seite 3
        // gilt erst dann als in-flight, wenn Seite 2 nachweislich angefragt wurde.
        // Das garantiert das dokumentierte Szenario „Seite 1..3 angefragt, Seite 2 hat
        // bereits geliefert, dann Cancellation" unabhängig von der Task-Reihenfolge.
        // (Die frühere Variante schlief 150 ms; unter Last konnte Seite 3 vor Seite 2
        // angefragt sein → Timing-Flake, RequestCount war dann 2 statt 3.)
        var thirdPageInFlight = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var page2Delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (service, _, handler) = CreateService(async (request, ct) =>
        {
            var url = request.RequestUri?.PathAndQuery;
            if (url == RegionalUrl(1))
                return JsonResponse(HttpStatusCode.OK, Serialize(new[] { Order(1) }), totalPages: 3);
            if (url == RegionalUrl(2))
            {
                page2Delivered.TrySetResult();
                return JsonResponse(HttpStatusCode.OK, Serialize(new[] { Order(2) }));
            }
            if (url == RegionalUrl(3))
            {
                // Erst wenn Seite 2 angefragt wurde, ist das Szenario hergestellt;
                // bis dahin wartet Seite 3 (Cancellation löst das Warten sauber auf).
                await page2Delivered.Task.WaitAsync(ct);
                thirdPageInFlight.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, ct); // hängt bis zur Cancellation
                return Error(HttpStatusCode.NotFound); // unerreichbar — TaskCanceledException kommt zuvor
            }
            return Error(HttpStatusCode.NotFound);
        });

        using var cts = new CancellationTokenSource();
        var fetchTask = service.GetAllRegionalMarketOrdersAsync(RegionId, ct: cts.Token);

        // Erst canceln, wenn Seite 3 nachweislich hängt (großzügiges Sicherheitslimit —
        // kein Laufzeit-Rennen: Der 250-ms-Stagger feuert garantiert, sofern der
        // Service weitere Seiten anfragt).
        await Task.WhenAny(thirdPageInFlight.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.True(thirdPageInFlight.Task.IsCompleted, "Seite 3 wurde nicht angefragt, bevor gecancelt wurde.");

        cts.Cancel();
        var result = await fetchTask;

        Assert.Null(result); // keine Teilmenge publiziert, obwohl Seite 2 bereits Daten geliefert hat
        Assert.Equal(3, handler.RequestCount); // genau Seite 1..3 angefragt — Szenario steht wie dokumentiert
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

    // ------------------------------------------------------------------
    // Assets: Abbruch waehrend Folgeseite -> OperationCanceledException
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetCharacterAssets_CancelledMidFetch_ThrowsOperationCanceledNotReturnNull()
    {
        // Abbruch waehrend der zweiten Seite muss als OperationCanceledException
        // propagieren und darf NICHT als null zurueckkommen (alter catch(Exception)-Pfad).
        var page2Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (service, _, _) = CreateService(async (request, ct) =>
        {
            var url = request.RequestUri?.PathAndQuery;
            if (url == AssetsUrl(1))
                return JsonResponse(HttpStatusCode.OK, Serialize(new[] { new
                {
                    item_id = 1001L, type_id = 1234, quantity = 1,
                    location_id = 60003466L, location_type = "station",
                    location_flag = "Hangar", is_singleton = false
                } }), totalPages: 2);
            if (url == AssetsUrl(2))
            {
                await page2Gate.Task.WaitAsync(ct);
                return JsonResponse(HttpStatusCode.OK, Serialize(Array.Empty<object>()));
            }
            return Error(HttpStatusCode.NotFound);
        });

        using var cts = new CancellationTokenSource();
        var fetchTask = service.GetCharacterAssetsAsync(CharacterId, cts.Token);

        // Seite 1 geliefert, Seite 2 haengt -> canceln
        await Task.Delay(200);
        cts.Cancel();
        page2Gate.SetResult(); // entblockt die Seite, aber Cancellation ist bereits signalisiert

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fetchTask);
    }

    // ------------------------------------------------------------------
    // GetStructureAsync (#50 Review): Fehlerklassifikation + Cancellation
    // ------------------------------------------------------------------

    private const long StructureId = 1_000_000_000_000;

    private static string StructureUrl(long structureId)
        => $"/universe/structures/{structureId}/";

    [Fact]
    public async Task GetStructure_Unauthorized_ReturnsUnauthenticated()
    {
        var (service, _, _) = CreateService((_, _) => Task.FromResult(Error(HttpStatusCode.Unauthorized)));

        var result = await service.GetStructureAsync(StructureId);

        Assert.False(result.IsResolved);
        Assert.Equal("unauthenticated", result.Error);
    }

    [Fact]
    public async Task GetStructure_Forbidden_ReturnsError403()
    {
        var (service, _, _) = CreateService((_, _) => Task.FromResult(Error(HttpStatusCode.Forbidden)));

        var result = await service.GetStructureAsync(StructureId);

        Assert.False(result.IsResolved);
        Assert.Equal("403", result.Error);
    }

    [Fact]
    public async Task GetStructure_NotFound_ReturnsNotFound()
    {
        var (service, _, _) = CreateService((_, _) => Task.FromResult(Error(HttpStatusCode.NotFound)));

        var result = await service.GetStructureAsync(StructureId);

        Assert.False(result.IsResolved);
        Assert.Equal("not-found", result.Error);
    }

    [Fact]
    public async Task GetStructure_RateLimited_ReturnsRateLimitNotUnavailable()
    {
        var (service, _, _) = CreateService((_, _) => Task.FromResult(Error(HttpStatusCode.TooManyRequests)));

        var result = await service.GetStructureAsync(StructureId);

        Assert.False(result.IsResolved);
        Assert.Equal("rate-limit", result.Error);
    }

    [Fact]
    public async Task GetStructure_ServerError_ReturnsServerErrorNotUnavailable()
    {
        var (service, _, _) = CreateService((_, _) => Task.FromResult(Error(HttpStatusCode.InternalServerError)));

        var result = await service.GetStructureAsync(StructureId);

        Assert.False(result.IsResolved);
        Assert.Equal("server-error", result.Error);
    }

    [Fact]
    public async Task GetStructure_Success_ResolvesWithStructureIdFromUrl()
    {
        var (service, _, handler) = CreateService((request, _) =>
        {
            Assert.EndsWith(StructureUrl(StructureId), request.RequestUri?.PathAndQuery);
            return Task.FromResult(JsonResponse(HttpStatusCode.OK,
                "{\"name\":\"Keepstar\",\"solar_system_id\":30000142,\"type_id\":35834}"));
        });

        var result = await service.GetStructureAsync(StructureId);

        Assert.True(result.IsResolved);
        Assert.Equal("Keepstar", result.Structure!.Name);
        Assert.Equal(StructureId, result.Structure.StructureId); // ID stammt aus der URL
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task GetStructure_CancellationMidFetch_RethrowsOperationCanceledNotUnavailable()
    {
        // Handler hängt, bis das Token feuert; die Cancellation muss als
        // OperationCanceledException durchgereicht werden und darf NICHT als
        // erfolgreich zurückgegebenes Unresolved ("unavailable") enden.
        var (service, _, _) = CreateService((request, ct) =>
        {
            var tcs = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            ct.Register(() => tcs.TrySetCanceled(ct));
            return tcs.Task;
        });

        using var cts = new CancellationTokenSource();
        var fetchTask = service.GetStructureAsync(StructureId, cts.Token);
        await Task.Delay(100);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fetchTask);
    }

    // ------------------------------------------------------------------
    // Telemetrie-Sink (#67): Messung des Regionalscans auf Transportebene
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetAllRegionalMarketOrders_TelemetrySink_ReportsEveryPageWithByteCounts()
    {
        var sink = new List<WALLEve.Models.Measurement.RegionalScanPageTelemetry>();
        var (service, _, _) = CreateService(async (request, ct) =>
        {
            if (request.RequestUri?.PathAndQuery == RegionalUrl(1))
            {
                var response = JsonResponse(HttpStatusCode.OK, Serialize(new[] { Order(1), Order(2) }), totalPages: 2);
                response.Headers.ETag = new EntityTagHeaderValue("\"v1\"");
                return response;
            }

            var page2 = JsonResponse(HttpStatusCode.OK, Serialize(new[] { Order(3) }));
            page2.Headers.ETag = new EntityTagHeaderValue("\"v2\"");
            return page2;
        });

        var result = await service.GetAllRegionalMarketOrdersAsync(RegionId, telemetrySink: sink.Add);

        Assert.Equal(3, result?.Count);
        Assert.Equal(2, sink.Count);

        Assert.Equal(1, sink[0].Page);
        Assert.Equal(2, sink[0].OrderCount);
        Assert.True(sink[0].ContentLength > 0, "Content-Length der Seite 1 muss gemessen werden");
        Assert.True(sink[0].Success);
        Assert.False(sink[0].FromCache);

        Assert.Equal(2, sink[1].Page);
        Assert.Equal(1, sink[1].OrderCount);
        Assert.True(sink[1].ContentLength > 0);
        Assert.True(sink[1].Success);
    }

    [Fact]
    public async Task GetAllRegionalMarketOrders_TelemetrySink_SecondRunMarks304PagesAsCacheHits()
    {
        var sink = new List<WALLEve.Models.Measurement.RegionalScanPageTelemetry>();
        var requestCount = 0;
        var (service, _, _) = CreateService(async (request, ct) =>
        {
            Interlocked.Increment(ref requestCount);
            if (request.Headers.IfNoneMatch.Count > 0)
            {
                // ETag-Cache-Treffer: 304 ohne Body — kein Transfer.
                return new HttpResponseMessage(HttpStatusCode.NotModified);
            }

            var response = JsonResponse(HttpStatusCode.OK, Serialize(new[] { Order(1), Order(2) }));
            response.Headers.ETag = new EntityTagHeaderValue("\"v1\"");
            return response;
        });

        // Erster Lauf füllt den Cache (ETag).
        await service.GetAllRegionalMarketOrdersAsync(RegionId);
        // Zweiter Lauf: If-None-Match → 304, Daten aus dem Cache.
        var secondResult = await service.GetAllRegionalMarketOrdersAsync(RegionId, telemetrySink: sink.Add);

        Assert.Equal(2, secondResult?.Count);
        var page = Assert.Single(sink);
        Assert.Equal(1, page.Page);
        Assert.True(page.FromCache, "304-Seite muss als Cache-Treffer markiert sein");
        Assert.Null(page.ContentLength);
        Assert.True(page.Success);
    }

    // ------------------------------------------------------------------
    // Mining-Ledger-Pagination (#185): alle Seiten atomar, Fehler auf
    // erster/mittlerer/letzter Seite publizieren null, 304 mit Cache,
    // gültig leeres Ergebnis.
    // ------------------------------------------------------------------

    private static string MiningUrl(int page)
        => $"/characters/{CharacterId}/mining/?page={page}";

    private static CharacterMiningEntry MiningEntry(int i) => new()
    {
        Date = $"2026-09-{(15 - i):00}",
        Quantity = 1000L + 500 * i,
        SolarSystemId = 30000001 + i,
        TypeId = 1230 + i
    };

    private static string MiningUrlFor(int i) => MiningUrl(1 + (i - 1) / 2); // 2 entries per page

    [Fact]
    public async Task GetCharacterMiningLedger_Success_MergesAllPagesExactlyOnce()
    {
        var (service, _, handler) = CreateService((request, _) =>
        {
            var url = request.RequestUri?.PathAndQuery;
            if (url == MiningUrl(1))
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, Serialize(new[] { MiningEntry(0), MiningEntry(1) }), totalPages: 2));
            if (url == MiningUrl(2))
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, Serialize(new[] { MiningEntry(2) })));
            return Task.FromResult(Error(HttpStatusCode.NotFound));
        });

        var result = await service.GetCharacterMiningLedgerAsync(CharacterId);

        Assert.NotNull(result);
        Assert.Equal(3, result!.Count);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task GetCharacterMiningLedger_FirstPageFails_ReturnsNull()
    {
        var (service, _, _) = CreateService((_, _) => Task.FromResult(Error(HttpStatusCode.InternalServerError)));

        var result = await service.GetCharacterMiningLedgerAsync(CharacterId);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetCharacterMiningLedger_MiddlePageFails_ReturnsNull()
    {
        var (service, _, _) = CreateService((request, _) =>
        {
            var url = request.RequestUri?.PathAndQuery;
            if (url == MiningUrl(1))
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, Serialize(new[] { MiningEntry(0) }), totalPages: 3));
            if (url == MiningUrl(2))
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, Serialize(new[] { MiningEntry(1) })));
            return Task.FromResult(Error(HttpStatusCode.InternalServerError)); // Seite 3
        });

        var result = await service.GetCharacterMiningLedgerAsync(CharacterId);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetCharacterMiningLedger_LastPageFails_ReturnsNull()
    {
        var (service, _, _) = CreateService((request, _) =>
        {
            var url = request.RequestUri?.PathAndQuery;
            if (url == MiningUrl(1))
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, Serialize(new[] { MiningEntry(0) }), totalPages: 2));
            return Task.FromResult(Error(HttpStatusCode.InternalServerError)); // Seite 2
        });

        var result = await service.GetCharacterMiningLedgerAsync(CharacterId);

        Assert.Null(result);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]        // 401
    [InlineData(HttpStatusCode.Forbidden)]            // 403
    [InlineData(HttpStatusCode.TooManyRequests)]       // 429 — Rate Limit
    [InlineData(HttpStatusCode.InternalServerError)]   // 500
    [InlineData(HttpStatusCode.ServiceUnavailable)]    // 503
    public async Task GetCharacterMiningLedger_HttpError_ReturnsNull(HttpStatusCode status)
    {
        var (service, _, _) = CreateService((_, _) => Task.FromResult(Error(status)));

        var result = await service.GetCharacterMiningLedgerAsync(CharacterId);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetCharacterMiningLedger_InvalidJson_ReturnsNull_NotCompleteEmpty()
    {
        var (service, _, _) = CreateService((request, _) =>
        {
            Assert.Equal(MiningUrl(1), request.RequestUri?.PathAndQuery);
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, "this is not json"));
        });

        var result = await service.GetCharacterMiningLedgerAsync(CharacterId);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetCharacterMiningLedger_EmptyFirstPage_ReturnsEmptyList_NotNull()
    {
        var (service, _, _) = CreateService((request, _) =>
        {
            Assert.StartsWith(MiningUrl(1), request.RequestUri?.PathAndQuery);
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, Serialize(Array.Empty<CharacterMiningEntry>())));
        });

        var result = await service.GetCharacterMiningLedgerAsync(CharacterId);

        Assert.NotNull(result);
        Assert.Empty(result!);
    }

    [Fact]
    public async Task GetCharacterMiningLedger_PreCancelled_ReturnsNull()
    {
        var (service, _, _) = CreateService((_, _) => Task.FromResult(Error(HttpStatusCode.InternalServerError)));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await service.GetCharacterMiningLedgerAsync(CharacterId, cts.Token);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetCharacterMiningLedger_NotModified_WithCache_ReturnsCachedData()
    {
        var cachedEntries = new List<CharacterMiningEntry> { MiningEntry(0), MiningEntry(1) };

        var (service, cache, _) = CreateService((request, _) =>
        {
            Assert.True(request.Headers.IfNoneMatch.Count > 0, "If-None-Match muss bei Cache-Eintrag gesendet werden");
            return Task.FromResult(JsonResponse(HttpStatusCode.NotModified, string.Empty));
        });

        cache.Set(MiningUrl(1), "\"v1\"", cachedEntries, DateTime.UtcNow.AddHours(1));

        var result = await service.GetCharacterMiningLedgerAsync(CharacterId);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Count);
    }

    [Fact]
    public async Task GetCharacterMiningLedger_ErrorLimit420_ReturnsNull()
    {
        // 420 Error Limited: ESI-spezifischer Statuscode
        var (service, _, _) = CreateService((_, _) =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)420)
            {
                Content = new StringContent("{\"error\":\"error limit reached\"}", Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        });

        var result = await service.GetCharacterMiningLedgerAsync(CharacterId);

        Assert.Null(result);
    }

    // ------------------------------------------------------------------
    // 401 → Refresh → Retry (#201)
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetCharacterMiningLedger_401RefreshSucceeds_RetriesAndReturnsData()
    {
        var auth = new StubAuthService { RefreshSucceeds = true };
        var firstCall = true;
        var requests = 0;
        var (service, _, _) = CreateService((request, _) =>
        {
            requests++;
            Assert.Equal(MiningUrl(1), request.RequestUri?.PathAndQuery);
            if (firstCall)
            {
                firstCall = false;
                return Task.FromResult(Error(HttpStatusCode.Unauthorized)); // 401 zuerst
            }
            return Task.FromResult(JsonResponse(HttpStatusCode.OK,
                Serialize(new[] { MiningEntry(0), MiningEntry(1) })));
        }, auth);

        var result = await service.GetCharacterMiningLedgerAsync(CharacterId);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Count);
        Assert.Equal(2, requests);          // genau ein Retry
        Assert.Equal(1, auth.ForceRefreshCalls);
    }

    [Fact]
    public async Task GetCharacterMiningLedger_401RefreshFails_ReturnsNull_NoRetryLoop()
    {
        var auth = new StubAuthService { RefreshSucceeds = false };
        var requests = 0;
        var (service, _, _) = CreateService((request, _) =>
        {
            requests++;
            return Task.FromResult(Error(HttpStatusCode.Unauthorized));
        }, auth);

        var result = await service.GetCharacterMiningLedgerAsync(CharacterId);

        Assert.Null(result);
        Assert.Equal(1, requests);           // kein Endlos-Retry
        Assert.Equal(1, auth.ForceRefreshCalls); // Refresh wurde versucht, schlug fehl
    }

    [Fact]
    public async Task GetCharacterMiningLedger_RetryStill401_ReturnsNull()
    {
        var auth = new StubAuthService { RefreshSucceeds = true };
        var requests = 0;
        var (service, _, _) = CreateService((request, _) =>
        {
            requests++;
            return Task.FromResult(Error(HttpStatusCode.Unauthorized)); // auch nach Refresh 401
        }, auth);

        var result = await service.GetCharacterMiningLedgerAsync(CharacterId);

        Assert.Null(result);
        Assert.Equal(2, requests);           // Original + ein Retry, dann Schluss
        Assert.Equal(1, auth.ForceRefreshCalls);
    }

    [Fact]
    public async Task GetCharacterMiningLedger_403_DoesNotTriggerRefresh()
    {
        var auth = new StubAuthService { RefreshSucceeds = true };
        var (service, _, _) = CreateService((_, _) =>
            Task.FromResult(Error(HttpStatusCode.Forbidden)), auth);

        var result = await service.GetCharacterMiningLedgerAsync(CharacterId);

        Assert.Null(result);
        Assert.Equal(0, auth.ForceRefreshCalls); // 403 ist Scope-/Auth-Fehler, kein Refresh
    }

    // ------------------------------------------------------------------
    // Industry-Jobs-Pagination (#185)
    // ------------------------------------------------------------------

    private static string IndustryUrl(int page)
        => $"/characters/{CharacterId}/industry/jobs/?page={page}";

    private static CharacterIndustryJob IndustryJob(int i) => new()
    {
        ActivityId = 1,
        BlueprintId = 1014567891234L + i,
        BlueprintLocationId = 60003760L,
        BlueprintTypeId = 1030 + i,
        Duration = 3600,
        EndDate = "2026-09-14T12:00:00Z",
        FacilityId = 60003760L,
        InstallerId = CharacterId,
        JobId = 100 + i,
        LicensedRuns = 1,
        OutputLocationId = 60003760L,
        ProductTypeId = 44992 + i,
        Runs = 1,
        StartDate = "2026-09-13T12:00:00Z",
        StationId = 60003760,
        Status = i == 0 ? "delivered" : "active",
        SuccessfulRuns = i == 0 ? 1 : null
    };

    [Fact]
    public async Task GetCharacterIndustryJobs_Success_MergesAllPagesExactlyOnce()
    {
        var (service, _, handler) = CreateService((request, _) =>
        {
            var url = request.RequestUri?.PathAndQuery;
            if (url == IndustryUrl(1))
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, Serialize(new[] { IndustryJob(0), IndustryJob(1) }), totalPages: 2));
            if (url == IndustryUrl(2))
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, Serialize(new[] { IndustryJob(2) })));
            return Task.FromResult(Error(HttpStatusCode.NotFound));
        });

        var result = await service.GetCharacterIndustryJobsAsync(CharacterId);

        Assert.NotNull(result);
        Assert.Equal(3, result!.Count);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task GetCharacterIndustryJobs_StructureStationId_AboveIntMax_DeserializesToLong()
    {
        // #205: EVE-Struktur-Station-IDs (> int.MaxValue) müssen als long
        // dekodiert werden, sonst platzt die Deserialisierung und der Sync
        // scheitert mit "ESI lieferte keine vollständigen Industrie-Jobs".
        const long structureStationId = 10_550_442_042L;
        var job = IndustryJob(0);
        job.StationId = structureStationId;

        var (service, _, _) = CreateService((request, _) =>
        {
            Assert.Equal(IndustryUrl(1), request.RequestUri?.PathAndQuery);
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, Serialize(new[] { job })));
        });

        var result = await service.GetCharacterIndustryJobsAsync(CharacterId);

        Assert.NotNull(result);
        var single = Assert.Single(result!);
        Assert.Equal(structureStationId, single.StationId);
    }

    [Fact]
    public async Task GetCharacterIndustryJobs_FirstPageFails_ReturnsNull()
    {
        var (service, _, _) = CreateService((_, _) => Task.FromResult(Error(HttpStatusCode.InternalServerError)));

        var result = await service.GetCharacterIndustryJobsAsync(CharacterId);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetCharacterIndustryJobs_MiddlePageFails_ReturnsNull()
    {
        var (service, _, _) = CreateService((request, _) =>
        {
            var url = request.RequestUri?.PathAndQuery;
            if (url == IndustryUrl(1))
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, Serialize(new[] { IndustryJob(0) }), totalPages: 3));
            if (url == IndustryUrl(2))
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, Serialize(new[] { IndustryJob(1) })));
            return Task.FromResult(Error(HttpStatusCode.InternalServerError)); // Seite 3
        });

        var result = await service.GetCharacterIndustryJobsAsync(CharacterId);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetCharacterIndustryJobs_LastPageFails_ReturnsNull()
    {
        var (service, _, _) = CreateService((request, _) =>
        {
            var url = request.RequestUri?.PathAndQuery;
            if (url == IndustryUrl(1))
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, Serialize(new[] { IndustryJob(0) }), totalPages: 2));
            return Task.FromResult(Error(HttpStatusCode.InternalServerError)); // Seite 2
        });

        var result = await service.GetCharacterIndustryJobsAsync(CharacterId);

        Assert.Null(result);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task GetCharacterIndustryJobs_HttpError_ReturnsNull(HttpStatusCode status)
    {
        var (service, _, _) = CreateService((_, _) => Task.FromResult(Error(status)));

        var result = await service.GetCharacterIndustryJobsAsync(CharacterId);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetCharacterIndustryJobs_InvalidJson_ReturnsNull_NotCompleteEmpty()
    {
        var (service, _, _) = CreateService((request, _) =>
        {
            Assert.Equal(IndustryUrl(1), request.RequestUri?.PathAndQuery);
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, "this is not json"));
        });

        var result = await service.GetCharacterIndustryJobsAsync(CharacterId);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetCharacterIndustryJobs_EmptyFirstPage_ReturnsEmptyList_NotNull()
    {
        var (service, _, _) = CreateService((request, _) =>
        {
            Assert.StartsWith(IndustryUrl(1), request.RequestUri?.PathAndQuery);
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, Serialize(Array.Empty<CharacterIndustryJob>())));
        });

        var result = await service.GetCharacterIndustryJobsAsync(CharacterId);

        Assert.NotNull(result);
        Assert.Empty(result!);
    }

    [Fact]
    public async Task GetCharacterIndustryJobs_PreCancelled_ReturnsNull()
    {
        var (service, _, _) = CreateService((_, _) => Task.FromResult(Error(HttpStatusCode.InternalServerError)));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await service.GetCharacterIndustryJobsAsync(CharacterId, cts.Token);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetCharacterIndustryJobs_NotModified_WithCache_ReturnsCachedData()
    {
        var cachedEntries = new List<CharacterIndustryJob> { IndustryJob(0), IndustryJob(1) };

        var (service, cache, _) = CreateService((request, _) =>
        {
            Assert.True(request.Headers.IfNoneMatch.Count > 0);
            return Task.FromResult(JsonResponse(HttpStatusCode.NotModified, string.Empty));
        });

        cache.Set(IndustryUrl(1), "\"v1\"", cachedEntries, DateTime.UtcNow.AddHours(1));

        var result = await service.GetCharacterIndustryJobsAsync(CharacterId);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Count);
    }

    [Fact]
    public async Task GetCharacterIndustryJobs_ErrorLimit420_ReturnsNull()
    {
        // 420 Error Limited: ESI-spezifischer Statuscode
        var (service, _, _) = CreateService((_, _) =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)420)
            {
                Content = new StringContent("{\"error\":\"error limit reached\"}", Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        });

        var result = await service.GetCharacterIndustryJobsAsync(CharacterId);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetCharacterIndustryJobs_CancelledMidFetch_ReturnsNull_NoPartialPublished()
    {
        // Deterministisch ohne Wall-Clock-Rennen: Seite 3 handshaked mit Seite 2,
        // um das Szenario „Seite 1–3 angefragt, Seite 2 bereits geliefert, dann
        // Cancellation" unabhängig von Task-Reihenfolge zu garantieren.
        var thirdPageInFlight = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var page2Delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (service, _, handler) = CreateService(async (request, ct) =>
        {
            var url = request.RequestUri?.PathAndQuery;
            if (url == IndustryUrl(1))
                return JsonResponse(HttpStatusCode.OK, Serialize(new[] { IndustryJob(0) }), totalPages: 3);
            if (url == IndustryUrl(2))
            {
                page2Delivered.TrySetResult();
                return JsonResponse(HttpStatusCode.OK, Serialize(new[] { IndustryJob(1) }));
            }
            if (url == IndustryUrl(3))
            {
                await page2Delivered.Task.WaitAsync(ct);
                thirdPageInFlight.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return Error(HttpStatusCode.NotFound);
            }
            return Error(HttpStatusCode.NotFound);
        });

        using var cts = new CancellationTokenSource();
        var fetchTask = service.GetCharacterIndustryJobsAsync(CharacterId, cts.Token);

        await Task.WhenAny(thirdPageInFlight.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.True(thirdPageInFlight.Task.IsCompleted, "Seite 3 wurde nicht angefragt, bevor gecancelt wurde.");

        cts.Cancel();
        var result = await fetchTask;

        Assert.Null(result); // keine Teilmenge publiziert, obwohl Seite 2 bereits Daten geliefert hat
        Assert.Equal(3, handler.RequestCount);
    }

    // ------------------------------------------------------------------
    // Blueprint-Pagination (#185)
    // ------------------------------------------------------------------

    private static string BlueprintUrl(int page)
        => $"/characters/{CharacterId}/blueprints/?page={page}";

    private static CharacterBlueprint BlueprintEntry(int i) => new()
    {
        ItemId = 1000L + i,
        TypeId = 1030 + i,
        LocationId = 60003760L,
        LocationFlag = "Hangar",
        Quantity = -1,
        MaterialEfficiency = 10,
        TimeEfficiency = 20,
        Runs = -1,
        IsBlueprintCopy = false
    };

    [Fact]
    public async Task GetCharacterBlueprints_Success_MergesAllPagesExactlyOnce()
    {
        var (service, _, handler) = CreateService((request, _) =>
        {
            var url = request.RequestUri?.PathAndQuery;
            if (url == BlueprintUrl(1))
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, Serialize(new[] { BlueprintEntry(0), BlueprintEntry(1) }), totalPages: 2));
            if (url == BlueprintUrl(2))
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, Serialize(new[] { BlueprintEntry(2) })));
            return Task.FromResult(Error(HttpStatusCode.NotFound));
        });

        var result = await service.GetCharacterBlueprintsAsync(CharacterId);

        Assert.NotNull(result);
        Assert.Equal(3, result!.Count);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task GetCharacterBlueprints_FirstPageFails_ReturnsNull()
    {
        var (service, _, _) = CreateService((_, _) => Task.FromResult(Error(HttpStatusCode.InternalServerError)));

        var result = await service.GetCharacterBlueprintsAsync(CharacterId);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetCharacterBlueprints_MiddlePageFails_ReturnsNull()
    {
        var (service, _, _) = CreateService((request, _) =>
        {
            var url = request.RequestUri?.PathAndQuery;
            if (url == BlueprintUrl(1))
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, Serialize(new[] { BlueprintEntry(0) }), totalPages: 3));
            if (url == BlueprintUrl(2))
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, Serialize(new[] { BlueprintEntry(1) })));
            return Task.FromResult(Error(HttpStatusCode.InternalServerError)); // Seite 3
        });

        var result = await service.GetCharacterBlueprintsAsync(CharacterId);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetCharacterBlueprints_LastPageFails_ReturnsNull()
    {
        var (service, _, _) = CreateService((request, _) =>
        {
            var url = request.RequestUri?.PathAndQuery;
            if (url == BlueprintUrl(1))
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, Serialize(new[] { BlueprintEntry(0) }), totalPages: 2));
            return Task.FromResult(Error(HttpStatusCode.InternalServerError)); // Seite 2
        });

        var result = await service.GetCharacterBlueprintsAsync(CharacterId);

        Assert.Null(result);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task GetCharacterBlueprints_HttpError_ReturnsNull(HttpStatusCode status)
    {
        var (service, _, _) = CreateService((_, _) => Task.FromResult(Error(status)));

        var result = await service.GetCharacterBlueprintsAsync(CharacterId);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetCharacterBlueprints_InvalidJson_ReturnsNull_NotCompleteEmpty()
    {
        var (service, _, _) = CreateService((request, _) =>
        {
            Assert.Equal(BlueprintUrl(1), request.RequestUri?.PathAndQuery);
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, "this is not json"));
        });

        var result = await service.GetCharacterBlueprintsAsync(CharacterId);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetCharacterBlueprints_EmptyFirstPage_ReturnsEmptyList_NotNull()
    {
        var (service, _, _) = CreateService((request, _) =>
        {
            Assert.StartsWith(BlueprintUrl(1), request.RequestUri?.PathAndQuery);
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, Serialize(Array.Empty<CharacterBlueprint>())));
        });

        var result = await service.GetCharacterBlueprintsAsync(CharacterId);

        Assert.NotNull(result);
        Assert.Empty(result!);
    }

    [Fact]
    public async Task GetCharacterBlueprints_PreCancelled_ReturnsNull()
    {
        var (service, _, _) = CreateService((_, _) => Task.FromResult(Error(HttpStatusCode.InternalServerError)));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await service.GetCharacterBlueprintsAsync(CharacterId, cts.Token);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetCharacterBlueprints_NotModified_WithCache_ReturnsCachedData()
    {
        var cachedEntries = new List<CharacterBlueprint> { BlueprintEntry(0), BlueprintEntry(1) };

        var (service, cache, _) = CreateService((request, _) =>
        {
            Assert.True(request.Headers.IfNoneMatch.Count > 0);
            return Task.FromResult(JsonResponse(HttpStatusCode.NotModified, string.Empty));
        });

        cache.Set(BlueprintUrl(1), "\"v1\"", cachedEntries, DateTime.UtcNow.AddHours(1));

        var result = await service.GetCharacterBlueprintsAsync(CharacterId);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Count);
    }

    [Fact]
    public async Task GetCharacterBlueprints_ErrorLimit420_ReturnsNull()
    {
        var (service, _, _) = CreateService((_, _) =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)420)
            {
                Content = new StringContent("{\"error\":\"error limit reached\"}", Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        });

        var result = await service.GetCharacterBlueprintsAsync(CharacterId);

        Assert.Null(result);
    }
}