namespace WALLEve.Models.Trading;

/// <summary>
/// Lebenszyklus-Status einer Trading-Opportunity (Issue #45). Der Wert entspricht
/// dem gespeicherten String in <c>TradingOpportunity.Status</c> — niemals umbenennen,
/// nur anhängen. Bestandsdaten mit dem Legacy-Wert "active" (vor der Status-Erfassung)
/// werden beim Lesen als <see cref="Planned"/> normalisiert; ein Daten-Update ist
/// bewusst NICHT nötig.
/// </summary>
public enum TradeStatus
{
    /// <summary>Empfohlen, noch nicht ausgeführt (Nachfolger des Legacy-Werts "active").</summary>
    Planned = 1,

    /// <summary>Vom Nutzer als ausgeführt markiert.</summary>
    Executed = 2,

    /// <summary>Vom Nutzer verworfen (bewusste Nicht-Ausführung).</summary>
    Dismissed = 3,

    /// <summary>Automatisch abgelaufen (Gültigkeit überschritten); Empfehlung bleibt erhalten.</summary>
    Expired = 4,

    /// <summary>Ungültig (z. B. Empfehlung fällt weg); Empfehlung und Inputs bleiben erhalten.</summary>
    Invalid = 5
}

/// <summary>
/// Herkunft eines Statuswechsels: Nutzerangabe oder automatische (System-)Hygiene.
/// Eine manuelle Markierung wird nie als automatische Ausführung ausgegeben und
/// umgekehrt — die Quelle ist Teil der Historie (Issue #45).
/// </summary>
public enum TradeStatusSource
{
    /// <summary>Vom Nutzer ausgelöste Markierung (Explizite Nutzerangabe).</summary>
    User = 1,

    /// <summary>Automatischer System-Übergang (Ablauf/Invalidierung durch Analyse-Hygiene).</summary>
    System = 2
}

public static class TradeStatusExtensions
{
    /// <summary>Canonical DB-String des Status.</summary>
    public static string ToDatabaseValue(this TradeStatus status) => status switch
    {
        TradeStatus.Planned => "planned",
        TradeStatus.Executed => "executed",
        TradeStatus.Dismissed => "dismissed",
        TradeStatus.Expired => "expired",
        TradeStatus.Invalid => "invalid",
        _ => "unknown"
    };

    /// <summary>
    /// Liest einen gespeicherten Status-String; der Legacy-Wert "active" (vor Issue #45
    /// der einzige vordefinierte Vor-Ausführungs-Wert) wird zu <see cref="TradeStatus.Planned"/>
    /// normalisiert. Unbekannte Werte gelten als ungültig (kein stiller Fallback auf planned).
    /// </summary>
    public static TradeStatus? FromDatabaseValue(string? value) => value switch
    {
        "planned" or "active" => TradeStatus.Planned,
        "executed" => TradeStatus.Executed,
        "dismissed" => TradeStatus.Dismissed,
        "expired" => TradeStatus.Expired,
        "invalid" => TradeStatus.Invalid,
        _ => null
    };

    /// <summary>Alle gespeicherten String-Werte, die <see cref="TradeStatus.Planned"/> bedeuten.</summary>
    public static readonly string[] PlannedStorageValues = ["planned", "active"];

    /// <summary>
    /// Erlaubte Statusübergänge: Nur aus <see cref="TradeStatus.Planned"/> sind Wechsel erlaubt;
    /// alle Endzustände (executed/dismissed/expired/invalid) sind terminal. Ein identischer
    /// Zielstatus ist immer erlaubt (idempotente Wiederholung legt keine neue Historie an).
    /// </summary>
    public static bool IsAllowedTransition(this TradeStatus from, TradeStatus to)
        => from == to || from == TradeStatus.Planned;
}

public static class TradeStatusSourceExtensions
{
    /// <summary>Canonical DB-String der Quelle.</summary>
    public static string ToDatabaseValue(this TradeStatusSource source) => source switch
    {
        TradeStatusSource.User => "user",
        TradeStatusSource.System => "system",
        _ => "unknown"
    };

    public static TradeStatusSource FromDatabaseValue(string? value) => value switch
    {
        "user" => TradeStatusSource.User,
        "system" => TradeStatusSource.System,
        _ => throw new ArgumentOutOfRangeException(nameof(value), $"Unbekannter TradeStatusSource-Wert: {value}")
    };
}