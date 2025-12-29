using System.Text.Json.Serialization;

namespace WALLEve.Models.Esi.Markets;

/// <summary>
/// Represents a regional market order from ESI API
/// GET /markets/{region_id}/orders/
/// </summary>
public class RegionalMarketOrder
{
    [JsonPropertyName("order_id")]
    public long OrderId { get; set; }

    [JsonPropertyName("type_id")]
    public int TypeId { get; set; }

    [JsonPropertyName("location_id")]
    public long LocationId { get; set; }

    [JsonPropertyName("system_id")]
    public int SystemId { get; set; }

    [JsonPropertyName("volume_total")]
    public int VolumeTotal { get; set; }

    [JsonPropertyName("volume_remain")]
    public int VolumeRemain { get; set; }

    [JsonPropertyName("min_volume")]
    public int MinVolume { get; set; }

    [JsonPropertyName("price")]
    public double Price { get; set; }

    [JsonPropertyName("is_buy_order")]
    public bool IsBuyOrder { get; set; }

    [JsonPropertyName("duration")]
    public int Duration { get; set; }

    [JsonPropertyName("issued")]
    public DateTime Issued { get; set; }

    /// <summary>
    /// Range of the order: "station", "region", "solarsystem", "1", "2", "3", "4", "5", "10", "20", "30", "40"
    /// </summary>
    [JsonPropertyName("range")]
    public string Range { get; set; } = string.Empty;
}
