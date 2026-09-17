namespace WALLEve.Models.Industry;

/// <summary>
/// Eine Plan-Zeile innerhalb der Material-Aggregation (#62): welcher Plan
/// wie viel von dem Material braucht. Nur für die Anzeige — die Fehlmenge
/// wird nie je Plan/je Ort abgeleitet, sondern einmalig gegen den
/// deduplizierten physischen Bestandspool des Materials.
/// </summary>
public sealed record MaterialDemandPlanRow(string PlanName, long RequiredQuantity);

/// <summary>
/// Abgleich eines Materials über alle Pläne gegen die Holdings (Issue #62).
///
/// Garantien:
/// - <see cref="PhysicalAvailable"/> ist der physische Bestandspool des
///   Materials über den GESAMTEN Owner-Snapshot — jede Asset-Zeile zählt
///   genau einmal. Gleiches Material in mehreren Orten/Plänen wird dadurch
///   nie als mehrfach verfügbar behauptet (Akzeptanzkriterium 1).
/// - <see cref="Inbound"/> (offene Buy-Orders) und <see cref="Bound"/>
///   (offene Sell-Orders) bleiben getrennte Mengen und werden nie still zur
///   Fehlmenge oder zum Bestand verrechnet (Akzeptanzkriterium 2).
/// - <see cref="Shortage"/> = max(0, Gesamtbedarf − physischer Pool),
///   einmalig je Material über alle Pläne — keine Doppelzählung.
/// - Fehlende oder unvollständige Quellen markieren IsPartial; null-Werte
///   sind nie als Nullbestand zu lesen (Akzeptanzkriterium 2).
/// </summary>
public sealed class MaterialDemandMatch
{
    /// <summary>EVE-TypeId des Materials.</summary>
    public int MaterialTypeId { get; init; }

    /// <summary>Plan-Zeilen (Anzeige der Herkunft des Bedarfs).</summary>
    public IReadOnlyList<MaterialDemandPlanRow> Plans { get; init; } = Array.Empty<MaterialDemandPlanRow>();

    /// <summary>Gesamtbedarf über alle Pläne (Planung, keine Reservierung).</summary>
    public long PlannedQuantity => Plans.Sum(p => p.RequiredQuantity);

    /// <summary>
    /// Deduplizierter physischer Bestandspool des Materials (Owner-Snapshot,
    /// jede Asset-Zeile genau einmal). null = Quelle fehlt → nicht ableitbar.
    /// </summary>
    public long? PhysicalAvailable { get; init; }

    /// <summary>Offenes Volumen aktiver Buy-Orders (getrennte Menge). null = Order-Quelle fehlt.</summary>
    public long? Inbound { get; init; }

    /// <summary>Offenes Volumen aktiver Sell-Orders (getrennte Menge). null = Order-Quelle fehlt.</summary>
    public long? Bound { get; init; }

    /// <summary>
    /// Fehlmenge = max(0, Gesamtbedarf − physischer Pool), EINMALIG je Material.
    /// null = Ableitung blockiert (physische Basis fehlt).
    /// </summary>
    public long? Shortage { get; init; }

    /// <summary>true, wenn mindestens eine Eingabequelle fehlt/unvollständig ist.</summary>
    public bool IsPartial { get; init; }

    /// <summary>"physical-source-missing" | "orders-source-missing" (erster Grund in dieser Reihenfolge).</summary>
    public string? PartialReason { get; init; }
}