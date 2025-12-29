namespace WALLEve.Models.Map;

/// <summary>
/// Live-Statistiken für ein System (von ESI)
/// </summary>
public class SystemStatistics
{
    public int SystemId { get; set; }
    public int Jumps { get; set; }
    public int ShipKills { get; set; }
    public int NpcKills { get; set; }
    public int PodKills { get; set; }

    /// <summary>
    /// Gesamt-Kills (Ships + NPCs + Pods)
    /// </summary>
    public int TotalKills => ShipKills + NpcKills + PodKills;
}
