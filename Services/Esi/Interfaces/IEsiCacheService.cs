using WALLEve.Models.Esi;

namespace WALLEve.Services.Esi.Interfaces;

public interface IEsiCacheService
{
    /// <summary>
    /// Holt einen Cache-Eintrag für einen Endpoint
    /// </summary>
    ETagCacheEntry<T>? Get<T>(string endpoint);

    /// <summary>
    /// Speichert einen Cache-Eintrag für einen Endpoint
    /// </summary>
    void Set<T>(string endpoint, string etag, T data, DateTime? expires = null);

    /// <summary>
    /// Löscht abgelaufene Cache-Einträge
    /// </summary>
    void CleanupExpired();

    /// <summary>
    /// Löscht alle Cache-Einträge
    /// </summary>
    void Clear();
}
