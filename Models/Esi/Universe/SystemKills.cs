using System.Text.Json.Serialization;

namespace WALLEve.Models.Esi.Universe;

/// <summary>
/// ESI Response für /universe/system_kills/
/// </summary>
public class SystemKills
{
    [JsonPropertyName("system_id")]
    public int SystemId { get; set; }

    [JsonPropertyName("ship_kills")]
    public int ShipKills { get; set; }

    [JsonPropertyName("npc_kills")]
    public int NpcKills { get; set; }

    [JsonPropertyName("pod_kills")]
    public int PodKills { get; set; }
}
