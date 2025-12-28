namespace WALLEve.Models.Map;

/// <summary>
/// Region-Node für Region-View
/// </summary>
public class MapRegionNode
{
    public int RegionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public int SystemCount { get; set; }
    public List<int> BorderSystemIds { get; set; } = new();
}
