using System.Text.Json.Serialization;

namespace WALLEve.Models.Esi.Corporation;

public class EveCorporation
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("ticker")]
    public string Ticker { get; set; } = string.Empty;

    [JsonPropertyName("member_count")]
    public int MemberCount { get; set; }

    [JsonPropertyName("alliance_id")]
    public int? AllianceId { get; set; }

    [JsonPropertyName("ceo_id")]
    public int CeoId { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("date_founded")]
    public DateTime? DateFounded { get; set; }

    [JsonPropertyName("tax_rate")]
    public float TaxRate { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}
