using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using WALLEve.Models.Configuration;
using WALLEve.Models.Esi.Alliance;
using WALLEve.Models.Esi.Character;
using WALLEve.Models.Esi.Corporation;
using WALLEve.Models.Esi.Markets;
using WALLEve.Models.Esi.Universe;
using WALLEve.Models.Measurement;
using WALLEve.Models.Sde;
using WALLEve.Services.Esi.Interfaces;
using WALLEve.Services.Market;
using WALLEve.Services.Sde.Interfaces;

namespace WALLEve.Tests;

/// <summary>
/// Offline-Tests für die Regionalscan-Messung (#67): Der Evaluator leitet
/// Grenzempfehlungen ausschließlich aus beobachteten Messdaten ab (niemals
/// geratene Limits), und der Messdienst aggregiert Seitentelemetrie ohne
/// Live-ESI — blockierter Zugriff wird als unvollständig ausgewiesen, nie
/// durch erfundene Zahlen ersetzt.
/// </summary>
public class RegionalScanMeasurementTests
{
    // ------------------------------------------------------------------
    // Evaluator (rein, offline)
    // ------------------------------------------------------------------

    [Fact]
    public void Evaluate_DerivesEnvelopeFromCompletedRegionsOnly()
    {
        var results = new List<RegionalScanRegionResult>
        {
            new() { RegionId = 1, Pages = 10, Orders = 100, BytesTransferred = 100_000, ElapsedMs = 5_000, Completed = true },
            new() { RegionId = 2, Pages = 20, Orders = 300, BytesTransferred = 250_000, ElapsedMs = 12_000, Completed = true },
            // Unvollständige Region: nicht in der Hülle, aber das Fehlerbudget zählt mit.
            new() { RegionId = 3, Pages = 5, Completed = false, FailedPages = 2 }
        };

        var recommendations = RegionalScanMeasurementEvaluator.Evaluate(results);

        Assert.Equal(2, recommendations.CompletedRegionCount);
        Assert.Equal(30, recommendations.TotalPages);
        Assert.Equal(400, recommendations.TotalOrders);
        Assert.Equal(350_000, recommendations.TotalBytes);
        Assert.Equal(20, recommendations.MaxPagesObserved);
        Assert.Equal(300, recommendations.MaxOrdersObserved);
        Assert.Equal(250_000, recommendations.MaxBytesObserved);
        Assert.Equal(12_000, recommendations.MaxElapsedMsObserved);
        Assert.Equal(2, recommendations.TotalFailedPages);
        Assert.Equal(2 * 100.0 / 30, recommendations.ErrorBudgetPercent, 3);
        Assert.Contains("gemessene Envelope", recommendations.Note);
    }

    [Fact]
    public void Evaluate_NoCompletedRegions_RecommendsNoLimits()
    {
        var recommendations = RegionalScanMeasurementEvaluator.Evaluate(
            new List<RegionalScanRegionResult>
            {
                new() { RegionId = 1, Pages = 4, Completed = false, FailedPages = 4 }
            });

        Assert.Equal(0, recommendations.CompletedRegionCount);
        Assert.Equal(0, recommendations.TotalPages);
        Assert.Equal(0, recommendations.MaxPagesObserved);
        Assert.Equal(0, recommendations.MaxBytesObserved);
        Assert.Equal(4, recommendations.TotalFailedPages);
        Assert.Contains("keine Grenzwerte empfohlen", recommendations.Note);
    }

    [Fact]
    public void Evaluate_ZeroFailedPages_IsZeroPercentBudget()
    {
        var recommendations = RegionalScanMeasurementEvaluator.Evaluate(
            new List<RegionalScanRegionResult>
            {
                new() { RegionId = 1, Pages = 8, Completed = true }
            });

        Assert.Equal(0, recommendations.ErrorBudgetPercent);
    }

    // ------------------------------------------------------------------
    // Messdienst (offline, Fake-Esi)
    // ------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_AggregatesTelemetry_AndWritesTokenFreeArtifact()
    {
        var dir = Path.Combine(Path.GetTempPath(), "walleve-measurement-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var dbPath = Path.Combine(dir, "wallet.db");
            await File.WriteAllTextAsync(dbPath, new string('x', 4096)); // 4 KiB beobachtete SQLite-Größe

            var options = new RegionalScanMeasurementOptions
            {
                DatabasePath = dbPath,
                ArtifactDirectory = dir,
                EsiBaseUrl = "https://esi.evetech.net/latest",
                ProbeGzip = false
            };
            var service = new RegionalScanMeasurementService(
                new FakeEsiApiService(), new FakeSde(), options,
                NullLogger<RegionalScanMeasurementService>.Instance);

            var report = await service.RunAsync(new[] { 10000002 });

            var region = Assert.Single(report.Regions);
            Assert.Equal("The Forge", region.RegionName);
            Assert.Equal(2, region.Pages);
            Assert.Equal(1, region.CachedPages);
            Assert.Equal(3, region.Orders);
            Assert.Equal(1000, region.BytesTransferred);
            Assert.Equal(4096, region.DatabaseBytesBefore);
            Assert.Equal(4096, region.DatabaseBytesAfter);
            Assert.Equal(0, region.FailedPages);
            Assert.True(region.Completed);
            Assert.Null(region.GzipProbeCompressedBytes); // ProbeGzip=false im Test

            var artifactPath = Path.Combine(dir, "regional-scan-measurement.json");
            Assert.True(File.Exists(artifactPath), "Artefakt muss geschrieben werden");

            var json = await File.ReadAllTextAsync(artifactPath);
            Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Bearer", json);
            Assert.Contains("esi.evetech.net", json);
            Assert.Contains("10000002", json);

            // Reproduzierbarkeit: Quelle + Zeitraum sind im Artefakt enthalten.
            Assert.Contains("Regionalscan-Messung", report.Source);
            Assert.NotEqual(default, report.MeasuredAtUtc);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_FailedScan_MarksIncomplete_NoInventedNumbers()
    {
        var dir = Path.Combine(Path.GetTempPath(), "walleve-measurement-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var options = new RegionalScanMeasurementOptions
            {
                DatabasePath = Path.Combine(dir, "wallet.db"),
                ArtifactDirectory = dir,
                ProbeGzip = false
            };
            var service = new RegionalScanMeasurementService(
                new FakeEsiApiService { Result = null }, new FakeSde(), options,
                NullLogger<RegionalScanMeasurementService>.Instance);

            var report = await service.RunAsync(new[] { 10000002 });

            var region = Assert.Single(report.Regions);
            Assert.False(region.Completed);
            Assert.NotNull(region.FailureNote);
            // Die Telemetrie zählt wirklich versuchte/übertragene Seiten (3 Orders wurden
            // transportiert), obwohl der Scan atomar kein Ergebnis publiziert hat.
            Assert.Equal(3, region.Orders);

            var recommendations = RegionalScanMeasurementEvaluator.Evaluate(report.Regions);
            Assert.Equal(0, recommendations.CompletedRegionCount);
            Assert.Contains("keine Grenzwerte empfohlen", recommendations.Note);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_FailedPage_CountsErrorBudget()
    {
        var fake = new FakeEsiApiService
        {
            Result = null, // atomarer Abbruch bei Seitenfehler
            EmitFailedPage = true
        };
        var dir = Path.Combine(Path.GetTempPath(), "walleve-measurement-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var service = new RegionalScanMeasurementService(
                fake, new FakeSde(),
                new RegionalScanMeasurementOptions
                {
                    DatabasePath = Path.Combine(dir, "wallet.db"),
                    ArtifactDirectory = dir,
                    ProbeGzip = false
                },
                NullLogger<RegionalScanMeasurementService>.Instance);

            var report = await service.RunAsync(new[] { 10000002 });

            var region = Assert.Single(report.Regions);
            Assert.False(region.Completed);
            Assert.Equal(1, region.FailedPages);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Settings_BindFromCommittedAppSettings_HasFiveRegions()
    {
        // Verhindert Regressions-Schleifen: Der Messlauf darf je Prozess genau die
        // konfigurierte Regionenliste durchlaufen (hier: 5 repräsentative Regionen).
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var config = new ConfigurationBuilder()
            .SetBasePath(repoRoot)
            .AddJsonFile("appsettings.json")
            .Build();
        var settings = config.GetSection("Measurement:RegionalScan")
            .Get<RegionalScanMeasurementSettings>() ?? new();

        Assert.False(settings.Enabled);
        Assert.Equal(5, settings.Regions.Length);
        Assert.Contains(10000002, settings.Regions); // The Forge
    }

    [Fact]
    public async Task RunAsync_GzipProbe_MeasuresCompressedAndDecompressedBytes()
    {
        // Der Probe-Pfad (Accept-Encoding: gzip) wird gegen einen Stub-Handler
        // gemessen: komprimierte < dekomprimierte Bytes — echte Messwerte statt null.
        var dir = Path.Combine(Path.GetTempPath(), "walleve-measurement-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var options = new RegionalScanMeasurementOptions
            {
                DatabasePath = Path.Combine(dir, "wallet.db"),
                ArtifactDirectory = dir,
                EsiBaseUrl = "https://esi.evetech.net/latest",
                ProbeGzip = true
            };
            var service = new RegionalScanMeasurementService(
                new FakeEsiApiService(), new FakeSde(), options,
                NullLogger<RegionalScanMeasurementService>.Instance,
                new GzipStubHttpClientFactory());

            var report = await service.RunAsync(new[] { 10000002 });

            var region = Assert.Single(report.Regions);
            Assert.NotNull(region.GzipProbeCompressedBytes);
            Assert.NotNull(region.GzipProbeDecompressedBytes);
            Assert.True(region.GzipProbeCompressedBytes > 0);
            Assert.True(region.GzipProbeDecompressedBytes > 100);
            Assert.True(region.GzipProbeCompressedBytes < region.GzipProbeDecompressedBytes,
                "gzip muss kleiner als der dekomprimierte Inhalt sein");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ------------------------------------------------------------------
    // Fakes
    // ------------------------------------------------------------------

    private sealed class GzipStubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new GzipStubHandler());

        private sealed class GzipStubHandler : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var payload = string.Join(';', Enumerable.Repeat("{\"order_id\":1}", 20_000));
                var compressed = await GzipAsync(payload);
                var response = new HttpResponseMessage(HttpStatusCode.OK);
                response.Content = new ByteArrayContent(compressed);
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                return response;
            }

            private static async Task<byte[]> GzipAsync(string text)
            {
                await using var buffer = new MemoryStream();
                await using (var gzip = new GZipStream(buffer, CompressionMode.Compress, leaveOpen: true))
                {
                    var bytes = Encoding.UTF8.GetBytes(text);
                    await gzip.WriteAsync(bytes, CancellationToken.None);
                }
                return buffer.ToArray();
            }
        }
    }

    private sealed class FakeEsiApiService : IEsiApiService
    {
        public List<RegionalMarketOrder>? Result { get; set; } = new();

        /// <summary>Seite 2 als fehlgeschlagen melden (Success=false).</summary>
        public bool EmitFailedPage { get; set; }

        public Task<List<RegionalMarketOrder>?> GetAllRegionalMarketOrdersAsync(
            int regionId, int? typeId = null, string orderType = "all",
            CancellationToken ct = default, Action<RegionalScanPageTelemetry>? telemetrySink = null)
        {
            telemetrySink?.Invoke(new RegionalScanPageTelemetry
            {
                RegionId = regionId, Page = 1, OrderCount = 2, ContentLength = 1000,
                FromCache = false, Success = true
            });
            telemetrySink?.Invoke(new RegionalScanPageTelemetry
            {
                RegionId = regionId, Page = 2, OrderCount = 1, ContentLength = null,
                FromCache = true, Success = !EmitFailedPage
            });
            return Task.FromResult(Result);
        }

        public Task<List<MarketOrder>?> GetMarketOrdersAsync(int characterId) => throw new NotImplementedException();
        public Task<CharacterOverview?> GetCharacterOverviewAsync() => throw new NotImplementedException();
        public Task<EveCharacter?> GetCharacterAsync(int characterId) => throw new NotImplementedException();
        public Task<EveCorporation?> GetCorporationAsync(int corporationId) => throw new NotImplementedException();
        public Task<EveAlliance?> GetAllianceAsync(int allianceId) => throw new NotImplementedException();
        public Task<double?> GetWalletBalanceAsync(int characterId) => throw new NotImplementedException();
        public Task<CharacterLocation?> GetLocationAsync(int characterId) => throw new NotImplementedException();
        public Task<CharacterShip?> GetCurrentShipAsync(int characterId) => throw new NotImplementedException();
        public Task<CharacterOnlineStatus?> GetOnlineStatusAsync(int characterId) => throw new NotImplementedException();
        public Task<SolarSystem?> GetSolarSystemAsync(int systemId) => throw new NotImplementedException();
        public Task<EveType?> GetTypeAsync(int typeId) => throw new NotImplementedException();
        public Task<StructureLookupResult> GetStructureAsync(long structureId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<CharacterSkills?> GetCharacterSkillsAsync() => throw new NotImplementedException();
        public Task<List<CharacterAsset>?> GetCharacterAssetsAsync(int characterId) => throw new NotImplementedException();

        public Task<List<CharacterMiningEntry>?> GetCharacterMiningLedgerAsync(int characterId, CancellationToken ct = default) => throw new NotImplementedException();

        public Task<List<CharacterBlueprint>?> GetCharacterBlueprintsAsync(int characterId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<CharacterIndustryJob>?> GetCharacterIndustryJobsAsync(int characterId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<RegionalMarketOrder>?> GetRegionalMarketOrdersAsync(int regionId, int? typeId = null, string orderType = "all", int page = 1) => throw new NotImplementedException();
        public Task<List<MarketHistoryEntry>?> GetMarketHistoryAsync(int regionId, int typeId) => throw new NotImplementedException();
        public Task<List<MarketPrice>?> GetMarketPricesAsync() => throw new NotImplementedException();
        public Task<List<WALLEve.Models.Esi.Wallet.WalletJournalEntry>?> GetWalletJournalAsync(int characterId, int page = 1) => throw new NotImplementedException();
        public Task<List<WALLEve.Models.Esi.Wallet.WalletTransaction>?> GetWalletTransactionsAsync(int characterId) => throw new NotImplementedException();
        public Task<List<WALLEve.Models.Esi.Wallet.WalletJournalEntry>?> GetAllWalletJournalPagesAsync(int characterId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<WALLEve.Models.Esi.Wallet.WalletTransaction>?> GetAllWalletTransactionsPagesAsync(int characterId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<MarketOrderHistory>?> GetMarketOrderHistoryAsync(int characterId) => throw new NotImplementedException();
        public Task<List<WALLEve.Models.Esi.Wallet.WalletJournalEntry>?> GetCorporationWalletJournalAsync(int corporationId, int division, int page = 1) => throw new NotImplementedException();
        public Task<List<WALLEve.Models.Esi.Wallet.WalletTransaction>?> GetCorporationWalletTransactionsAsync(int corporationId, int division) => throw new NotImplementedException();
        public Task<List<SystemJumps>?> GetSystemJumpsAsync() => throw new NotImplementedException();
        public Task<List<SystemKills>?> GetSystemKillsAsync() => throw new NotImplementedException();
    }

    private sealed class FakeSde : ISdeUniverseService
    {
        public Task<string?> GetRegionNameAsync(int regionId)
            => Task.FromResult<string?>(regionId == 10000002 ? "The Forge" : $"Region {regionId}");

        public Task<bool> IsDatabaseAvailableAsync() => Task.FromResult(true);
        public Task<string?> GetTypeNameAsync(int typeId) => throw new NotImplementedException();
        public Task<string?> GetTypeGroupAsync(int typeId) => throw new NotImplementedException();
        public Task<Dictionary<int, string?>> GetTypeGroupsAsync(IReadOnlyCollection<int> typeIds) => throw new NotImplementedException();
        public Task<SolarSystemInfo?> GetSolarSystemAsync(int solarSystemId) => throw new NotImplementedException();
        public Task<StationInfo?> GetStationAsync(long stationId) => throw new NotImplementedException();
        public Task<string?> GetLocationNameAsync(long locationId) => throw new NotImplementedException();
        public Task<int?> GetRegionIdForLocationAsync(long locationId) => throw new NotImplementedException();
        public Task<int?> GetSolarSystemIdForLocationAsync(long locationId) => throw new NotImplementedException();
        public Task<Dictionary<int, string>> GetAllMarketItemsAsync() => throw new NotImplementedException();
        public Task<Dictionary<int, string>> GetAllRegionsAsync() => throw new NotImplementedException();
        public Task<Dictionary<int, string>> SearchSolarSystemsAsync(string searchQuery, int maxResults = 10) => throw new NotImplementedException();
    }
}