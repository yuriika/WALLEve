namespace WALLEve.Models.Trading;

/// <summary>
/// Explizite Eingaben und Annahmen für die nachvollziehbare Rang-Bewertung
/// (Issue #54): Ein Kandidat wird ausschließlich aus diesen persistierten
/// Werten bewertet — gleiche Eingaben plus gleiche Algorithmusversion
/// ergeben reproduzierbar dasselbe Ergebnis und dieselbe Erklärung.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><see cref="LiquidityScore"/> (0-1, höher = liquider) und
/// <see cref="RiskScore"/> (0-1, höher = riskanter) sind PFLICHT-Eingaben:
/// Eine Rangliste ohne Liquiditäts-/Risiko-Einschätzung wäre nicht
/// nachvollziehbar, also nicht ausführbar.</item>
/// <item><see cref="ExpectedFillDays"/> ist optional: Fehlt die Zeitannahme,
/// sind die zeitbasierten Dimensionen (Kapitalbindung, ISK/Stunde) nicht
/// berechenbar, der Kandidat bleibt aber bewertbar.</item>
/// </list>
/// </remarks>
public sealed record TradeRankingInput(
    int TradingOpportunityId,

    /// <summary>Netto-Gewinn nach Gebühren in ISK (Pflicht, darf negativ sein).</summary>
    decimal? EstimatedProfit,

    /// <summary>Benötigtes Kapital in ISK (Pflicht, muss positiv sein: ROI und
    /// Kapitalbindung sind bei Null-/Negativ-Kapital nicht definiert).</summary>
    decimal? RequiredCapital,

    /// <summary>Liquiditätsschätzung 0-1 (höher = liquider). Pflicht.</summary>
    double? LiquidityScore,

    /// <summary>Risikoschätzung 0-1 (höher = riskanter). Pflicht.</summary>
    double? RiskScore,

    /// <summary>Angenommene Füllzeit in Tagen (optional; &gt; 0, sonst ungültig).</summary>
    double? ExpectedFillDays);