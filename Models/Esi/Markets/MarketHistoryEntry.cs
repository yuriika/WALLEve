using System.Text.Json.Serialization;

namespace WALLEve.Models.Esi.Markets;

/// <summary>
/// Represents a historical market statistics entry from ESI API
/// GET /markets/{region_id}/history/
/// </summary>
public class MarketHistoryEntry
{
    [JsonPropertyName("date")]
    public DateTime Date { get; set; }

    [JsonPropertyName("average")]
    public double Average { get; set; }

    [JsonPropertyName("highest")]
    public double Highest { get; set; }

    [JsonPropertyName("lowest")]
    public double Lowest { get; set; }

    [JsonPropertyName("volume")]
    public long Volume { get; set; }

    [JsonPropertyName("order_count")]
    public long OrderCount { get; set; }
}
