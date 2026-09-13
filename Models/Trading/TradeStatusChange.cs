namespace WALLEve.Models.Trading;

/// <summary>
/// Ein persistierter Statuswechsel einer Trading-Opportunity (Issue #45): Zielstatus,
/// Zeitpunkt und Quelle (Nutzer zählt genauso wie automatische Hygiene). Die Historie
/// wird additiv geschrieben und nie verändert — sie dokumentiert, WANN und von WEM
/// eine Empfehlung in einen Zustand überging. Ein Ablauf oder eine Invalidierung
/// entfernt die ursprüngliche Empfehlung und ihre Inputs (TradeContract) bewusst nicht.
/// </summary>
public class TradeStatusChange
{
    public int Id { get; set; }

    /// <summary>Opportunity, deren Status sich geändert hat.</summary>
    public int TradingOpportunityId { get; set; }

    /// <summary>Owner (Charakter), dem die Opportunity gehört. Owner-Isolation über alle Einträge.</summary>
    public int CharacterId { get; set; }

    /// <summary>Vorheriger Status als DB-String; null = initiale Erfassung.</summary>
    public string? FromStatus { get; set; }

    /// <summary>Neuer Status als DB-String (canonical, z. B. "expired").</summary>
    public string ToStatus { get; set; } = string.Empty;

    /// <summary>Herkunft des Wechsels: "user" (manuelle Markierung) oder "system" (automatisch).</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Zeitpunkt des Wechsels (UTC).</summary>
    public DateTime ChangedAt { get; set; }

    /// <summary>Begründung (Deutsch, optional), z. B. warum eine Empfehlung ungültig wurde.</summary>
    public string? Reason { get; set; }
}