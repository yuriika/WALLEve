using WALLEve.Extensions;

namespace WALLEve.Models.Wallet;

public class MarketOrderViewModel
{
    public long OrderId { get; set; }
    public int TypeId { get; set; }
    public string TypeName { get; set; } = string.Empty;
    public bool IsBuyOrder { get; set; }
    public double Price { get; set; }
    public int VolumeRemain { get; set; }
    public int VolumeTotal { get; set; }
    public DateTime Issued { get; set; }
    public int Duration { get; set; }
    public long LocationId { get; set; }
    public string? LocationName { get; set; }
    public string State { get; set; } = "active"; // for history

    // Calculated properties
    public int VolumeFilled => VolumeTotal - VolumeRemain;
    public double PercentageFilled => VolumeTotal > 0 ? (double)VolumeFilled / VolumeTotal * 100 : 0;
    public DateTime ExpiresAt => Issued.AddDays(Duration);
    public TimeSpan TimeRemaining => ExpiresAt - DateTime.UtcNow;
    public bool IsExpired => TimeRemaining.TotalSeconds <= 0;

    // UI helpers
    public string FormattedPrice => Price.FormatIsk();
    public string FormattedTotal => (Price * VolumeTotal).FormatIsk();
    public string OrderTypeDisplay => IsBuyOrder ? "Kauforder" : "Verkaufsorder";
    public string OrderTypeIcon => IsBuyOrder ? "📥" : "📤";
}
