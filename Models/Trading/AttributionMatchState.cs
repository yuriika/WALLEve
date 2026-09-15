namespace WALLEve.Models.Trading;

/// <summary>
/// Ergebnis der evidenzbasierten Zuordnung von Wallet-Transaktionen zu einer
/// Empfehlung (Issue #60). Gespeichert wird ausschließlich, was aus den
/// vorhandenen Daten belegbar ist; mehrdeutige Lagen bleiben offen und werden
/// nie stillschweigend einer Empfehlung zugerechnet.
/// </summary>
public static class AttributionMatchState
{
    /// <summary>Genau eine Transaktion passt vollständig (Owner/Typ/Seite/Menge/Zeit).</summary>
    public const string Unique = "unique";

    /// <summary>Mehrere Transaktionen passen vollständig — Zuordnung bleibt offen.</summary>
    public const string Ambiguous = "ambiguous";

    /// <summary>Die Kandidaten decken die erwartete Menge nur teilweise ab.</summary>
    public const string Partial = "partial";

    /// <summary>Kein Kandidat vorhanden (oder bereits verbraucht).</summary>
    public const string Missing = "missing";
}

/// <summary>Herkunft einer Zuordnung bzw. einer manuellen Korrektur.</summary>
public static class AttributionSource
{
    /// <summary>Deterministisch durch das System ermittelt.</summary>
    public const string System = "system";

    /// <summary>Vom Nutzer manuell gesetzt oder korrigiert.</summary>
    public const string User = "user";
}