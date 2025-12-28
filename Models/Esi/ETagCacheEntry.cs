namespace WALLEve.Models.Esi;

/// <summary>
/// Cache-Eintrag für ETag-basiertes HTTP Caching
/// </summary>
public class ETagCacheEntry<T>
{
    /// <summary>
    /// ETag Header Value (z.B. "e7b8c9d0a1b2c3d4e5f6")
    /// </summary>
    public required string ETag { get; set; }

    /// <summary>
    /// Gecachte Response-Daten
    /// </summary>
    public required T Data { get; set; }

    /// <summary>
    /// Zeitpunkt, wann dieser Cache-Eintrag erstellt wurde
    /// </summary>
    public DateTime CachedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Expires Header (wenn von ESI zurückgegeben)
    /// </summary>
    public DateTime? Expires { get; set; }

    /// <summary>
    /// Prüft ob der Cache-Eintrag noch gültig ist
    /// </summary>
    public bool IsValid()
    {
        if (Expires.HasValue)
        {
            return DateTime.UtcNow < Expires.Value;
        }

        // Default: 5 Minuten Cache wenn kein Expires-Header vorhanden
        return DateTime.UtcNow < CachedAt.AddMinutes(5);
    }
}
