using System.Text.Json.Serialization;

namespace WALLEve.Models.Esi.Markets;

/// <summary>
/// Represents global market price information from ESI API
/// GET /markets/prices/
/// </summary>
public class MarketPrice
{
    [JsonPropertyName("type_id")]
    public int TypeId { get; set; }

    [JsonPropertyName("average_price")]
    public double? AveragePrice { get; set; }

    [JsonPropertyName("adjusted_price")]
    public double? AdjustedPrice { get; set; }
}
