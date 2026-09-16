using WALLEve.Models.Esi.Markets;

namespace WALLEve.Services.Market.Interfaces;

/// <summary>
/// Cache für vollständige Regionsdaten (#69): eine Region wird pro Cachefenster
/// höchstens einmal vollständig über den gemessenen Regionalscan-Pfad
/// (<c>GetAllRegionalMarketOrdersAsync</c>, inklusive atomarem Publizieren) geladen;
/// danach filtern Aufrufer lokal nach Typ/System/Location. Kein Vollregions-Request
/// pro Item — ein Scan pro Region pro Fenster.
/// </summary>
public interface IRegionalMarketCacheService
{
    /// <summary>
    /// Liefert alle Orders einer Region. Innerhalb des Cachefensters wird der
    /// gespeicherte Regionalscan geliefert (kein redundanter Scan); außerhalb wird
    /// genau ein Scan angestoßen. Ein fehlgeschlagener/abgebrochener Refresh
    /// publiziert keine Teildaten: der letzte vollständige Stand bleibt aktiv.
    /// </summary>
    Task<IReadOnlyList<RegionalMarketOrder>> GetRegionOrdersAsync(int regionId, CancellationToken ct = default);

    /// <summary>
    /// Liefert die Orders einer Region, lokal auf einen Item-Typ gefiltert
    /// (kein zusätzlicher ESI-Request — Filterung erfolgt auf dem Regionalscan).
    /// </summary>
    Task<IReadOnlyList<RegionalMarketOrder>> GetOrdersForTypeAsync(int regionId, int typeId, CancellationToken ct = default);

    /// <summary>
    /// Datenalter und Messgrundlage eines Regionscaches (für Sichtbarkeit in der UI).
    /// null, wenn die Region noch nie gescannt wurde.
    /// </summary>
    RegionalMarketCacheInfo? GetCacheInfo(int regionId);

    /// <summary>
    /// Entfernt alle Regionseinträge (Diagnose/Tests).
    /// </summary>
    void Clear();
}

/// <summary>
/// Messgrundlage eines Regionscaches: wann der letzte vollständige Scan erfolgte,
/// wie viele Orders/Seiten er enthielt und ob er aus dem Netz oder aus dem
/// Cachefenster stammt.
/// </summary>
public sealed record RegionalMarketCacheInfo(
    int RegionId,
    DateTime ScannedAtUtc,
    int OrderCount,
    int PageCount,
    bool FromCacheWindow);