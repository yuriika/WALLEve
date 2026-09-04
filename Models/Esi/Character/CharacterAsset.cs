using System.Text.Json.Serialization;

namespace WALLEve.Models.Esi.Character;

/// <summary>
/// Ein Asset-Item aus ESI. GET /characters/{character_id}/assets/
/// </summary>
public class CharacterAsset
{
    [JsonPropertyName("item_id")]
    public long ItemId { get; set; }

    [JsonPropertyName("type_id")]
    public int TypeId { get; set; }

    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }

    [JsonPropertyName("location_id")]
    public long LocationId { get; set; }

    [JsonPropertyName("location_type")]
    public string LocationType { get; set; } = string.Empty;

    [JsonPropertyName("location_flag")]
    public string LocationFlag { get; set; } = string.Empty;

    [JsonPropertyName("is_singleton")]
    public bool IsSingleton { get; set; }

    [JsonPropertyName("is_blueprint_copy")]
    public bool? IsBlueprintCopy { get; set; }

    [JsonIgnore]
    public string? TypeName { get; set; }

    [JsonIgnore]
    public double? CurrentMarketValue { get; set; }

    [JsonIgnore]
    public double? CostBasis { get; set; }
}