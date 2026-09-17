using System.Text.Json.Serialization;

namespace WALLEve.Models.Esi.Character;

/// <summary>
/// Ein Blueprint-Item aus ESI (#49).
/// GET /characters/{character_id}/blueprints/ — Scope: esi-characters.read_blueprints.v1.
/// Die BPO/BPC-Semantik steckt in zwei Rohfeldern, die unabhängig voneinander
/// erhalten bleiben müssen: <c>runs</c> (-1 = Original/BPO-Sentinel, &gt;= 0 =
/// verbleibende Kopier-Runs) und <c>is_blueprint_copy</c> (boolesche
/// BPO/BPC-Unterscheidung). ME/TE und der Lagerort sind ebenso Rohwerte und
/// dürfen in der Persistenzschicht nicht umgerechnet oder normalisiert werden.
/// </summary>
public class CharacterBlueprint
{
    [JsonPropertyName("item_id")]
    public long ItemId { get; set; }

    [JsonPropertyName("type_id")]
    public int TypeId { get; set; }

    [JsonPropertyName("location_id")]
    public long LocationId { get; set; }

    [JsonPropertyName("location_flag")]
    public string LocationFlag { get; set; } = string.Empty;

    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }

    [JsonPropertyName("material_efficiency")]
    public int MaterialEfficiency { get; set; }

    [JsonPropertyName("time_efficiency")]
    public int TimeEfficiency { get; set; }

    /// <summary>Roh-Runs: -1 = BPO (Original), &gt;= 0 = verbleibende Runs eines BPC.</summary>
    [JsonPropertyName("runs")]
    public int Runs { get; set; }

    [JsonPropertyName("is_blueprint_copy")]
    public bool IsBlueprintCopy { get; set; }
}