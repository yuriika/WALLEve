namespace WALLEve.Models.Map;

/// <summary>
/// Aktueller Zustand der Karte
/// </summary>
public class MapState
{
    public MapViewMode ViewMode { get; set; } = MapViewMode.LocalEnvironment;
    public int? SelectedRegionId { get; set; }
    public int? CurrentCharacterSystemId { get; set; }
    public int JumpRadius { get; set; } = 5;
    public RouteResult? ActiveRoute { get; set; }
    public RouteComparisonResult? RouteComparison { get; set; }
    public bool ShowStatistics { get; set; } = false;
    public bool ShowJumps { get; set; } = false;
    public bool ShowKills { get; set; } = false;
}
