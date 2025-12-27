using System.Text.Json.Serialization;

namespace WALLEve.Models.Esi.Markets;

public class MarketOrderHistory
{
    [JsonPropertyName("duration")]
    public int Duration { get; set; }

    [JsonPropertyName("escrow")]
    public double? Escrow { get; set; }

    [JsonPropertyName("is_buy_order")]
    public bool IsBuyOrder { get; set; }

    [JsonPropertyName("is_corporation")]
    public bool IsCorporation { get; set; }

    [JsonPropertyName("issued")]
    public DateTime Issued { get; set; }

    [JsonPropertyName("location_id")]
    public long LocationId { get; set; }

    [JsonPropertyName("min_volume")]
    public int? MinVolume { get; set; }

    [JsonPropertyName("order_id")]
    public long OrderId { get; set; }

    [JsonPropertyName("price")]
    public double Price { get; set; }

    [JsonPropertyName("range")]
    public string Range { get; set; } = string.Empty;

    [JsonPropertyName("region_id")]
    public int RegionId { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty; // "cancelled", "expired", "fulfilled"

    [JsonPropertyName("type_id")]
    public int TypeId { get; set; }

    [JsonPropertyName("volume_remain")]
    public int VolumeRemain { get; set; }

    [JsonPropertyName("volume_total")]
    public int VolumeTotal { get; set; }
}
