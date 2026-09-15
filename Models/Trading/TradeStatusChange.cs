using System;

namespace WALLEve.Models.Trading;

/// <summary>
/// Unveränderliche Historie einer Statusänderung einer Trading-Empfehlung
/// (Issue #45): dokumentiert VON-Status, NACH-Status, Zeitpunkt und Quelle
/// (Nutzer oder System). Die Empfehlung selbst (<c>TradingOpportunity</c>)
/// bleibt beim Ablauf/Invalidierung vollständig erhalten — die Historie wird
/// additiv ergänzt, nie löscht eine Statusänderung die Empfehlung oder ihre
/// Eingaben. Wiederholte identische Markierungen erzeugen keine Duplikate
/// (Idempotenz).
/// </summary>
public class TradeStatusChange
{
    public int Id { get; set; }

    /// <summary>Empfehlung, deren Status sich geändert hat.</summary>
    public int TradingOpportunityId { get; set; }

    /// <summary>Owner (Charakter) der Empfehlung — Owner-Isolation.</summary>
    public int CharacterId { get; set; }

    /// <summary>Status VOR der Änderung (Werte aus <see cref="RecommendationStatus"/>).</summary>
    public string FromStatus { get; set; } = string.Empty;

    /// <summary>Status NACH der Änderung (Werte aus <see cref="RecommendationStatus"/>).</summary>
    public string ToStatus { get; set; } = string.Empty;

    /// <summary>Quelle der Markierung: Nutzer (manuell) oder System (Ablauf/Invalidierung).</summary>
    public TradeStatusSource Source { get; set; }

    /// <summary>Zeitpunkt der Statusänderung.</summary>
    public DateTime ChangedAt { get; set; }

    /// <summary>Optionaler deutscher Hinweis (z. B. Begründung der Markierung).</summary>
    public string? Note { get; set; }
}