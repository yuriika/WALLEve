using WALLEve.Models.Map;

namespace WALLEve.Services.Map.Interfaces;

/// <summary>
/// Service für Routenberechnung (Dijkstra + ESI)
/// </summary>
public interface IRouteCalculationService
{
    /// <summary>
    /// Berechnet Route mit lokalem Dijkstra-Algorithmus
    /// </summary>
    Task<RouteResult> CalculateRouteLocalAsync(
        int originId,
        int destinationId,
        RoutingPreference preference);

    /// <summary>
    /// Berechnet Route mit ESI API
    /// </summary>
    Task<RouteResult> CalculateRouteEsiAsync(
        int originId,
        int destinationId,
        RoutingPreference preference);

    /// <summary>
    /// Berechnet beide Routen und vergleicht sie
    /// </summary>
    Task<RouteComparisonResult> CalculateRouteComparisonAsync(
        int originId,
        int destinationId,
        RoutingPreference preference);
}
