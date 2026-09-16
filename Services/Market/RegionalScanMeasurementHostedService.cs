using WALLEve.Models.Configuration;
using WALLEve.Services.Market.Interfaces;

namespace WALLEve.Services.Market;

/// <summary>
/// Führt den konfigurierten Regionalscan-Messlauf (#67) beim App-Start genau einmal
/// aus. Nur registriert, wenn <c>Measurement:RegionalScan:Enabled=true</c> — kein
/// Produktionscollector, keine Wiederholung.
/// </summary>
public class RegionalScanMeasurementHostedService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly RegionalScanMeasurementSettings _settings;
    private readonly ILogger<RegionalScanMeasurementHostedService> _logger;

    public RegionalScanMeasurementHostedService(
        IServiceProvider services,
        RegionalScanMeasurementSettings settings,
        ILogger<RegionalScanMeasurementHostedService> logger)
    {
        _services = services;
        _settings = settings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Kurz warten, bis die App vollständig gestartet ist.
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        using var scope = _services.CreateScope();
        var measurement = scope.ServiceProvider.GetRequiredService<IRegionalScanMeasurementService>();

        try
        {
            var report = await measurement.RunAsync(_settings.Regions, stoppingToken);
            var evaluation = RegionalScanMeasurementEvaluator.Evaluate(report.Regions);

            if (evaluation.CompletedRegionCount == 0)
            {
                _logger.LogError(
                    "RegionalScan-Messlauf ohne vollständige Messung beendet ({FailedPages} fehlgeschlagene Seiten) — " +
                    "es werden KEINE erfundenen Zahlen dokumentiert. Artefakt: {Artifact}",
                    evaluation.TotalFailedPages, Path.Combine(
                        string.IsNullOrWhiteSpace(GetArtifactDirectory()) ? AppContext.BaseDirectory : GetArtifactDirectory(),
                        "regional-scan-measurement.json"));
            }
            else
            {
                _logger.LogInformation(
                    "RegionalScan-Messlauf abgeschlossen: {Regions} vollständige Regionen, {Pages} Seiten, " +
                    "{Orders} Orders, {Bytes:N0} Bytes, max. {MaxMs} ms, Fehlerbudget {Budget} %, " +
                    "SQLite {DbBefore:N0} → {DbAfter:N0} Bytes",
                    evaluation.CompletedRegionCount, evaluation.TotalPages, evaluation.TotalOrders,
                    evaluation.TotalBytes, evaluation.MaxElapsedMsObserved, evaluation.ErrorBudgetPercent,
                    report.Regions.FirstOrDefault()?.DatabaseBytesBefore ?? 0,
                    report.Regions.FirstOrDefault()?.DatabaseBytesAfter ?? 0);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("RegionalScan-Messlauf abgebrochen (Shutdown).");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "RegionalScan-Messlauf fehlgeschlagen — es werden KEINE erfundenen Zahlen dokumentiert.");
        }
    }

    private string GetArtifactDirectory()
        => _services.GetService<RegionalScanMeasurementOptions>()?.ArtifactDirectory ?? "";
}