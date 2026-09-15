namespace WALLEve.Models.Trading;

/// <summary>
/// Ergebnis der Einzel-Bewertung eines Kandidaten (Issue #54): alle
/// Rang-Dimensionen getrennt mit Wert, Gewicht und Erklärung sowie je
/// Szenario (konservativ/realistisch/optimistisch) die geschätzte
/// ISK/Stunde. Reine C#-Berechnung ohne LLM-Aufrufe und ohne
/// Datenbankzugriff; deterministisch aus den Eingaben und der
/// Algorithmusversion reproduzierbar.
/// </summary>
/// <param name="IsActionable">false = Pflichtdaten fehlen oder ein Bereich ist ungültig; Begründung in <see cref="NotActionableReason"/>.</param>
/// <param name="AlgorithmVersion">Version der Analyse („trade-ranking-v1").</param>
/// <param name="Explanation">Gesamterklärung (Deutsch), deterministisch aus Eingaben und Annahmen gebildet.</param>
public sealed record TradeRankingResult(
    bool IsActionable,
    string? NotActionableReason,
    string AlgorithmVersion,
    IReadOnlyList<TradeRankingDimension> Dimensions,
    IReadOnlyList<TradeRankingScenario> Scenarios,
    string Explanation);

/// <summary>
/// Bewerteter Kandidat in einer Rangliste (Issue #54): Platz, gewichteter
/// Gesamt-Score (0-100) und die normalisierten Punktwerte je Dimension
/// (0-100, höher = besser), die diesen Platz ergeben haben.
/// </summary>
public sealed record TradeRankingEntry(
    int TradingOpportunityId,
    int Rank,
    double WeightedScore,
    IReadOnlyDictionary<string, double> DimensionPoints);

/// <summary>
/// Ergebnis einer Kandidaten-Rangliste (Issue #54): sortierte Einträge,
/// die ausgeschlossenen Kandidaten und eine deterministische Erklärung der
/// Normalisierung und Gewichtung. Ausgeschlossen sind nicht ausführbare
/// Kandidaten (fehlende Pflichtdaten oder ungültige Bereiche) und Kandidaten
/// ohne positives aktives Gewicht (alle ihre Dimensionen sind mit Gewicht 0
/// konfiguriert) — beide werden in der Erklärung mit Begründung genannt.
/// </summary>
public sealed record TradeRankingOutcome(
    string AlgorithmVersion,
    IReadOnlyList<TradeRankingEntry> Entries,
    IReadOnlyList<int> ExcludedOpportunityIds,
    string Explanation);