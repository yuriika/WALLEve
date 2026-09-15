namespace WALLEve.Models.Trading;

/// <summary>
/// Zulässige Empfehlungs-Statuswerte einer TradingOpportunity (Issue #45).
/// Konstanten statt Strings: gleiche Werte wie die bestehende
/// <c>TradingOpportunity.Status</c>-Spalte ("active", "planned", "executed",
/// "dismissed", "expired", "invalid").
/// </summary>
public static class RecommendationStatus
{
    /// <summary>Aktive Empfehlung (Standard nach der Analyse).</summary>
    public const string Active = "active";

    /// <summary>Vom Nutzer als geplant markiert.</summary>
    public const string Planned = "planned";

    /// <summary>Vom Nutzer als ausgeführt markiert (nie automatisch).</summary>
    public const string Executed = "executed";

    /// <summary>Vom Nutzer verworfen.</summary>
    public const string Dismissed = "dismissed";

    /// <summary>Automatisch abgelaufen (Ablaufzeit überschritten).</summary>
    public const string Expired = "expired";

    /// <summary>Automatisch oder manuell als ungültig markiert.</summary>
    public const string Invalid = "invalid";

    /// <summary>
    /// Alle gespeicherten String-Werte, die eine nicht-terminale, geplante Empfehlung
    /// bedeuten: canonical "planned" sowie der Legacy-Wert "active" (Bestand vor Issue
    /// #45). Beim Lesen/Normalisieren mit <c>Contains</c> verwenden.
    /// </summary>
    public static readonly string[] PlannedStorageValues = [Planned, Active];
}