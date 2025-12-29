namespace WALLEve.Models.Map;

/// <summary>
/// Ergebnis einer Routenberechnung
/// </summary>
public class RouteResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<int>? Path { get; set; }
    public List<MapSolarSystemNode>? Systems { get; set; }
    public int TotalJumps { get; set; }
    public float AverageSecurity { get; set; }
    public int HighSecJumps { get; set; }
    public int LowSecJumps { get; set; }
    public int NullSecJumps { get; set; }
}
