namespace WALLEve.Models.Trading;

/// <summary>
/// Handelsseite, auf der eine Wallet-Transaktion zu einer Empfehlung gehört
/// (Issue #60). Die Seite wird vom Aufrufer der Zuordnung explizit übergeben —
/// die Zuordnung selbst leitet sie nicht aus Textfeldern ab.
/// </summary>
public static class TradeSide
{
    /// <summary>Kauf (Wallet-Journal-Transaktion mit IsBuy = true).</summary>
    public const string Buy = "buy";

    /// <summary>Verkauf (Wallet-Journal-Transaktion mit IsBuy = false).</summary>
    public const string Sell = "sell";

    /// <summary>Zulässige gespeicherte Werte.</summary>
    public static readonly string[] All = [Buy, Sell];
}

/// <summary>
/// Belegbarkeit der Gebühren einer Zuordnung (Issue #60): Nur wenn die
/// Gebührenherkunft aus Issue #46 die Sätze tatsächlich belegt, wird ein
/// exakter Netto-Wert berechnet — sonst bleibt das tatsächliche Netto
/// unbekannt statt künstlich exakt zu sein.
/// </summary>
public static class AttributionFeeKnowledge
{
    /// <summary>Sätze durch Herkunft belegt (automatic/manual_override/estimated).</summary>
    public const string Known = "known";

    /// <summary>Keine belegbaren Sätze — ActualNet bleibt unbekannt (null).</summary>
    public const string Unknown = "unknown";
}