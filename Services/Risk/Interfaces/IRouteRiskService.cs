using WALLEve.Models.Risk;

namespace WALLEve.Services.Risk.Interfaces;

/// <summary>
/// Erhebt Risikoevidenz für eine Route (#73): ESI Jumps/Kills und
/// zKillboard-Verluste als getrennte Evidence mit Alter und Delayed-Hinweis.
///
/// Vertrag: Die Erhebung wirft nie. Jede nicht verfügbare Quelle (Rate-Limit,
/// Timeout, fehlende Daten, deaktiviert) erzeugt nicht verfügbare Evidence;
/// ein System ohne verfügbare Evidence gilt als unbekannt. Fehlt der
/// Verlustnachweis, ist die Route unbekannt — niemals automatisch sicher.
/// </summary>
public interface IRouteRiskService
{
    Task<RouteRiskSummary> CollectRouteRiskAsync(IReadOnlyList<int> systemIds, CancellationToken ct = default);
}