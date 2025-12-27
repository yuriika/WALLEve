using System.Text.Json.Serialization;

namespace WALLEve.Models.Esi.Character;

public class EveCharacter
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("birthday")]
    public DateTime Birthday { get; set; }

    [JsonPropertyName("gender")]
    public string Gender { get; set; } = string.Empty;

    [JsonPropertyName("race_id")]
    public int RaceId { get; set; }

    [JsonPropertyName("bloodline_id")]
    public int BloodlineId { get; set; }

    [JsonPropertyName("ancestry_id")]
    public int? AncestryId { get; set; }

    [JsonPropertyName("corporation_id")]
    public int CorporationId { get; set; }

    [JsonPropertyName("alliance_id")]
    public int? AllianceId { get; set; }

    [JsonPropertyName("security_status")]
    public float? SecurityStatus { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }
}
