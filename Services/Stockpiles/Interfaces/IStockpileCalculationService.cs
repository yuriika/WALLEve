using WALLEve.Models.Esi.Markets;
using WALLEve.Models.Holdings;
using WALLEve.Models.Stockpiles;

namespace WALLEve.Services.Stockpiles.Interfaces;

/// <summary>
/// Bestandsberechnung für Stockpile-Ziele (Issue #43): physisch, eingehend
/// und gebunden getrennt, Shortage/Surplus nur gegen den physischen Bestand.
/// Physische Basis ist der jüngste abgeschlossene Holding-Snapshot des Owners;
/// Orders werden vom Aufrufer bereitgestellt (keine Live-ESI-Abhängigkeit in
/// deterministischen Tests). Fehlende Quellen markieren partial und blockieren
/// die Ableitung.
/// </summary>
public interface IStockpileCalculationService
{
    /// <summary>
    /// Berechnet für alle Ziele eines Owners die getrennten Bestandsanteile.
    /// </summary>
    /// <param name="ownerType">Besitzer-Dimension (Character oder Corporation).</param>
    /// <param name="ownerId">Owner-id.</param>
    /// <param name="includeArchived">Archivierte Ziele mit ausweisen (nachvollziehbar statt gelöscht).</param>
    /// <param name="orders">
    /// Aktive persönliche Market-Orders (GET /characters/{id}/orders/). null = Quelle nicht verfügbar;
    /// die Zeilen werden dann als partial markiert und Inbound/Bound bleiben null.
    /// </param>
    /// <param name="ordersAvailable">
    /// true, wenn <paramref name="orders"/> den vollständigen, aktuellen Order-Bestand
    /// repräsentiert; false markiert die Zeilen als partial (unvollständige Ableitung blockieren).
    /// </param>
    Task<IReadOnlyList<StockpileCalculationLine>> CalculateAsync(
        OwnerType ownerType,
        int ownerId,
        bool includeArchived = false,
        IReadOnlyList<MarketOrder>? orders = null,
        bool ordersAvailable = false,
        CancellationToken ct = default);
}