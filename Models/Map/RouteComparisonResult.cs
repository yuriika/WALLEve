namespace WALLEve.Models.Map;

/// <summary>
/// Vergleich zwischen lokalem und ESI-Routing
/// </summary>
public class RouteComparisonResult
{
    public RouteResult LocalRoute { get; set; } = new();
    public RouteResult EsiRoute { get; set; } = new();
    public bool RoutesMatch { get; set; }
    public int JumpDifference { get; set; }
    public List<int> LocalOnlySystemIds { get; set; } = new();
    public List<int> EsiOnlySystemIds { get; set; } = new();
}
