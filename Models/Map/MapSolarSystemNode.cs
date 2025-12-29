namespace WALLEve.Models.Map;

/// <summary>
/// Solar System Node für System-View und Local-Environment-View
/// </summary>
public class MapSolarSystemNode
{
    public int SolarSystemId { get; set; }
    public string Name { get; set; } = string.Empty;
    public float Security { get; set; }
    public int RegionId { get; set; }
    public string RegionName { get; set; } = string.Empty;
    public int ConstellationId { get; set; }
    public string ConstellationName { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }

    /// <summary>
    /// Security-Klasse (highsec, lowsec, nullsec)
    /// </summary>
    public string SecurityClass => Security switch
    {
        >= 0.5f => "highsec",
        >= 0.1f => "lowsec",
        _ => "nullsec"
    };
}
