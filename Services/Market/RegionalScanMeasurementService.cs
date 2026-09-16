using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;
using WALLEve.Models.Configuration;
using WALLEve.Models.Measurement;
using WALLEve.Services.Esi.Interfaces;
using WALLEve.Services.Market.Interfaces;
using WALLEve.Services.Sde.Interfaces;

namespace WALLEve.Services.Market;

/// <summary>
/// Führt einen begrenzten, cachekonformen Messlauf des Regionalscans aus (#67):
/// ruft für jede repräsentative Region den ECHTEN App-Pfad
/// (<c>GetAllRegionalMarketOrdersAsync</c> — inklusive ETag/304-Cache, gestaffelter
/// Parallelität und Atomicität) auf und erfasst Seiten, Orders, übertragene Bytes,
/// Dauer, SQLite-Wachstum und Fehlerbudget. Per Region läuft zusätzlich eine einzelne
/// Gzip-Probe (eine Zusatzanfrage), um die komprimierte Transfergröße zu belegen.
/// Das Ergebnis wird als JSON-Artefakt (reproduzierbar, tokenfrei) persistiert.
/// KEIN Produktionscollector — nur ein explizit konfigurierter Einmallauf.
/// </summary>
public sealed class RegionalScanMeasurementService : IRegionalScanMeasurementService
{
    private static readonly JsonSerializerOptions ArtifactJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IEsiApiService _esi;
    private readonly ISdeUniverseService _sde;
    private readonly IHttpClientFactory? _httpClientFactory;
    private readonly RegionalScanMeasurementOptions _options;
    private readonly ILogger<RegionalScanMeasurementService> _logger;

    public RegionalScanMeasurementService(
        IEsiApiService esi,
        ISdeUniverseService sde,
        RegionalScanMeasurementOptions options,
        ILogger<RegionalScanMeasurementService> logger,
        IHttpClientFactory? httpClientFactory = null)
    {
        _esi = esi;
        _sde = sde;
        _options = options;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<RegionalScanMeasurementReport> RunAsync(int[] regionIds, CancellationToken ct = default)
    {
        _logger.LogInformation("RegionalScan-Messlauf gestartet: {Count} Regionen", regionIds.Length);
        var databaseBytesBefore = GetDatabaseBytes();

        var report = new RegionalScanMeasurementReport
        {
            EsiBaseUrl = _options.EsiBaseUrl,
            MeasuredAtUtc = DateTime.UtcNow,
            DatabasePath = _options.DatabasePath
        };

        foreach (var regionId in regionIds)
        {
            ct.ThrowIfCancellationRequested();
            report.Regions.Add(await MeasureRegionAsync(regionId, databaseBytesBefore, ct));
        }

        var databaseBytesAfter = GetDatabaseBytes();
        report.Notes.Add(
            $"SQLite-Datei gesamt: {databaseBytesBefore:N0} → {databaseBytesAfter:N0} Bytes " +
            $"({databaseBytesAfter - databaseBytesBefore:+#,0;-#,0;0} Bytes). Der Regionalscan selbst persistiert keine " +
            "Orderdaten — ein Wachstum während des Laufs stammt von parallelen Collectoren.");

        await PersistArtifactAsync(report, ct);
        return report;
    }

    private async Task<RegionalScanRegionResult> MeasureRegionAsync(
        int regionId, long databaseBytesBefore, CancellationToken ct)
    {
        var startedUtc = DateTime.UtcNow;
        var regionName = await GetRegionNameAsync(regionId);

        var telemetry = new List<RegionalScanPageTelemetry>();
        void Sink(RegionalScanPageTelemetry page)
        {
            lock (telemetry)
            {
                telemetry.Add(page);
            }
        }

        var completed = false;
        string? failureNote = null;
        long elapsedMs = 0;

        try
        {
            var stopwatch = Stopwatch.StartNew();
            var orders = await _esi.GetAllRegionalMarketOrdersAsync(regionId, orderType: "all", ct: ct, telemetrySink: Sink);
            stopwatch.Stop();
            elapsedMs = stopwatch.ElapsedMilliseconds;

            completed = orders != null;
            if (!completed)
            {
                failureNote = "Scan abgebrochen (atomar) — keine Teildaten (ESI-Fehler/Cancellation).";
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            failureNote = ex.Message;
            _logger.LogWarning(ex, "Regionalscan-Messung für Region {RegionId} fehlgeschlagen", regionId);
        }

        var probe = await ProbeGzipAsync(regionId, ct);

        return new RegionalScanRegionResult
        {
            RegionId = regionId,
            RegionName = regionName,
            StartedUtc = startedUtc,
            FinishedUtc = DateTime.UtcNow,
            Pages = telemetry.Count,
            CachedPages = telemetry.Count(t => t.FromCache),
            Orders = telemetry.Where(t => t.Success).Sum(t => t.OrderCount),
            BytesTransferred = telemetry.Where(t => t.ContentLength.HasValue).Sum(t => t.ContentLength!.Value),
            ElapsedMs = elapsedMs,
            DatabaseBytesBefore = databaseBytesBefore,
            DatabaseBytesAfter = GetDatabaseBytes(),
            FailedPages = telemetry.Count(t => !t.Success),
            Completed = completed,
            FailureNote = failureNote,
            GzipProbeCompressedBytes = probe?.Compressed,
            GzipProbeDecompressedBytes = probe?.Decompressed
        };
    }

    /// <summary>
    /// Misst die komprimierte Transfergröße der ersten Regionalscan-Seite mit
    /// explizitem <c>Accept-Encoding: gzip</c>. Eine einzelne, begrenzte Zusatzanfrage
    /// pro Region. null = Probe nicht möglich (kein HttpClient im Test, Netzfehler).
    /// </summary>
    private async Task<(long Compressed, long Decompressed)?> ProbeGzipAsync(int regionId, CancellationToken ct)
    {
        if (_httpClientFactory == null || !_options.ProbeGzip)
        {
            return null;
        }

        try
        {
            var client = _httpClientFactory.CreateClient("EveApi");
            var endpoint = $"{_options.EsiBaseUrl}/markets/{regionId}/orders/?order_type=all&page=1";
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            // Antwort vollständig in einen Puffer kopieren: der Netzwerk-Stream ist
            // nicht seekbar, Length() würde werfen. compressedBuffer.Length == komprimierte Bytes.
            await using var compressedStream = await response.Content.ReadAsStreamAsync(ct);
            using var compressedBuffer = new MemoryStream();
            await compressedStream.CopyToAsync(compressedBuffer, ct);
            compressedBuffer.Position = 0;

            using var decompressed = new MemoryStream();
            using (var gzipStream = new GZipStream(compressedBuffer, CompressionMode.Decompress, leaveOpen: true))
            {
                await gzipStream.CopyToAsync(decompressed, ct);
            }

            return (compressedBuffer.Length, decompressed.Length);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gzip-Probe für Region {RegionId} fehlgeschlagen", regionId);
            return null;
        }
    }

    private async Task<string> GetRegionNameAsync(int regionId)
    {
        try
        {
            return await _sde.GetRegionNameAsync(regionId) ?? $"Region {regionId}";
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Regionsname für {RegionId} nicht verfügbar", regionId);
            return $"Region {regionId}";
        }
    }

    private long GetDatabaseBytes()
    {
        if (string.IsNullOrWhiteSpace(_options.DatabasePath))
        {
            return 0;
        }

        try
        {
            return new FileInfo(_options.DatabasePath).Length;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SQLite-Dateigröße nicht lesbar: {Path}", _options.DatabasePath);
            return 0;
        }
    }

    private async Task PersistArtifactAsync(RegionalScanMeasurementReport report, CancellationToken ct)
    {
        var directory = string.IsNullOrWhiteSpace(_options.ArtifactDirectory)
            ? AppContext.BaseDirectory
            : _options.ArtifactDirectory;
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, "regional-scan-measurement.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(report, ArtifactJsonOptions), ct);
        _logger.LogInformation("RegionalScan-Messartefakt geschrieben: {Path}", path);
    }
}