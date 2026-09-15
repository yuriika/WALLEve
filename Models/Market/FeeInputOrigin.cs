namespace WALLEve.Models.Market;

/// <summary>
/// Herkunft einer Gebühren-Eingabe (Issue #46). Jede Eingabe der
/// Fee-Berechnung muss unterscheidbar machen, ob sie aus echten Daten,
/// einem expliziten manuellen Override oder einer konservativen Schätzung
/// stammt — ein stiller Null- oder Default-Fallback ist nicht erlaubt.
/// </summary>
public enum FeeInputOrigin
{
    /// <summary>Echte Daten (automatisch bezogen, z. B. ESI-Skill-Level).</summary>
    Automatic = 0,

    /// <summary>Expliziter manueller Override (Konfiguration field), ersetzt automatische Daten.</summary>
    ManualOverride = 1,

    /// <summary>Konservative Schätzung mangels belegbarer Daten (keine Skills/Standings verfügbar).</summary>
    Estimated = 2,

    /// <summary>Notwendige Eingabe fehlt und kann nicht zuverlässig geschätzt werden —
    /// eine präzise Berechnung ist damit blockiert (oder liefert nur eine Spanne).</summary>
    Unknown = 3
}

public static class FeeInputOriginLabels
{
    /// <summary>Anzeige-Label der Herkunft (Produktsprache: Deutsch).</summary>
    public static string Label(this FeeInputOrigin origin) => origin switch
    {
        FeeInputOrigin.Automatic => "automatisch (ESI)",
        FeeInputOrigin.ManualOverride => "manueller Override",
        FeeInputOrigin.Estimated => "geschätzt (konservativ)",
        FeeInputOrigin.Unknown => "unbekannt",
        _ => "unbekannt"
    };

    /// <summary>Stabiler Persistenz-Wert (Datenbankschema, Englisch).</summary>
    public static string StorageValue(this FeeInputOrigin origin) => origin switch
    {
        FeeInputOrigin.Automatic => "automatic",
        FeeInputOrigin.ManualOverride => "manual_override",
        FeeInputOrigin.Estimated => "estimated",
        _ => "unknown"
    };
}