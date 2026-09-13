using WALLEve.Models.Holdings;
using WALLEve.Models.Stockpiles;

namespace WALLEve.Models.Stockpiles;

/// <summary>
/// Übersichts-Ergebnis der Stockpile-UI (Issue #53): Berechnungszeilen plus
/// Quellen-/Freshness-Angaben. Die Herkunft der Zahlen wird bewusst
/// mitgeführt, damit die UI unvollständige Quellen als solche zeigen kann
/// statt sie als Nullbestand zu präsentieren.
/// </summary>
public sealed class StockpileOverview
{
    /// <summary>Besitzer-Dimension der Übersicht (Character oder Corporation).</summary>
    public OwnerType OwnerType { get; init; }

    /// <summary>Owner-id der Übersicht.</summary>
    public int OwnerId { get; init; }

    /// <summary>Berechnete Zeilen (Owner-scoped, optional inklusive archivierter Ziele).</summary>
    public IReadOnlyList<StockpileCalculationLine> Lines { get; init; } = Array.Empty<StockpileCalculationLine>();

    /// <summary>
    /// Zeitpunkt des physischen Bestands-Snapshots (Freshness) — null, wenn keiner vorliegt.
    /// </summary>
    public DateTime? PhysicalSourceSyncedAt { get; init; }

    /// <summary>true, wenn die physische Bestandsquelle (abgeschlossener Snapshot) verfügbar ist.</summary>
    public bool PhysicalSourceAvailable { get; init; }

    /// <summary>true, wenn die Order-Quelle vollständig/aktuell vorliegt.</summary>
    public bool OrdersSourceAvailable { get; init; }

    /// <summary>Zeitpunkt der Order-Quelle (Freshness) — null, wenn unbekannt oder nicht verfügbar.</summary>
    public DateTime? OrdersSourceSyncedAt { get; init; }

    /// <summary>true, wenn mindestens eine Zeile auf unvollständigen Quellen beruht.</summary>
    public bool HasPartialLines => Lines.Any(l => l.IsPartial);
}
