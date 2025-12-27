namespace WALLEve.Models.Sde;

/// <summary>
/// Informationen über ein Sonnensystem aus der SDE
/// </summary>
public class SolarSystemInfo
{
    public int SolarSystemId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string RegionName { get; set; } = string.Empty;
    public int RegionId { get; set; }
    public float Security { get; set; }

    public string SecurityClass => Security switch
    {
        >= 0.5f => "Highsec",
        >= 0.1f => "Lowsec",
        _ => "Nullsec"
    };
}
