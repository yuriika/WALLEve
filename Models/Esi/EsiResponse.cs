namespace WALLEve.Models.Esi;

/// <summary>
/// Wrapper für ESI API Responses mit Metadaten
/// </summary>
public class EsiResponse<T>
{
    public T? Data { get; set; }
    public int StatusCode { get; set; }
    public string? ETag { get; set; }
    public DateTime? Expires { get; set; }
    public DateTime? LastModified { get; set; }
    public int? TotalPages { get; set; }
    public RateLimitInfo? RateLimit { get; set; }
    public bool IsSuccess => StatusCode >= 200 && StatusCode < 300;
    public bool IsNotModified => StatusCode == 304;
}
