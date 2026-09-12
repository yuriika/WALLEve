namespace WALLEve.Models.Sde;

/// <summary>
/// NPC-Station aus der SDE-Tabelle staStations (stationID → Station).
/// </summary>
public class StationInfo
{
    public long StationId { get; set; }

    public string Name { get; set; } = string.Empty;

    public int SolarSystemId { get; set; }

    public int RegionId { get; set; }
}