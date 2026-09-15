using WALLEve.Models.Trading;

namespace WALLEve.Services.Trading;

/// <summary>
/// Nachvollziehbare Rang-Bewertung von Trade-Kandidaten (Issue #54):
/// Netto-ISK, ROI, Kapitalbindung, Liquidität, Risiko und geschätzte
/// ISK/Stunde werden GETRENNT berechnet und gewichtet; Konservativ/
/// Realistisch/Optimistisch entstehen aus expliziten Annahmen über die
/// Füllzeit (keine exakte Füllzeit-Behauptung). Reine, zustandsfreie
/// C#-Logik: keine Datenbank, keine externen Dienste, keine LLM-Aufrufe.
/// Gleiche Eingaben + gleiche Algorithmusversion reproduzieren Ergebnis
/// und Erklärung exakt.
/// </summary>
/// <remarks>
/// Ausführbarkeit: Fehlende Pflichtdaten (Gewinn, Kapital, Liquidität,
/// Risiko) oder ungültige Bereiche (Kapital &lt;= 0, Scores außerhalb
/// 0-1, Füllzeit &lt;= 0) ergeben einen NICHT ausführbaren Kandidaten
/// mit deutscher Begründung — niemals still erfundene Defaults. Fehlt
/// nur die Zeitannahme, bleiben die zeitbasierten Dimensionen
/// (Kapitalbindung, ISK/Stunde) unberechenbar, der Kandidat ist aber
/// weiter bewertbar (Gewichte werden auf die aktiven Dimensionen
/// renormalisiert).
/// </remarks>
public static class TradeRankingEngine
{
    /// <summary>Version der Analyse; Bestandteil jedes Ergebnisses.</summary>
    public const string AlgorithmVersion = "trade-ranking-v1";

    private const double HoursPerDay = 24.0;

    private const string NetProfitDimension = "Netto-ISK";
    private const string RoiDimension = "ROI";
    private const string CapitalBindingDimension = "Kapitalbindung";
    private const string LiquidityDimension = "Liquidität";
    private const string RiskDimension = "Risiko";
    private const string IskPerHourDimension = "ISK/Stunde";

    /// <summary>Einzel-Bewertung eines Kandidaten mit den Standard-Annahmen.</summary>
    public static TradeRankingResult Evaluate(TradeRankingInput input) => Evaluate(input, TradeRankingAssumptions.Default);

    /// <summary>Einzel-Bewertung eines Kandidaten mit expliziten Annahmen.</summary>
    public static TradeRankingResult Evaluate(TradeRankingInput input, TradeRankingAssumptions assumptions)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(assumptions);

        var validationError = Validate(input);
        if (validationError is not null)
            return new TradeRankingResult(
                IsActionable: false,
                NotActionableReason: validationError,
                AlgorithmVersion,
                Array.Empty<TradeRankingDimension>(),
                Array.Empty<TradeRankingScenario>(),
                $"Nicht bewertbar: {validationError}");

        var dimensions = new List<TradeRankingDimension>
        {
            NetProfitDimensionValue(input, assumptions),
            RoiDimensionValue(input, assumptions),
            CapitalBindingDimensionValue(input, assumptions),
            new(LiquidityDimension, input.LiquidityScore!.Value, "Score 0-1", assumptions.LiquidityWeight, HigherIsBetter: true,
                $"Liquiditätsschätzung {input.LiquidityScore.Value:F2} (0-1) aus der Marktsicht des Aufrufers; höher = liquider."),
            new(RiskDimension, input.RiskScore!.Value, "Score 0-1", assumptions.RiskWeight, HigherIsBetter: false,
                $"Risikoschätzung {input.RiskScore.Value:F2} (0-1) aus Sprüngen/Sicherheit/Preisband des Aufrufers; höher = riskanter."),
            IskPerHourDimensionValue(input, assumptions)
        };

        var scenarios = BuildScenarios(input, assumptions);
        var explanation = BuildExplanation(input, assumptions, dimensions, scenarios);

        return new TradeRankingResult(
            IsActionable: true,
            NotActionableReason: null,
            AlgorithmVersion,
            dimensions,
            scenarios,
            explanation);
    }

    /// <summary>
    /// Rangliste über mehrere Kandidaten (Standard-Annahmen): nutzungsfähige
    /// Kandidaten werden je Dimension über das realistische Szenario
    /// min-max-normalisiert (0-100, höher = besser; Kapitalbindung und
    /// Risiko invertiert) und mit den Dimensionsgewichten zum Gesamt-Score
    /// verrechnet. Nicht ausführbare Kandidaten werden ausgeschlossen und
    /// in der Erklärung genannt.
    /// </summary>
    public static TradeRankingOutcome Rank(IReadOnlyList<TradeRankingInput> inputs) => Rank(inputs, TradeRankingAssumptions.Default);

    /// <summary>Rangliste über mehrere Kandidaten mit expliziten Annahmen.</summary>
    public static TradeRankingOutcome Rank(IReadOnlyList<TradeRankingInput> inputs, TradeRankingAssumptions assumptions)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(assumptions);

        var actionable = new List<TradeRankingInput>();
        var excluded = new List<int>();
        foreach (var input in inputs)
        {
            if (Validate(input) is { } reason)
            {
                excluded.Add(input.TradingOpportunityId);
                continue;
            }
            actionable.Add(input);
        }

        if (actionable.Count == 0)
        {
            return new TradeRankingOutcome(
                AlgorithmVersion,
                Array.Empty<TradeRankingEntry>(),
                excluded,
                $"Keine Kandidaten bewertbar: {string.Join("; ", excluded.Select(id => $"Opportunity {id}"))} sind nicht ausführbar (fehlende Pflichtdaten oder ungültige Bereiche).");
        }

        // Dimensionen des realistischem Szenarios je Kandidat (deterministisch, ohne Zeitannahme = null).
        var values = new Dictionary<string, double[]>();
        var activeDimensions = new List<TradeRankingDimension>();
        foreach (var candidate in actionable)
        {
            var result = Evaluate(candidate, assumptions);
            foreach (var dimension in result.Dimensions)
            {
                if (dimension.Value is null)
                    continue;
                if (!values.TryGetValue(dimension.Name, out var bucket))
                {
                    bucket = new double[actionable.Count];
                    values[dimension.Name] = bucket;
                    activeDimensions.Add(dimension);
                }
                bucket[actionable.IndexOf(candidate)] = dimension.Value.Value;
            }
        }

        var points = new Dictionary<int, Dictionary<string, double>>();
        foreach (var candidate in actionable)
            points[candidate.TradingOpportunityId] = new Dictionary<string, double>();

        foreach (var dimension in activeDimensions)
        {
            var bucket = values[dimension.Name];
            var min = bucket.Min();
            var max = bucket.Max();
            var span = max - min;
            for (var i = 0; i < bucket.Length; i++)
            {
                double normalized = span > 0
                    ? (bucket[i] - min) / span
                    : 0.5; // alle Kandidaten gleich: neutrale Mitte, kein 0-Spannen-Problem
                if (!dimension.HigherIsBetter)
                    normalized = 1.0 - normalized;
                points[actionable[i].TradingOpportunityId][dimension.Name] = Math.Round(normalized * 100.0, 2);
            }
        }

        var activeWeightSum = activeDimensions.Sum(d => d.Weight);
        var entries = actionable
            .Select(candidate =>
            {
                var candidatePoints = points[candidate.TradingOpportunityId];
                var weighted = activeDimensions.Sum(d => candidatePoints[d.Name] * d.Weight) / activeWeightSum;
                return (Candidate: candidate, Score: weighted, Points: candidatePoints);
            })
            .OrderByDescending(x => x.Score)
            .Select((x, index) => new TradeRankingEntry(
                x.Candidate.TradingOpportunityId,
                index + 1,
                Math.Round(x.Score, 2),
                x.Points))
            .ToList();

        var explanation = BuildRankExplanation(activeDimensions, actionable, entries, excluded);

        return new TradeRankingOutcome(AlgorithmVersion, entries, excluded, explanation);
    }

    // --- Validierung ---

    private static string? Validate(TradeRankingInput input)
    {
        if (input.EstimatedProfit is null)
            return "Fehlende Pflichteingabe: Netto-Gewinn (ISK).";
        if (input.RequiredCapital is null)
            return "Fehlende Pflichteingabe: benötigtes Kapital (ISK).";
        if (input.RequiredCapital <= 0)
            return "Ungültige Eingabe: benötigtes Kapital muss positiv sein (ROI und Kapitalbindung sind bei Null-/Negativ-Kapital nicht definiert).";
        if (input.LiquidityScore is null)
            return "Fehlende Pflichteingabe: Liquiditätsschätzung (0-1).";
        if (input.RiskScore is null)
            return "Fehlende Pflichteingabe: Risikoschätzung (0-1).";
        if (input.LiquidityScore is < 0 or > 1)
            return "Ungültige Eingabe: Liquiditätsschätzung muss zwischen 0 und 1 liegen.";
        if (input.RiskScore is < 0 or > 1)
            return "Ungültige Eingabe: Risikoschätzung muss zwischen 0 und 1 liegen.";
        if (input.ExpectedFillDays is { } days and <= 0)
            return "Ungültige Eingabe: Füllzeit muss positiv sein (in Tagen); fehlende Füllzeit ist zulässig, 0 oder negativ nicht.";
        return null;
    }

    // --- Dimensionen ---

    private static TradeRankingDimension NetProfitDimensionValue(TradeRankingInput input, TradeRankingAssumptions assumptions)
    {
        var profit = input.EstimatedProfit!.Value;
        return new(NetProfitDimension, (double)profit, "ISK", assumptions.NetProfitWeight, HigherIsBetter: true,
            $"Netto-Gewinn nach Gebühren: {profit:N0} ISK (Eingabe).");
    }

    private static TradeRankingDimension RoiDimensionValue(TradeRankingInput input, TradeRankingAssumptions assumptions)
    {
        var profit = (double)input.EstimatedProfit!.Value;
        var capital = (double)input.RequiredCapital!.Value;
        var roi = profit / capital * 100.0;
        return new(RoiDimension, roi, "%", assumptions.RoiWeight, HigherIsBetter: true,
            $"ROI = Netto-Gewinn / Kapital × 100 = {profit:N0} / {capital:N0} × 100 = {roi:F2} %.");
    }

    private static TradeRankingDimension CapitalBindingDimensionValue(TradeRankingInput input, TradeRankingAssumptions assumptions)
    {
        if (input.ExpectedFillDays is not { } days)
        {
            return new(CapitalBindingDimension, null, "ISK-Tage", assumptions.CapitalBindingWeight, HigherIsBetter: false,
                "Nicht berechenbar: keine Füllzeit-Annahme vorhanden.");
        }

        var capital = (double)input.RequiredCapital!.Value;
        var binding = capital * days;
        return new(CapitalBindingDimension, binding, "ISK-Tage", assumptions.CapitalBindingWeight, HigherIsBetter: false,
            $"Kapitalbindung = Kapital × realistische Füllzeit = {capital:N0} × {days:0.##} Tage = {binding:N0} ISK-Tage (niedriger = besser).");
    }

    private static TradeRankingDimension IskPerHourDimensionValue(TradeRankingInput input, TradeRankingAssumptions assumptions)
    {
        if (input.ExpectedFillDays is not { } days)
        {
            return new(IskPerHourDimension, null, "ISK/Stunde", assumptions.IskPerHourWeight, HigherIsBetter: true,
                "Nicht berechenbar: keine Füllzeit-Annahme vorhanden.");
        }

        var profit = (double)input.EstimatedProfit!.Value;
        var realisticHours = days * HoursPerDay * assumptions.RealisticFillTimeMultiplier;
        var perHour = profit / realisticHours;
        return new(IskPerHourDimension, perHour, "ISK/Stunde", assumptions.IskPerHourWeight, HigherIsBetter: true,
            $"ISK/Stunde (realistisch) = Netto-Gewinn / (Füllzeit × Faktor) = {profit:N0} / ({days:0.##} × 24 h × {assumptions.RealisticFillTimeMultiplier:0.##}) = {perHour:N0} ISK/h. Annahme, keine exakte Füllzeit.");
    }

    // --- Szenarien ---

    private static IReadOnlyList<TradeRankingScenario> BuildScenarios(TradeRankingInput input, TradeRankingAssumptions assumptions)
    {
        if (input.ExpectedFillDays is not { } days)
            return Array.Empty<TradeRankingScenario>();

        var profit = (double)input.EstimatedProfit!.Value;
        var scenarios = new[]
        {
            ("Konservativ", assumptions.ConservativeFillTimeMultiplier),
            ("Realistisch", assumptions.RealisticFillTimeMultiplier),
            ("Optimistisch", assumptions.OptimisticFillTimeMultiplier)
        };

        return scenarios
            .Select(s =>
            {
                var hours = days * HoursPerDay * s.Item2;
                return new TradeRankingScenario(s.Item1, s.Item2, hours, profit / hours);
            })
            .ToList();
    }

    // --- Erklärungen ---

    private static string BuildExplanation(
        TradeRankingInput input,
        TradeRankingAssumptions assumptions,
        IReadOnlyList<TradeRankingDimension> dimensions,
        IReadOnlyList<TradeRankingScenario> scenarios)
    {
        var lines = new List<string>
        {
            $"Bewertung Opportunity {input.TradingOpportunityId} (Algorithmus {AlgorithmVersion}).",
            $"Eingaben: Netto-Gewinn {input.EstimatedProfit:N0} ISK, benötigtes Kapital {input.RequiredCapital:N0} ISK, " +
            $"Liquidität {input.LiquidityScore:F2}, Risiko {input.RiskScore:F2}" +
            (input.ExpectedFillDays is { } d ? $", Füllzeit-Annahme {d:0.##} Tage." : ", keine Füllzeit-Annahme."),
            "Dimensionen (getrennt berechnet und gewichtet):"
        };
        lines.AddRange(dimensions.Select(d => $"  - {d.Name}: {FormatDimValue(d)} — {d.Explanation}"));

        if (scenarios.Count > 0)
        {
            lines.Add("Szenarien (konservativ/realistisch/optimistisch aus expliziten Füllzeit-Faktoren, keine exakte Füllzeit-Behauptung):");
            lines.AddRange(scenarios.Select(s =>
                $"  - {s.Name}: angenommene Füllzeit {s.AssumedFillHours:0.##} h, geschätzte {s.IskPerHour:N0} ISK/Stunde (Faktor {s.FillTimeMultiplier:0.##})."));
        }
        else
        {
            lines.Add("Szenarien: nicht berechenbar (keine Füllzeit-Annahme).");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatDimValue(TradeRankingDimension d)
        => d.Value is { } v ? $"{v:N2} {d.Unit}" : "nicht berechenbar";

    private static string BuildRankExplanation(
        IReadOnlyList<TradeRankingDimension> activeDimensions,
        IReadOnlyList<TradeRankingInput> ranked,
        IReadOnlyList<TradeRankingEntry> entries,
        IReadOnlyList<int> excluded)
    {
        var lines = new List<string>
        {
            $"Rangliste (Algorithmus {AlgorithmVersion}, Szenario „Realistisch“ für Dimensionswerte).",
            "Normalisierung je Dimension: min-max auf 0-100 über alle bewertbaren Kandidaten; Kapitalbindung und Risiko invertiert (niedriger = besser).",
            $"Gewichte (Summe aktiver Gewichte = 1): {string.Join(", ", activeDimensions.Select(d => $"{d.Name} {d.Weight:0.##}"))}.",
            $"Bewertet: {ranked.Count} Kandidaten; ausgeschlossen: {(excluded.Count > 0 ? string.Join(", ", excluded) : "keine")}."
        };
        lines.AddRange(entries.Select(e =>
            $"  Platz {e.Rank}: Opportunity {e.TradingOpportunityId}, Gesamt-Score {e.WeightedScore:0.##} " +
            $"(Punkte: {string.Join(", ", e.DimensionPoints.Select(p => $"{p.Key} {p.Value:0.##}"))})."));
        return string.Join(Environment.NewLine, lines);
    }
}