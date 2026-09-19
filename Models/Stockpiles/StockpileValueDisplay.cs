using System.Globalization;

namespace WALLEve.Models.Stockpiles;

/// <summary>
/// Darstellungshilfen für Stockpile-Werte (Issue #53). Akzeptanzkriterium:
/// unvollständige Bestandsdaten dürfen NICHT als Nullbestand erscheinen —
/// null wird deshalb als „—" mit Hinweistext dargestellt, nie als 0.
/// Bewusst rein und deterministisch, damit die Regel testbar bleibt.
/// </summary>
public static class StockpileValueDisplay
{
    /// <summary>Platzhalter für nicht ableitbare Werte (nie „0").</summary>
    public const string NotAvailable = "—";

    private static readonly CultureInfo DisplayCulture = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>Formatiert einen Mengenwert; null = nicht ableitbar („—").</summary>
    public static string Format(long? value)
        => value.HasValue ? value.Value.ToString("N0", DisplayCulture) : NotAvailable;

    public static bool IsTrustworthy(long? value) => value.HasValue;

    /// <summary>Deutscher Hinweistext zu einem Partial-Grund aus der Berechnung (#43).</summary>
    public static string DescribePartialReason(string? reason) => reason switch
    {
        "physical-source-missing" => "Bestandsquelle fehlt (kein abgeschlossener Snapshot)",
        "scope-not-covered" => "Ort/Container im Snapshot nicht abgedeckt",
        "orders-source-missing" => "Order-Quelle unvollständig/nicht verfügbar",
        null or "" => "Nicht ableitbar",
        _ => $"Unvollständige Datenquelle ({reason})"
    };

    /// <summary>Kurztext für die Quellen-/Freshness-Spalte einer Zeile.</summary>
    public static string DescribeSource(bool partial, string? partialReason)
        => partial ? DescribePartialReason(partialReason) : "vollständig";

    /// <summary>Freshness-Text; null = Zeitpunkt nicht verfügbar (= nicht „gerade eben").</summary>
    public static string FormatFreshness(DateTime? syncedAt)
        => syncedAt.HasValue
            ? syncedAt.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm", DisplayCulture)
            : "unbekannt";
}
