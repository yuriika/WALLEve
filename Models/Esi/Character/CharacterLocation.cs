using System.Text.Json.Serialization;

namespace WALLEve.Models.Esi.Character;

public class CharacterLocation
{
    [JsonPropertyName("solar_system_id")]
    public int SolarSystemId { get; set; }

    [JsonPropertyName("station_id")]
    public int? StationId { get; set; }

    [JsonPropertyName("structure_id")]
    public long? StructureId { get; set; }
}
