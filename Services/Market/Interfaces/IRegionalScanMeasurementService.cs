using WALLEve.Models.Measurement;

namespace WALLEve.Services.Market.Interfaces;

/// <summary>
/// Führt den begrenzten, cachekonformen Regionalscan-Messlauf aus (#67).
/// </summary>
public interface IRegionalScanMeasurementService
{
    /// <summary>
    /// Misst den Regionalscan für alle angegebenen Regionen über den echten App-Pfad
    /// (ETag/304-Cache, gestaffelte Parallelität, Atomicität) und persistiert ein
    /// JSON-Artefakt (reproduzierbar, tokenfrei). Kein Live-Zugriff ersetzt fehlende
    /// Messdaten durch erfundene Zahlen — abgebrochene Regionen werden als solche
    /// ausgewiesen.
    /// </summary>
    Task<RegionalScanMeasurementReport> RunAsync(int[] regionIds, CancellationToken ct = default);
}