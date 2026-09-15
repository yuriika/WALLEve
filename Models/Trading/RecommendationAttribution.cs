namespace WALLEve.Models.Trading;

/// <summary>
/// Evidenzbasierte Zuordnung von Wallet-Transaktionen zu einer Trading-Empfehlung
/// (Issue #60). Eine Zeile je Empfehlung und Owner.
///
/// Grundsätze:
/// - Die erwartete Spanne (<see cref="ExpectedNetMin"/>..<see cref="ExpectedNetMax"/>)
///   und das tatsächliche Netto (<see cref="ActualNet"/>) werden GETRENNT gespeichert;
///   die Erwartung wird nie durch das Ergebnis überschrieben.
/// - Nicht belegbare Zustände bleiben offen: <see cref="AttributionMatchState.Ambiguous"/>
///   und <see cref="AttributionMatchState.Missing"/> erzeugen keine zugeordnete Menge
///   und kein tatsächliches Netto.
/// - Unbekannte Gebühren (Herkunft aus #46) ergeben <c>ActualNet = null</c> und
///   <see cref="FeeKnowledge.Unknown"/> — keine künstlich exakte Zahl.
/// - Kein FIFO-Lot-Verbrauch (Nicht-Ziel des Issues); zugeordnet wird nur die
///   belegte Menge.
/// </summary>
public class RecommendationAttribution
{
    public int Id { get; set; }

    /// <summary>Empfehlung, der die Transaktionen zugeordnet werden.</summary>
    public int TradingOpportunityId { get; set; }

    /// <summary>Owner der Empfehlung — Owner-Isolation der Zuordnung.</summary>
    public int CharacterId { get; set; }

    /// <summary>Verglichener Item-Typ.</summary>
    public int TypeId { get; set; }

    /// <summary>Handelsseite (Werte aus <see cref="TradeSide"/>).</summary>
    public string Side { get; set; } = string.Empty;

    /// <summary>Zustand der Zuordnung (Werte aus <see cref="AttributionMatchState"/>).</summary>
    public string MatchState { get; set; } = string.Empty;

    /// <summary>Menge, die die Empfehlung erwartet.</summary>
    public int ExpectedQuantity { get; set; }

    /// <summary>Belegte Menge der zugeordneten Transaktionen (bei Mehrdeutigkeit 0).</summary>
    public int AttributedQuantity { get; set; }

    /// <summary>Untere Grenze der erwarteten Netto-Spanne (Empfehlung, Issue #60).</summary>
    public decimal ExpectedNetMin { get; set; }

    /// <summary>Obere Grenze der erwarteten Netto-Spanne (Empfehlung, Issue #60).</summary>
    public decimal ExpectedNetMax { get; set; }

    /// <summary>
    /// Tatsächliches Netto der zugeordneten Transaktionen. Verkauf: Erlös nach
    /// Gebühren (positiv); Kauf: Aufwand inkl. Broker-Fee (negativ). <c>null</c>
    /// bedeutet UNBEKANNT (nicht belegbare Gebühren oder kein Match) — kein
    /// Platzhalterwert.
    /// </summary>
    public decimal? ActualNet { get; set; }

    /// <summary>Belegbarkeit der Gebühren (Werte aus <see cref="AttributionFeeKnowledge"/>).</summary>
    public string FeeKnowledge { get; set; } = AttributionFeeKnowledge.Unknown;

    /// <summary>Herkunft des aktuellen Werts (Werte aus <see cref="AttributionSource"/>).</summary>
    public string Source { get; set; } = AttributionSource.System;

    /// <summary>Zeitfenster, in dem Transaktionen zugeordnet werden dürfen.</summary>
    public DateTime WindowStartUtc { get; set; }
    public DateTime WindowEndUtc { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>Deutscher Hinweis zum Zustand (z. B. Begründung einer Mehrdeutigkeit).</summary>
    public string? Note { get; set; }
}