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

    /// <summary>
    /// Besitzer-Charakter (Owner). Kein ESI-Feld — wird beim Abruf
    /// (GetCharacterAssetsAsync) auf die abfragende CharacterId gesetzt,
    /// damit jede Rohzeile ihren Owner verlustfrei behält.
    /// </summary>
    [JsonIgnore]
    public int OwnerCharacterId { get; set; }

    /// <summary>
    /// Parent/Container-ItemId: Bei location_type "item" ist location_id die
    /// ItemId des Containers (ESI-Semantik). Rein aus den ESI-Feldern abgeleitet —
    /// keine erfundene Zuordnung; unaufgelöste IDs bleiben als Rohwert erhalten.
    /// </summary>
    [JsonIgnore]
    public long? ParentItemId => LocationType == "item" ? LocationId : null;
}