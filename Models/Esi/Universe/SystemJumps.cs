using System.Text.Json.Serialization;

namespace WALLEve.Models.Esi.Universe;

/// <summary>
/// ESI Response für /universe/system_jumps/
/// </summary>
public class SystemJumps
{
    [JsonPropertyName("system_id")]
    public int SystemId { get; set; }

    [JsonPropertyName("ship_jumps")]
    public int ShipJumps { get; set; }
}
