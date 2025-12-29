using WALLEve.Models.Map;
using WALLEve.Services.Map.Interfaces;

namespace WALLEve.Services.Map;

/// <summary>
/// TODO: Vollständige Implementierung des Dijkstra-Algorithmus
/// Platzhalter-Implementierung für jetzt
/// </summary>
public class RouteCalculationService : IRouteCalculationService
{
    private readonly IMapDataService _mapData;
    private readonly ILogger<RouteCalculationService> _logger;

    public RouteCalculationService(
        IMapDataService mapData,
        ILogger<RouteCalculationService> logger)
    {
        _mapData = mapData;
        _logger = logger;
    }

    public Task<RouteResult> CalculateRouteLocalAsync(
        int originId,
        int destinationId,
        RoutingPreference preference)
    {
        _logger.LogWarning("RouteCalculationService: Local routing not yet implemented");
        return Task.FromResult(new RouteResult
        {
            Success = false,
            Error = "Local routing not yet implemented"
        });
    }

    public Task<RouteResult> CalculateRouteEsiAsync(
        int originId,
        int destinationId,
        RoutingPreference preference)
    {
        _logger.LogWarning("RouteCalculationService: ESI routing not yet implemented");
        return Task.FromResult(new RouteResult
        {
            Success = false,
            Error = "ESI routing not yet implemented"
        });
    }

    public Task<RouteComparisonResult> CalculateRouteComparisonAsync(
        int originId,
        int destinationId,
        RoutingPreference preference)
    {
        _logger.LogWarning("RouteCalculationService: Route comparison not yet implemented");
        return Task.FromResult(new RouteComparisonResult
        {
            RoutesMatch = false
        });
    }
}
