using System.Text.Json.Serialization;

namespace WALLEve.Models.Esi.Universe;

public class SolarSystem
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("system_id")]
    public int SystemId { get; set; }

    [JsonPropertyName("constellation_id")]
    public int ConstellationId { get; set; }

    [JsonPropertyName("security_status")]
    public float SecurityStatus { get; set; }
}
