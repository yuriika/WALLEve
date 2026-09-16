using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using WALLEve.Configuration;
using WALLEve.Services.Risk.Interfaces;

namespace WALLEve.Services.Risk;

/// <summary>
/// zKillboard-Adapter (#73): liefert Verlustzahlen je System über den
/// Losses-Endpunkt (https://zkillboard.com/api/losses/systemID/{id}/).
///
/// Eigenschaften des Adapters:
/// - User-Agent und Compression (GZip/Deflate) über den registrierten
///   benannten HttpClient ("Zkillboard").
/// - Lokaler Cache: identische Systemabfragen werden innerhalb der TTL
///   ohne HTTP-Request beantwortet.
/// - Request-Abstand: reale HTTP-Requests werden auf einen Mindestabstand
///   serialisiert (Semaphore + Time-Gate).
/// - Provider-Health: nach FailureThreshold Fehlschlägen in Folge gilt der
///   Provider für UnhealthyCooldownSeconds als ungesund; Requests werden
///   in dieser Zeit ohne HTTP-Aufruf abgewiesen (fail fast).
///
/// Der Adapter wirft nie: Rate-Limit, Timeout, fehlende oder ungültige Daten
/// ergeben null (= Quelle nicht verfügbar) und werden protokolliert.
/// </summary>
public sealed class ZkillboardClient : IZkillboardClient
{
    private readonly HttpClient _httpClient;
    private readonly ZkillboardSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly ILogger<ZkillboardClient> _logger;

    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private long _lastRequestTimestamp;

    private int _consecutiveFailures;
    private DateTime? _unhealthyUntilUtc;

    public ZkillboardClient(
        HttpClient httpClient,
        IOptions<ZkillboardSettings> settings,
        IMemoryCache cache,
        ILogger<ZkillboardClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _cache = cache;
        _logger = logger;
    }

    public bool IsEnabled => _settings.Enabled;

    /// <summary>Wahr, solange der Provider im Fehler-Cooldown ist (kein HTTP).</summary>
    public bool IsInCooldown => _unhealthyUntilUtc.HasValue && DateTime.UtcNow < _unhealthyUntilUtc.Value;

    public async Task<ZkillboardLosses?> GetLossesAsync(int systemId, CancellationToken ct = default)
    {
        if (!_settings.Enabled)
        {
            _logger.LogDebug("ZkillboardClient: Quelle deaktiviert, System {SystemId} übersprungen", systemId);
            return null;
        }

        var cacheKey = $"zkb:losses:{systemId}";
        if (_cache.TryGetValue(cacheKey, out ZkillboardLosses? cached) && cached != null)
        {
            return cached;
        }

        if (IsInCooldown)
        {
            _logger.LogDebug("ZkillboardClient: Provider im Cooldown, Request für System {SystemId} übersprungen", systemId);
            return null;
        }

        try
        {
            using var response = await SendWithSpacingAsync(systemId, ct);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                RecordFailure($"rate-limit for system {systemId}");
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                RecordFailure($"http-{(int)response.StatusCode} for system {systemId}");
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(content))
            {
                RecordFailure($"empty response for system {systemId}");
                return null;
            }

            // zKillboard liefert ein JSON-Array von Killmails; die Verlustzahl
            // ist die Arraylänge. Eine valide leere Antwort ([]) ist ein echtes
            // Datum (0 Verluste), kein Fehler.
            long count = 0;
            using (var doc = JsonDocument.Parse(content))
            {
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    count = doc.RootElement.GetArrayLength();
                }
                else
                {
                    RecordFailure($"malformed response for system {systemId}");
                    return null;
                }
            }

            var losses = new ZkillboardLosses
            {
                SystemId = systemId,
                Losses = (int)Math.Min(count, int.MaxValue),
                CollectedAt = DateTimeOffset.UtcNow,
                IsDelayed = true
            };

            ResetHealth();

            var ttl = TimeSpan.FromMinutes(Math.Max(1, _settings.CacheTtlMinutes));
            _cache.Set(cacheKey, losses, ttl);

            _logger.LogDebug("ZkillboardClient: {Losses} Verluste für System {SystemId}", losses.Losses, systemId);
            return losses;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Caller-Cancellation durchreichen — nie als Provider-Fehler verbuchen.
            throw;
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // Timeout des HttpClients (kein Caller-Cancel).
            RecordFailure($"timeout for system {systemId}: {ex.Message}");
            return null;
        }
        catch (HttpRequestException ex)
        {
            RecordFailure($"request failed for system {systemId}: {ex.Message}");
            return null;
        }
        catch (JsonException ex)
        {
            RecordFailure($"ungültiges JSON für System {systemId}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Serialisiert reale HTTP-Requests auf den konfigurierten Mindestabstand.
    /// Cache-Treffer passieren dieses Gate nicht (sie stellen keinen Request).
    /// </summary>
    private async Task<HttpResponseMessage> SendWithSpacingAsync(int systemId, CancellationToken ct)
    {
        await _requestGate.WaitAsync(ct);
        try
        {
            var minInterval = TimeSpan.FromMilliseconds(Math.Max(0, _settings.MinRequestIntervalMs));
            var elapsed = Stopwatch.GetElapsedTime(_lastRequestTimestamp);
            if (elapsed < minInterval)
            {
                await Task.Delay(minInterval - elapsed, ct);
            }

            _lastRequestTimestamp = Stopwatch.GetTimestamp();

            var url = $"{_settings.BaseUrl.TrimEnd('/')}/api/losses/systemID/{systemId}/";
            return await _httpClient.GetAsync(url, ct);
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private void RecordFailure(string reason)
    {
        _consecutiveFailures++;
        _logger.LogWarning("ZkillboardClient: Fehlschlag {Count}/{Threshold} ({Reason})",
            _consecutiveFailures, _settings.FailureThreshold, reason);

        if (_consecutiveFailures >= Math.Max(1, _settings.FailureThreshold))
        {
            _unhealthyUntilUtc = DateTime.UtcNow.AddSeconds(Math.Max(1, _settings.UnhealthyCooldownSeconds));
            _logger.LogWarning("ZkillboardClient: Provider gilt als ungesund bis {Until} (Cooldown)",
                _unhealthyUntilUtc.Value);
        }
    }

    private void ResetHealth()
    {
        _consecutiveFailures = 0;
        _unhealthyUntilUtc = null;
    }
}