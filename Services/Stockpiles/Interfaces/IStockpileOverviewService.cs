using WALLEve.Models.Esi.Markets;
using WALLEve.Models.Holdings;
using WALLEve.Models.Stockpiles;

namespace WALLEve.Services.Stockpiles.Interfaces;

/// <summary>
/// Liefert die Owner-scoped Stockpile-Übersicht für die UI (Issue #53):
/// Berechnungszeilen (#43) plus Quellen-/Freshness-Metadaten, damit die UI
/// archivierte Ziele, unvollständige Quellen und fremde Owner korrekt trennt.
/// </summary>
public interface IStockpileOverviewService
{
    /// <summary>
    /// Baut die Übersicht für genau einen Owner. Ziele anderer Owner dürfen
    /// nie enthalten sein (Owner-Wechsel zeigt keine fremden Ziele).
    /// </summary>
    /// <param name="ownerType">Besitzer-Dimension.</param>
    /// <param name="ownerId">Owner-id.</param>
    /// <param name="includeArchived">Archivierte Ziele mit ausweisen.</param>
    /// <param name="orders">Persönliche Market-Orders; null = Order-Quelle nicht verfügbar.</param>
    /// <param name="ordersAvailable">true, wenn <paramref name="orders"/> vollständig/aktuell ist (sonst partial).</param>
    /// <param name="ordersSyncedAt">Freshness der Order-Quelle (optional).</param>
    /// <param name="ct">Abbruch-Token.</param>
    Task<StockpileOverview> GetOverviewAsync(
        OwnerType ownerType,
        int ownerId,
        bool includeArchived = false,
        IReadOnlyList<MarketOrder>? orders = null,
        bool ordersAvailable = false,
        DateTime? ordersSyncedAt = null,
        CancellationToken ct = default);

    /// <summary>
    /// Baut den Markt-Kontext für die Fehlmengenliste (Issue #59): Quotes des
    /// konfigurierten Vergleichsmarkts (mit Preisalter aus dem Snapshot-Zeitpunkt)
    /// für alle TypeIds mit belastbarer Fehlmenge sowie den nächstgelegenen
    /// aktiven Hub je auflösbarer Ziel-Location (exakte Sprungdistanz).
    /// Fehlende Quellen (kein Vergleichsmarkt, kein Snapshot, nicht auflösbares
    /// System, fehlender Graph) bleiben sichtbar unbekannt — nie ein erfundener
    /// 0-Preis oder 0-Sprung.
    /// </summary>
    /// <param name="ownerType">Besitzer-Dimension.</param>
    /// <param name="ownerId">Owner-id.</param>
    /// <param name="includeArchived">Archivierte Ziele beim Shortage-Scope mit berücksichtigen.</param>
    /// <param name="orders">Persönliche Market-Orders (wie bei <see cref="GetOverviewAsync"/>).</param>
    /// <param name="ordersAvailable">true, wenn <paramref name="orders"/> vollständig/aktuell ist.</param>
    /// <param name="ct">Abbruch-Token.</param>
    Task<StockpileMarketContext> GetShortageMarketContextAsync(
        OwnerType ownerType,
        int ownerId,
        bool includeArchived = false,
        IReadOnlyList<MarketOrder>? orders = null,
        bool ordersAvailable = false,
        CancellationToken ct = default);
}
