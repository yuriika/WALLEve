using WALLEve.Models.Holdings;
using WALLEve.Models.Stockpiles;

namespace WALLEve.Services.Stockpiles.Interfaces;

/// <summary>
/// CRUD für persistierte Stockpile-Ziele (Issue #36): Owner-scoped Ziele mit
/// optionalem Ort/Container-Scope, Notiz und Archivstatus. Bewusst OHNE
/// Bestandsberechnung — die Quellen-Aufteilung folgt in #43.
/// </summary>
public interface IStockpileService
{
    /// <summary>
    /// Alle Ziele eines Owners. Standardmäßig werden archivierte Ziele
    /// ausgeblendet; mit <paramref name="includeArchived"/> bleiben sie
    /// abrufbar (Archiv-Wiederlesen).
    /// </summary>
    Task<List<StockpileTarget>> GetAllAsync(OwnerType ownerType, int ownerId, bool includeArchived = false, CancellationToken ct = default);

    /// <summary>Ein einzelnes Ziel (auch archivierte) anhand der Id.</summary>
    Task<StockpileTarget?> GetAsync(long id, CancellationToken ct = default);

    /// <summary>
    /// Legt ein Ziel an. Lehnt negative Mengen und doppelte aktive
    /// Type-Ziele desselben Owners ab.
    /// </summary>
    Task<StockpileTarget> CreateAsync(StockpileTarget target, CancellationToken ct = default);

    /// <summary>Aktualisiert Menge, Ort/Container, Notiz und Archivstatus eines Ziels.</summary>
    Task<StockpileTarget> UpdateAsync(StockpileTarget target, CancellationToken ct = default);

    /// <summary>Löscht ein Ziel endgültig; false, wenn es nicht existiert.</summary>
    Task<bool> DeleteAsync(long id, CancellationToken ct = default);

    /// <summary>Setzt oder entfernt den Archivstatus eines Ziels.</summary>
    Task<bool> SetArchivedAsync(long id, bool archived, CancellationToken ct = default);
}