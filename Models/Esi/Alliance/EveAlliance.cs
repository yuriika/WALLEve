using System.Text.Json.Serialization;

namespace WALLEve.Models.Esi.Alliance;

public class EveAlliance
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("ticker")]
    public string Ticker { get; set; } = string.Empty;

    [JsonPropertyName("creator_id")]
    public int CreatorId { get; set; }

    [JsonPropertyName("creator_corporation_id")]
    public int CreatorCorporationId { get; set; }

    [JsonPropertyName("executor_corporation_id")]
    public int? ExecutorCorporationId { get; set; }

    [JsonPropertyName("date_founded")]
    public DateTime DateFounded { get; set; }
}
