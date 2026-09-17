using System.Text.Json.Serialization;

namespace WALLEve.Models.Esi.Character;

/// <summary>
/// Eine Ledger-Zeile des persönlichen Mining-Ledgers (#39).
/// GET /characters/{character_id}/mining/ — Scope: esi-industry.read_character_mining.v1.
/// Der ESI-Schlüssel ist (date, type_id, solar_system_id); quantity ist die
/// kumulierte Tagesmenge dieser Kombination. „date" ist ein ISO-Datum
/// (yyyy-MM-dd) und wird in der Persistenzschicht zu einem Datum normalisiert.
/// </summary>
public class CharacterMiningEntry
{
    [JsonPropertyName("date")]
    public string Date { get; set; } = string.Empty;

    [JsonPropertyName("quantity")]
    public long Quantity { get; set; }

    [JsonPropertyName("solar_system_id")]
    public int SolarSystemId { get; set; }

    [JsonPropertyName("type_id")]
    public int TypeId { get; set; }
}