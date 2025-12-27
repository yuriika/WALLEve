namespace WALLEve.Models.Sde;

/// <summary>
/// Fortschritt eines Downloads
/// </summary>
public class SdeDownloadProgress
{
    public long BytesDownloaded { get; set; }
    public long TotalBytes { get; set; }
    public int ProgressPercent => TotalBytes > 0 ? (int)(BytesDownloaded * 100 / TotalBytes) : 0;
    public string Status { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public bool HasError { get; set; }
    public string? ErrorMessage { get; set; }
}
