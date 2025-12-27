using System.Text.Json.Serialization;

namespace WALLEve.Models.Esi.Character;

public class CharacterShip
{
    [JsonPropertyName("ship_item_id")]
    public long ShipItemId { get; set; }

    [JsonPropertyName("ship_name")]
    public string ShipName { get; set; } = string.Empty;

    [JsonPropertyName("ship_type_id")]
    public int ShipTypeId { get; set; }
}
