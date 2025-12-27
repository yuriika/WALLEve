namespace WALLEve.Models.Wallet;

/// <summary>
/// Verknüpfung zu einer Market Order (aktiv oder historisch)
/// </summary>
public class MarketOrderInfo
{
    public long OrderId { get; set; }
    public int TypeId { get; set; }
    public double Price { get; set; }
    public int VolumeTotal { get; set; }
    public int VolumeRemain { get; set; }
    public bool IsBuyOrder { get; set; }
    public DateTime Issued { get; set; }
    public string Status { get; set; } = string.Empty; // Active, Completed, Cancelled, Expired

    public int VolumeFilled => VolumeTotal - VolumeRemain;
    public double PercentFilled => VolumeTotal > 0 ? (double)VolumeFilled / VolumeTotal * 100 : 0;
    public bool IsActive => Status == "Active";
}
