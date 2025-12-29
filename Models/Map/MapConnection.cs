namespace WALLEve.Models.Map;

/// <summary>
/// Stargate-Verbindung zwischen Systemen
/// </summary>
public class MapConnection
{
    public int FromSystemId { get; set; }
    public int ToSystemId { get; set; }
    public int FromRegionId { get; set; }
    public int ToRegionId { get; set; }

    /// <summary>
    /// Name der Ziel-Region (für Cross-Region Connections)
    /// </summary>
    public string? ToRegionName { get; set; }

    /// <summary>
    /// Ist dies eine Cross-Region Verbindung?
    /// </summary>
    public bool IsCrossRegion => FromRegionId != ToRegionId;
}
