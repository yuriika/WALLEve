using WALLEve.Models.Database;

namespace WALLEve.Services.Trading;

/// <summary>
/// Reine Anzeige-Daten einer Action Card (Issue #66): Schlüsselfakten
/// (Was/Wo/Menge/Preis/Netto/Annahmen) zuerst, Rechnung/Evidenz aufklappbar.
/// Die Instanz ist unveränderlich (init-only) — Hilfsaktionen (Kopieren,
/// Marktdetails, Wegpunkt) laufen als Seitenkanal und können die Empfehlung
/// nie verändern. Statusänderungen laufen ausschließlich über
/// <see cref="ITradeStatusService"/> (explizite Zustandsmaschine, #45).
/// </summary>
public sealed class TradingActionCardModel
{
    public int OpportunityId { get; init; }

    /// <summary>Owner der Empfehlung — dient der Owner-Isolation bei Markierungen.</summary>
    public int CharacterId { get; init; }

    /// <summary>Werte aus <c>TradingOpportunity.OpportunityType</c> ("inventory_sell", …).</summary>
    public string OpportunityType { get; init; } = string.Empty;

    /// <summary>„Was": Item-Name (aus SDE; Fallback "Type N").</summary>
    public string TypeName { get; init; } = string.Empty;

    /// <summary>„Wo": primärer Ort der Empfehlung (Kauf- oder Verkaufsstation).</summary>
    public string? LocationLabel { get; init; }

    /// <summary>
    /// „Menge": nur wenn aus der Evidenz belegbar ableitbar (siehe
    /// <see cref="TradingActionCardService.TryParseQuantity"/>). <c>null</c> =
    /// nicht belegbar, wird als „—" angezeigt (keine erfundene Menge).
    /// </summary>
    public int? Quantity { get; init; }

    public double? BuyPrice { get; init; }

    public double? SellPrice { get; init; }

    /// <summary>Geschätzter Nettogewinn der Analyse (für den Vergleich mit dem tatsächlichen Ergebnis).</summary>
    public double EstimatedProfit { get; init; }

    /// <summary>Angezeigtes Netto: tatsächliches Ergebnis, falls vorhanden, sonst Schätzung.</summary>
    public double NetValue { get; init; }

    /// <summary>true = <see cref="NetValue"/> ist das tatsächliche Ergebnis (Nutzerangabe/Attribution).</summary>
    public bool HasActualResult { get; init; }

    /// <summary>Tatsächlicher Gewinn, falls vom Nutzer/System erfasst (Performance-Tracking).</summary>
    public double? ActualProfit { get; init; }

    public double Score { get; init; }

    public string? AlgorithmVersion { get; init; }

    public string Provenance { get; init; } = string.Empty;

    /// <summary>Legacy-Datensatz: Score ist keine aktuelle Evidenz (vor Provenienz-Erfassung).</summary>
    public bool IsLegacyProvenance { get; init; }

    public string? DataQuality { get; init; }

    /// <summary>Brokergebühr-Satz (persistiert zum Analysezeitpunkt, Issue #46).</summary>
    public double? BrokerFeeRate { get; init; }

    public double? SalesTaxRate { get; init; }

    public string? BrokerFeeOrigin { get; init; }

    public string? SalesTaxOrigin { get; init; }

    public DateTime DetectedAt { get; init; }

    public DateTime ExpiresAt { get; init; }

    /// <summary>Status-Wert aus <see cref="RecommendationStatus"/>.</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>Terminale Zustände (executed/expired/invalid) erlauben keine Nutzer-Markierung mehr.</summary>
    public bool IsTerminalStatus { get; init; }

    /// <summary>Annahmen der Rechnung (aus Datenqualität und Gebührenherkunft abgeleitet).</summary>
    public IReadOnlyList<string> Assumptions { get; init; } = Array.Empty<string>();

    /// <summary>Zeilen der aufklappbaren Rechnung/Evidenz (aus <c>Opportunity.Evidence</c>).</summary>
    public IReadOnlyList<string> EvidenceLines { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Text für „Preis kopieren": relevanter Preis (Sell-, sonst Buy-Preis),
    /// invariant formatiert ohne Währungssymbol. <c>null</c> = kein belegbarer Preis.
    /// </summary>
    public string? CopyPricePayload { get; init; }

    /// <summary>Text für „Menge kopieren"; nur wenn <see cref="Quantity"/> belegbar ist.</summary>
    public string? CopyQuantityPayload { get; init; }
}