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
/// Ungültige Annahmen (Füllzeit-Faktoren, Dimensionsgewichte) sind dagegen
/// Konfigurationsfehler des Aufrufers: sie werfen eine
/// <see cref="ArgumentOutOfRangeException"/>, statt NaN/∞ in
/// Normalisierung, Score oder Erklärung gelangen zu lassen.
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
    /// <exception cref="ArgumentOutOfRangeException">Die Annahmen sind ungültig
    /// (nicht endliche oder nicht positive Füllzeit-Faktoren, nicht endliche oder
    /// negative Gewichte, Gewichtssumme nicht positiv).</exception>
    public static TradeRankingResult Evaluate(TradeRankingInput input, TradeRankingAssumptions assumptions)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(assumptions);

        ThrowIfAssumptionsInvalid(assumptions);

        var validationError = Validate(input, assumptions);
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
    /// verrechnet. Fehlende Dimensionswerte (z. B. keine Zeitannahme)
    /// bleiben je Kandidat ungewichtet: kein erfundener 0-Score, die
    /// übrigen Gewichte werden pro Kandidat auf Summe 1 renormalisiert.
    /// Nicht ausführbare Kandidaten werden ausgeschlossen und in der
    /// Erklärung genannt.
    /// </summary>
    public static TradeRankingOutcome Rank(IReadOnlyList<TradeRankingInput> inputs) => Rank(inputs, TradeRankingAssumptions.Default);

    /// <summary>Rangliste über mehrere Kandidaten mit expliziten Annahmen.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Die Annahmen sind ungültig
    /// (nicht endliche oder nicht positive Füllzeit-Faktoren, nicht endliche oder
    /// negative Gewichte, Gewichtssumme nicht positiv).</exception>
    public static TradeRankingOutcome Rank(IReadOnlyList<TradeRankingInput> inputs, TradeRankingAssumptions assumptions)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(assumptions);

        ThrowIfAssumptionsInvalid(assumptions);

        var actionable = new List<TradeRankingInput>();
        var excluded = new List<int>();
        foreach (var input in inputs)
        {
            if (Validate(input, assumptions) is { } reason)
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

        // Dimensionen des realistischem Szenario je Kandidat (deterministisch, ohne Zeitannahme = null).
        // Es werden NUR vorhandene Werte gesammelt: Fehlt einem Kandidaten eine Dimension (z. B.
        // Kapitalbindung/ISK/Stunde ohne Zeitannahme), fällt sie für ihn aus min/max, Punkten und
        // Gewichtung heraus — niemals wird eine erfundene 0 als Dimensionswert behandelt.
        var dimensionValues = new Dictionary<string, Dictionary<int, double>>();
        var activeDimensions = new List<TradeRankingDimension>();
        for (var i = 0; i < actionable.Count; i++)
        {
            var candidate = actionable[i];
            var result = Evaluate(candidate, assumptions);
            foreach (var dimension in result.Dimensions)
            {
                if (dimension.Value is null)
                    continue;
                if (!dimensionValues.TryGetValue(dimension.Name, out var bucket))
                {
                    bucket = new Dictionary<int, double>();
                    dimensionValues[dimension.Name] = bucket;
                    activeDimensions.Add(dimension);
                }
                bucket[i] = dimension.Value.Value;
            }
        }

        var points = new Dictionary<int, Dictionary<string, double>>();
        foreach (var candidate in actionable)
            points[candidate.TradingOpportunityId] = new Dictionary<string, double>();

        foreach (var dimension in activeDimensions)
        {
            var bucket = dimensionValues[dimension.Name];
            var min = bucket.Values.Min();
            var max = bucket.Values.Max();
            var scale = Math.Max(Math.Abs(min), Math.Abs(max));
            // Robust normalisieren (Review-Blocker): (value - min) / (max - min) kann bei
            // extremen, aber endlichen Werten überlaufen (∞ / ∞ = NaN). Die Skalierung mit
            // dem betragsgrößten Wert hält Zähler und Nenner endlich; das Verhältnis —
            // und damit das Ergebnis — bleibt unverändert.
            var span = scale > 0 ? max / scale - min / scale : 0.0;
            foreach (var (index, value) in bucket)
            {
                double normalized = span > 0
                    ? (value / scale - min / scale) / span
                    : 0.5; // alle Kandidaten gleich (oder Spanne 0): neutrale Mitte, kein 0-Spannen-Problem
                if (!dimension.HigherIsBetter)
                    normalized = 1.0 - normalized;
                points[actionable[index].TradingOpportunityId][dimension.Name] = Math.Round(normalized * 100.0, 2);
            }
        }

        // Gewichtung je Kandidat über SEINE aktiven Dimensionen: fehlende Dimensionen tragen weder
        // Punkte noch Gewicht, die restlichen Gewichte werden pro Kandidat auf Summe 1 renormalisiert.
        // Kandidaten, deren Summe der aktiven Gewichte 0 ist (alle ihre Dimensionen sind mit Gewicht 0
        // konfiguriert), sind NICHT rangierbar: die Division durch die Gewichtssumme ergäbe 0/0 = NaN.
        // Sie werden deterministisch ausgeschlossen und in der Erklärung genannt (Review-Blocker).
        var scored = new List<(TradeRankingInput Candidate, double Weighted, IReadOnlyDictionary<string, double> Points)>();
        var notRankable = new List<int>();
        foreach (var candidate in actionable)
        {
            var candidatePoints = points[candidate.TradingOpportunityId];
            var active = activeDimensions.Where(d => candidatePoints.ContainsKey(d.Name)).ToList();
            var weightSum = active.Sum(d => d.Weight);
            if (weightSum <= 0)
            {
                notRankable.Add(candidate.TradingOpportunityId);
                continue;
            }
            scored.Add((candidate, active.Sum(d => candidatePoints[d.Name] * d.Weight) / weightSum, candidatePoints));
        }

        var entries = scored
            .OrderByDescending(x => x.Weighted)
            .Select((x, index) => new TradeRankingEntry(
                x.Candidate.TradingOpportunityId,
                index + 1,
                Math.Round(x.Weighted, 2),
                x.Points))
            .ToList();

        // Nicht ausführbare und nicht rangierbare Kandidaten verlassen die Rangliste sichtbar.
        var excludedIds = excluded.Concat(notRankable).ToList();

        var explanation = BuildRankExplanation(activeDimensions, actionable, entries, excluded, notRankable);

        return new TradeRankingOutcome(AlgorithmVersion, entries, excludedIds, explanation);
    }

    // --- Validierung ---

    private static string? Validate(TradeRankingInput input, TradeRankingAssumptions assumptions)
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
        if (input.LiquidityScore is { } liquidity && !double.IsFinite(liquidity))
            return "Ungültige Eingabe: Liquiditätsschätzung muss endlich sein (NaN/∞ ist kein gültiger Score).";
        if (input.RiskScore is { } risk && !double.IsFinite(risk))
            return "Ungültige Eingabe: Risikoschätzung muss endlich sein (NaN/∞ ist kein gültiger Score).";
        if (input.LiquidityScore is < 0 or > 1)
            return "Ungültige Eingabe: Liquiditätsschätzung muss zwischen 0 und 1 liegen.";
        if (input.RiskScore is < 0 or > 1)
            return "Ungültige Eingabe: Risikoschätzung muss zwischen 0 und 1 liegen.";
        if (input.ExpectedFillDays is { } days && !double.IsFinite(days))
            return "Ungültige Eingabe: Füllzeit muss endlich sein (NaN/∞ ist kein gültiger Zeitwert).";
        if (input.ExpectedFillDays is { } d and <= 0)
            return "Ungültige Eingabe: Füllzeit muss positiv sein (in Tagen); fehlende Füllzeit ist zulässig, 0 oder negativ nicht.";
        if (ValidateDerivedValues(input, assumptions) is { } derivedError)
            return derivedError;
        return null;
    }

    /// <summary>
    /// Prüft, ob die aus Füllzeit, Kapital und Gewinn ABGELEITETEN Werte
    /// (Kapitalbindung, Szenario-Stunden, ISK/Stunde) endlich bleiben. Endliche
    /// Eingaben können bei Extremwerten im Produkt überlaufen (z. B.
    /// Kapital × Füllzeit = ∞ oder Gewinn / sehr kleine Stundenzahl = ∞);
    /// der Kandidat ist dann deterministisch NICHT ausführbar, statt NaN/∞ in
    /// Dimensionen, Szenarien oder Score gelangen zu lassen (Review-Blocker:
    /// keine erfundenen 0/∞-Ergebnisse aus großen endlichen Füllzeiten).
    /// </summary>
    private static string? ValidateDerivedValues(TradeRankingInput input, TradeRankingAssumptions assumptions)
    {
        if (input.ExpectedFillDays is not { } days)
            return null;

        var capital = (double)input.RequiredCapital!.Value;
        var profit = (double)input.EstimatedProfit!.Value;

        if (!double.IsFinite(capital * days))
            return "Ungültige Eingabe: Kapital × Füllzeit überschreitet den darstellbaren Zahlenbereich — die Kapitalbindung wäre nicht endlich (Füllzeit verkleinern).";

        var fillTimeFactors = new (string Name, double Value)[]
        {
            ("konservativ", assumptions.ConservativeFillTimeMultiplier),
            ("realistisch", assumptions.RealisticFillTimeMultiplier),
            ("optimistisch", assumptions.OptimisticFillTimeMultiplier)
        };
        foreach (var (name, factor) in fillTimeFactors)
        {
            var hours = days * HoursPerDay * factor;
            if (!double.IsFinite(hours) || hours <= 0)
                return $"Ungültige Eingabe: Füllzeit × 24 h × Faktor „{name}“ liegt außerhalb des darstellbaren Zahlenbereichs — keine endliche Szenario-Dauer möglich.";
            if (!double.IsFinite(profit / hours))
                return $"Ungültige Eingabe: ISK/Stunde im Szenario „{name}“ wäre mit dieser Füllzeit nicht endlich darstellbar.";
        }

        return null;
    }

    // --- Annahmen ---

    /// <summary>
    /// Wirft, wenn die expliziten Annahmen ungültig sind. Annahmen sind
    /// Konfiguration des Aufrufers (keine Kandidatendaten): sie werden nicht
    /// still auf Defaults korrigiert und ergeben keinen „nicht bewertbar“-Kandidaten,
    /// sondern einen sofortigen Abbruch — so können 0-, negative, NaN- oder
    /// ∞-Faktoren und -Gewichte niemals NaN/∞ in Dimensionswerte, Normalisierung,
    /// Score oder Erklärung einbringen.
    /// </summary>
    private static void ThrowIfAssumptionsInvalid(TradeRankingAssumptions assumptions)
    {
        if (ValidateAssumptions(assumptions) is { } error)
            throw new ArgumentOutOfRangeException(nameof(assumptions), error);
    }

    /// <summary>
    /// Prüft Faktoren und Gewichte der Annahmen. Füllzeit-Faktoren müssen endlich
    /// und positiv sein; Gewichte endlich, nicht negativ und in Summe endlich und
    /// positiv. Die Gewichte müssen NICHT auf 1 summieren: der Gesamt-Score wird je
    /// Kandidat über dessen aktive Dimensionen auf Summe 1 renormalisiert.
    /// </summary>
    private static string? ValidateAssumptions(TradeRankingAssumptions assumptions)
    {
        var fillTimeFactors = new (string Name, double Value)[]
        {
            ("konservativ", assumptions.ConservativeFillTimeMultiplier),
            ("realistisch", assumptions.RealisticFillTimeMultiplier),
            ("optimistisch", assumptions.OptimisticFillTimeMultiplier)
        };
        foreach (var (name, value) in fillTimeFactors)
        {
            if (!double.IsFinite(value) || value <= 0)
                return $"Ungültige Annahme: Füllzeit-Faktor „{name}“ muss endlich und positiv sein (0, negative Werte, NaN und ∞ ergeben keine gültige Füllzeit).";
        }

        var weights = new (string Name, double Value)[]
        {
            (NetProfitDimension, assumptions.NetProfitWeight),
            (RoiDimension, assumptions.RoiWeight),
            (CapitalBindingDimension, assumptions.CapitalBindingWeight),
            (LiquidityDimension, assumptions.LiquidityWeight),
            (RiskDimension, assumptions.RiskWeight),
            (IskPerHourDimension, assumptions.IskPerHourWeight)
        };
        foreach (var (name, value) in weights)
        {
            if (!double.IsFinite(value) || value < 0)
                return $"Ungültige Annahme: Gewicht für „{name}“ muss endlich und nicht negativ sein (negative Werte, NaN und ∞ ergeben keinen gültigen Score).";
        }

        var weightSum = weights.Sum(w => w.Value);
        if (!double.IsFinite(weightSum) || weightSum <= 0)
            return "Ungültige Annahme: die Gewichtssumme muss positiv und endlich sein (Gewichtssumme 0 oder Überlauf ergibt keinen Score).";

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
        IReadOnlyList<int> excluded,
        IReadOnlyList<int> notRankable)
    {
        var lines = new List<string>
        {
            $"Rangliste (Algorithmus {AlgorithmVersion}, Szenario „Realistisch“ für Dimensionswerte).",
            "Normalisierung je Dimension: min-max auf 0-100 über alle Kandidaten mit vorhandenem Wert; Kapitalbindung und Risiko invertiert (niedriger = besser). Fehlende Werte (z. B. keine Zeitannahme) bleiben außen vor — keine erfundenen Nullen.",
            $"Gewichte (Summe aktiver Gewichte = 1, je Kandidat auf dessen aktive Dimensionen renormalisiert): {string.Join(", ", activeDimensions.Select(d => $"{d.Name} {d.Weight:0.##}"))}.",
            $"Bewertet: {ranked.Count} Kandidaten; ausgeschlossen: {(excluded.Count > 0 ? string.Join(", ", excluded) : "keine")}." +
            $" Nicht rangierbar (kein positives aktives Gewicht, keine Division möglich): {(notRankable.Count > 0 ? string.Join(", ", notRankable) : "keine")}."
        };
        lines.AddRange(entries.Select(e =>
        {
            var missing = activeDimensions
                .Where(d => !e.DimensionPoints.ContainsKey(d.Name))
                .Select(d => d.Name)
                .ToList();
            var missingText = missing.Count > 0 ? $"; fehlend und ungewichtet: {string.Join(", ", missing)}" : string.Empty;
            return $"  Platz {e.Rank}: Opportunity {e.TradingOpportunityId}, Gesamt-Score {e.WeightedScore:0.##} " +
                   $"(Punkte: {string.Join(", ", e.DimensionPoints.Select(p => $"{p.Key} {p.Value:0.##}"))}{missingText}).";
        }));
        return string.Join(Environment.NewLine, lines);
    }
}