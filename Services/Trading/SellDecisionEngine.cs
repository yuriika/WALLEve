using WALLEve.Services.Market.Interfaces;

namespace WALLEve.Services.Trading;

/// <summary>
/// Ortsgenaue Verkaufsentscheidung (Issue #61): vergleicht für eine Bestandsmenge an
/// EINEM Ort drei Optionen mit getrennten Zahlen —
/// „Sell-now" (sofortige Ausführung gegen vorhandene, den Ort erreichende Buy-Orders),
/// „List" (neue Sell-Order auf einem gültigen Tick-/Queue-Ziel unter der besten Quote)
/// und „Hold" (nicht verkaufen) — und gibt EINE begründete Empfehlung samt Belegen aus.
///
/// Grundsätze:
/// - Die Menge ist doppelt begrenzt: durch den BESITZ am Ort und durch die vorhandene,
///   erreichbare TIEFE. Bestände und Orders an anderen Orten aus anderen Asset-Orten
///   werden nie stillschweigend mitverkauft (kein Route-Trading, kein Transfer).
/// - Gebühren werden genau EINMAL je ausgeführter Menge berechnet (Broker-Fee beim
///   Einstellen, Sales Tax beim Verkauf über <see cref="IFeeCalculatorService.CalculateSellProceeds"/>);
///   die gespeicherte Cost Basis enthält die Erwerbskosten bereits einmal, daher wird
///   für den Break-even <see cref="IFeeCalculatorService.CalculateBreakEvenSellPriceForStoredBasis"/>
///   verwendet — keine zweite Buy-Gebühr.
/// - Unvollständige Pflichtdaten (fehlender Besitz, fehlende/ungültige Cost Basis,
///   fehlgeschlagenes Fremd-Orderbuch) ergeben KEINE Empfehlung, sondern eine deutsche
///   Begründung. Es werden keine Mengen, Preise oder Gebühren erfunden.
/// Reine, zustandsfreie Logik: keine Datenbank, kein ESI, kein LLM, keine Uhrzeit —
/// gleiche Eingaben ergeben exakt dasselbe Ergebnis und dieselben Belege.
/// </summary>
public static class SellDecisionEngine
{
    /// <summary>Version der Analyse; Bestandteil jedes Ergebnisses.</summary>
    public const string AlgorithmVersion = "sell-decision-v1";

    /// <summary>EVE-Marktschritt unterhalb 1.000 ISK (zwei Dezimalstellen).</summary>
    public const double TickBelowThousand = 0.01;

    /// <summary>EVE-Marktschritt ab 1.000 ISK (ganze ISK).</summary>
    public const double TickAtLeastThousand = 1.0;

    /// <summary>Preis-Toleranz für Vergleiche „gleicher Preis" (wie im Orderbuch-Kontext).</summary>
    private const double PriceEqualityTolerance = 0.005;

    /// <summary>
    /// Marktschritt für einen Referenzpreis. Annahme (keine ESI-Angabe): unter 1.000 ISK
    /// ist der kleinste Schritt 0,01 ISK, ab 1.000 ISK 1 ISK — der Zielpreis liegt damit
    /// immer auf einem gültigen Tick und niemals zwischen zwei handelbaren Stufen.
    /// </summary>
    public static double DeriveTickSize(double referencePrice)
        => referencePrice >= 1000.0 ? TickAtLeastThousand : TickBelowThousand;

    /// <summary>
    /// Rundet einen Preis auf eine gültige Tick-Stufe AB. Aufrunden würde einen höheren
    /// Preis liefern, als die Unterbietung verlangt — und damit eine ungültige Absicht verschleiern.
    /// </summary>
    public static double RoundDownToTick(double price, double tickSize)
    {
        if (tickSize <= 0) return price;
        var steps = Math.Floor(price / tickSize + 1e-9);
        return Math.Round(steps * tickSize, 6);
    }

    /// <summary>
    /// Evaluiert die drei Optionen und empfiehlt genau eine. Wirft nur bei ungültiger
    /// Konfiguration des Aufrufers (nicht endlicher/nicht positiver Tick-Override), nie bei
    /// unvollständigen Kandidatendaten — die ergeben eine begründete „keine Empfehlung".
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Der übergebene Tick-Schritt ist
    /// nicht endlich oder nicht positiv; er würde jedes Tick-Ziel ungültig machen.</exception>
    public static SellDecisionResult Evaluate(SellDecisionInput input, IFeeCalculatorService fees)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(fees);

        if (input.TickSizeOverride is { } overrideTick && (!double.IsFinite(overrideTick) || overrideTick <= 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(input),
                "Ungültiger Tick-Schritt (muss endlich und positiv sein) — ein Tick-Ziel ist damit nicht bestimmbar.");
        }

        var owned = input.OwnedQuantityAtLocation;

        // --- Pflichtdaten: fehlend ⇒ keine Empfehlung (kein erfundener Marktzustand) ---
        if (input.ForeignDataStatus == OrderBookDataStatus.Failed)
        {
            var reason = "Fremd-Orderbuch nicht verfügbar (ESI-Abruf fehlgeschlagen) — " +
                         "Nachfrage, Quote und Tick-Ziel sind nicht bewertbar. Keine Empfehlung.";
            return NotActionable(reason, input, owned);
        }

        if (owned <= 0)
        {
            var reason = $"Keine Bestandsmenge am Ort {LocationLabel(input)} — ohne Besitz vor Ort " +
                         "gibt es dort nichts zu verkaufen (Bestände an anderen Orten zählen nicht). Keine Empfehlung.";
            return NotActionable(reason, input, owned);
        }

        if (input.CostBasisPerUnit is not { } costBasis)
        {
            var reason = "Cost Basis unbekannt — Break-even und Netto-Gewinn sind nicht belegbar. Keine Empfehlung.";
            return NotActionable(reason, input, owned);
        }

        if (!double.IsFinite(costBasis) || costBasis <= 0)
        {
            var reason = "Ungültige Cost Basis (muss endlich und positiv sein) — " +
                         "Break-even und Netto-Gewinn sind nicht belegbar. Keine Empfehlung.";
            return NotActionable(reason, input, owned);
        }

        var buyDepth = ReachableBuyDepth(input.BuySide);
        var sellQuotes = CompetingSellQuotes(input.SellSide);
        var ignoredBuyOrders = IgnoredBuyOrderCount(input.BuySide);
        var foreignSellOrders = IgnoredSellQuoteCount(input.SellSide);

        var breakEven = fees.CalculateBreakEvenSellPriceForStoredBasis(costBasis, input.Skills);

        var sellNow = BuildSellNow(input, fees, costBasis, breakEven, owned, buyDepth);
        var list = BuildList(input, fees, costBasis, breakEven, owned, sellQuotes);

        var evidence = BuildEvidence(input, owned, buyDepth, sellNow, list, sellQuotes,
            ignoredBuyOrders, foreignSellOrders, costBasis, breakEven);

        var (action, reasonText) = Recommend(sellNow, list, owned, costBasis, breakEven);

        return new SellDecisionResult
        {
            AlgorithmVersion = AlgorithmVersion,
            IsActionable = true,
            NotActionableReason = null,
            SellNow = sellNow,
            List = list,
            Hold = new SellHoldPlan
            {
                Quantity = owned,
                Reason = action == SellDecisionAction.Hold
                    ? reasonText
                    : $"{owned} Stück bleiben unverkauft; eine Verkaufsoption ist belegt vorteilhafter."
            },
            RecommendedAction = action,
            RecommendationReason = reasonText,
            Evidence = evidence
        };
    }

    // --- Option „Sell-now": sofortige Ausführung gegen vorhandene Buy-Orders ---

    private static SellNowPlan BuildSellNow(
        SellDecisionInput input,
        IFeeCalculatorService fees,
        double costBasis,
        double breakEven,
        int owned,
        IReadOnlyList<OrderBookLine> buyDepth)
    {
        if (buyDepth.Count == 0)
        {
            return new SellNowPlan
            {
                IsEvaluable = false,
                NotEvaluableReason = "Keine erreichbare Buy-Order am Ort — eine sofortige Ausführung ist nicht möglich.",
                BreakEvenPrice = breakEven
            };
        }

        var remaining = owned;
        var levelQuantities = new List<(double Price, int Quantity)>();
        foreach (var order in buyDepth)
        {
            if (remaining <= 0) break;
            var take = Math.Min(remaining, order.VolumeRemain);
            if (take <= 0) continue;
            levelQuantities.Add((order.Price, take));
            remaining -= take;
        }

        var executed = levelQuantities.Sum(l => l.Quantity);
        if (executed <= 0)
        {
            return new SellNowPlan
            {
                IsEvaluable = false,
                NotEvaluableReason = "Die erreichbaren Buy-Orders haben keine ausführbare Restmenge — keine Ausführung möglich.",
                BreakEvenPrice = breakEven
            };
        }

        // Gebühren GENAU EINMAL je ausgeführter Stufe: die Summe der Stufen-Ergebnisse ist
        // identisch zur Rechnung auf die Gesamtmenge, weil Broker-Fee und Sales Tax linear
        // am Wert hängen — und sie bleibt korrekt, falls ein Rechner eine Mindestgebühr ansetzt.
        double gross = 0, broker = 0, tax = 0, net = 0;
        foreach (var (price, quantity) in levelQuantities)
        {
            var result = fees.CalculateSellProceeds(price, quantity, input.Skills);
            gross += result.GrossAmount;
            broker += result.BrokerFee;
            tax += result.SalesTax;
            net += result.NetAmount;
        }

        var acquisitionCost = costBasis * executed;
        var netProfit = net - acquisitionCost;
        var averagePrice = gross / executed;

        return new SellNowPlan
        {
            IsEvaluable = true,
            NotEvaluableReason = null,
            Quantity = executed,
            UnfilledQuantity = owned - executed,
            IsPartialFill = executed < owned,
            BestPrice = levelQuantities[0].Price,
            AveragePrice = averagePrice,
            LevelsUsed = levelQuantities.Select(l => l.Price).Distinct().Count(),
            GrossAmount = gross,
            BrokerFee = broker,
            SalesTax = tax,
            NetAmount = net,
            EffectiveFeeRatePercent = gross > 0 ? (gross - net) / gross * 100.0 : 0,
            BreakEvenPrice = breakEven,
            NetProfit = netProfit,
            RoiPercent = acquisitionCost > 0 ? netProfit / acquisitionCost * 100.0 : null
        };
    }

    // --- Option „List": neue Sell-Order auf gültigem Tick-/Queue-Ziel ---

    private static SellListPlan BuildList(
        SellDecisionInput input,
        IFeeCalculatorService fees,
        double costBasis,
        double breakEven,
        int owned,
        IReadOnlyList<OrderBookLine> sellQuotes)
    {
        if (sellQuotes.Count == 0)
        {
            return new SellListPlan
            {
                IsEvaluable = false,
                NotEvaluableReason = "Kein Angebot am eigenen Ort (keine Vergleichsquote) — " +
                                     "ein gültiges Tick-/Queue-Ziel ist nicht bestimmbar.",
                BreakEvenPrice = breakEven
            };
        }

        var bestCompeting = sellQuotes[0].Price;
        var tick = input.TickSizeOverride ?? DeriveTickSize(bestCompeting);
        var target = RoundDownToTick(bestCompeting - tick, tick);

        if (target <= 0)
        {
            return new SellListPlan
            {
                IsEvaluable = false,
                NotEvaluableReason = $"Die beste Quote {bestCompeting:N2} ISK liegt unter einem Tick-Schritt — " +
                                     "es gibt keinen positiven Tick-Zielpreis.",
                TickSize = tick,
                BestCompetingSellPrice = bestCompeting,
                BreakEvenPrice = breakEven
            };
        }

        // Queue: alles mit Preis < Zielpreis steht vor der neuen Order; bei gleichem Preis
        // (Abrundung auf die Tick-Stufe) ist die bestehende Order älter und wird zuerst bedient
        // (FIFO). Das Angebot über dem Zielpreis wird erst nach der neuen Order bedient.
        var tolerance = Math.Max(tick / 2.0, PriceEqualityTolerance);
        var cheaperAhead = sellQuotes.Where(q => q.Price <= target + tolerance).ToList();
        var behind = sellQuotes.Where(q => q.Price > target + tolerance).ToList();

        var proceeds = fees.CalculateSellProceeds(target, owned, input.Skills);
        var acquisitionCost = costBasis * owned;
        var netProfit = proceeds.NetAmount - acquisitionCost;

        return new SellListPlan
        {
            IsEvaluable = true,
            NotEvaluableReason = null,
            TickSize = tick,
            BestCompetingSellPrice = bestCompeting,
            TargetPrice = target,
            UndercutAmount = bestCompeting - target,
            Quantity = owned,
            CheaperQuantityAhead = cheaperAhead.Sum(q => q.VolumeRemain),
            CompetingQuantityBehind = behind.Sum(q => q.VolumeRemain),
            QueuePosition = cheaperAhead.Count + 1,
            WouldBeBestAtLocation = cheaperAhead.Count == 0,
            GrossAmount = proceeds.GrossAmount,
            BrokerFee = proceeds.BrokerFee,
            SalesTax = proceeds.SalesTax,
            NetAmount = proceeds.NetAmount,
            EffectiveFeeRatePercent = proceeds.EffectiveFeeRatePercent,
            BreakEvenPrice = breakEven,
            NetProfit = netProfit,
            RoiPercent = acquisitionCost > 0 ? netProfit / acquisitionCost * 100.0 : null,
            IsBelowBreakEven = target < breakEven - tolerance
        };
    }

    // --- Empfehlung ---

    private static (string Action, string Reason) Recommend(
        SellNowPlan sellNow,
        SellListPlan list,
        int owned,
        double costBasis,
        double breakEven)
    {
        var evaluable = new List<(string Action, double Profit, string Detail)>();
        if (sellNow.IsEvaluable)
        {
            var profit = sellNow.NetProfit ?? double.NegativeInfinity;
            evaluable.Add((SellDecisionAction.SellNow, profit,
                $"Sell-now: {sellNow.Quantity} Stück über {sellNow.LevelsUsed} Preisstufe(n), " +
                $"Ø {sellNow.AveragePrice:N2} ISK, netto {sellNow.NetAmount:N0} ISK, Gewinn {profit:N0} ISK."));
        }
        if (list.IsEvaluable)
        {
            var profit = list.NetProfit ?? double.NegativeInfinity;
            evaluable.Add((SellDecisionAction.List, profit,
                $"List {list.TargetPrice:N2} ISK (Tick {list.TickSize:0.##}): {list.Quantity} Stück, " +
                $"Queue-Position {list.QueuePosition}, Netto bei vollständiger Füllung {list.NetAmount:N0} ISK, " +
                $"Gewinn {profit:N0} ISK."));
        }

        if (evaluable.Count == 0)
        {
            var reasons = new List<string>();
            if (!sellNow.IsEvaluable) reasons.Add(sellNow.NotEvaluableReason ?? "Sell-now nicht bewertbar.");
            if (!list.IsEvaluable) reasons.Add(list.NotEvaluableReason ?? "List nicht bewertbar.");
            return (SellDecisionAction.Hold,
                $"Hold: {string.Join(" ", reasons)} Nicht verkaufen ist damit die einzige belegbare Option.");
        }

        var profitable = evaluable.Where(e => e.Profit > 0).ToList();
        if (profitable.Count == 0)
        {
            var best = evaluable.OrderByDescending(e => e.Profit).First();
            return (SellDecisionAction.Hold,
                $"Hold: keine Option erreicht die Cost Basis von {costBasis:N0} ISK/Stück " +
                $"(Break-even {breakEven:N2} ISK). {string.Join(" ", evaluable.Select(e => e.Detail))} " +
                "Ein Verkauf wäre ein belegter Verlust.");
        }

        // Bei gleichem belegbaren Gewinn gewinnt Sell-now: die Ausführung ist durch die
        // vorhandene Tiefe belegt, während eine List-Order erst gefüllt werden muss.
        var winner = profitable
            .OrderByDescending(e => e.Profit)
            .ThenBy(e => e.Action == SellDecisionAction.SellNow ? 0 : 1)
            .First();

        var runnerUp = profitable.FirstOrDefault(e => e.Action != winner.Action);
        var comparison = runnerUp.Detail is null
            ? "Die andere Option ist nicht bewertbar."
            : $"Alternative: {runnerUp.Detail}";
        var residual = winner.Action == SellDecisionAction.SellNow && sellNow.IsPartialFill
            ? $" {sellNow.UnfilledQuantity} Stück deckt die Nachfrage nicht ab."
            : string.Empty;

        return (winner.Action,
            $"Empfehlung {winner.Action}: {winner.Detail}{residual} {comparison} " +
            $"Menge durch Besitz ({owned} Stück am Ort) und vorhandene Tiefe begrenzt.");
    }

    // --- Belege ---

    private static IReadOnlyList<string> BuildEvidence(
        SellDecisionInput input,
        int owned,
        IReadOnlyList<OrderBookLine> buyDepth,
        SellNowPlan sellNow,
        SellListPlan list,
        IReadOnlyList<OrderBookLine> sellQuotes,
        int ignoredBuyOrders,
        int ignoredSellQuotes,
        double costBasis,
        double breakEven)
    {
        var evidence = new List<string>
        {
            $"Ort {LocationLabel(input)}: {owned} Stück im Besitz; Bestände an anderen Orten sind nicht Teil dieser Entscheidung."
        };

        evidence.Add(buyDepth.Count == 0
            ? "Sofortige Ausführung: keine erreichbare Buy-Order am Ort."
            : $"Sofortige Ausführung: {buyDepth.Count} erreichbare Buy-Order(s) am Ort, " +
              $"bester Preis {buyDepth[0].Price:N2} ISK, ausführbar insgesamt " +
              $"{buyDepth.Sum(o => o.VolumeRemain)} Stück.");

        if (ignoredBuyOrders > 0)
        {
            evidence.Add($"{ignoredBuyOrders} Buy-Order(s) erreichen den Ort nicht und wurden nicht als Nachfrage gewertet.");
        }
        if (ignoredSellQuotes > 0)
        {
            evidence.Add($"{ignoredSellQuotes} Sell-Order(s) an anderen Orten wurden nicht als Konkurrenz gewertet.");
        }

        if (sellNow.IsEvaluable)
        {
            evidence.Add($"Sell-now: {sellNow.Quantity} Stück über {sellNow.LevelsUsed} Preisstufe(n) " +
                         $"(bester {sellNow.BestPrice:N2}, Ø {sellNow.AveragePrice:N2} ISK), " +
                         $"Brutto {sellNow.GrossAmount:N0} ISK − Broker {sellNow.BrokerFee:N0} − Sales Tax {sellNow.SalesTax:N0} " +
                         $"= netto {sellNow.NetAmount:N0} ISK (Gebühren genau einmal, Satz {sellNow.EffectiveFeeRatePercent:F2} %).");
        }
        else
        {
            evidence.Add($"Sell-now: {sellNow.NotEvaluableReason}");
        }

        if (list.IsEvaluable)
        {
            evidence.Add($"List: gültiger Tick {list.TickSize:0.##} ISK, Zielpreis {list.TargetPrice:N2} ISK " +
                         $"({list.UndercutAmount:N2} ISK unter der besten Quote {list.BestCompetingSellPrice:N2} ISK), " +
                         $"Queue-Ziel Position {list.QueuePosition} " +
                         $"({list.CheaperQuantityAhead} Stück billiger davor, {list.CompetingQuantityBehind} Stück konkurrieren erst dahinter), " +
                         $"Netto bei vollständiger Füllung {list.NetAmount:N0} ISK " +
                         $"(Broker {list.BrokerFee:N0} + Sales Tax {list.SalesTax:N0} genau einmal).");
            if (list.IsBelowBreakEven)
            {
                evidence.Add($"List-Ziel {list.TargetPrice:N2} ISK liegt unter dem Break-even {list.BreakEvenPrice:N2} ISK.");
            }
        }
        else
        {
            evidence.Add($"List: {list.NotEvaluableReason}");
        }

        evidence.Add($"Cost Basis {costBasis:N0} ISK/Stück enthält die Erwerbskosten genau einmal — " +
                     $"Break-even {breakEven:N2} ISK ohne zweite Buy-Gebühr.");

        BuildDataQualityNotes(input, buyDepth, sellQuotes).ForEach(evidence.Add);
        return evidence;
    }

    private static List<string> BuildDataQualityNotes(
        SellDecisionInput input,
        IReadOnlyList<OrderBookLine> buyDepth,
        IReadOnlyList<OrderBookLine> sellQuotes)
    {
        var notes = new List<string>();
        if (input.ForeignDataStatus == OrderBookDataStatus.Empty)
        {
            notes.Add("Datenlage: gültig leeres Fremd-Orderbuch (keine fremden Orders in der Region geladen).");
        }
        else if (buyDepth.Count == 0 && sellQuotes.Count == 0)
        {
            notes.Add("Datenlage: keine fremde Order am eigenen Ort — keine Vergleichswerte vorhanden.");
        }
        if (sellQuotes.Count == 0)
        {
            notes.Add("Datenlage: kein Angebot am eigenen Ort — ein Tick-/Queue-Ziel ist daraus nicht bestimmbar.");
        }
        if (input.Skills is null)
        {
            notes.Add("Skills fehlen — die Gebührensätze stammen aus der konservativen Schätzung des Gebührenrechners.");
        }
        return notes;
    }

    // --- Tiefe/Quoten (Ort und Erreichbarkeit) ---

    /// <summary>
    /// Ausführbare Nachfrage am eigenen Ort: fremde Buy-Orders mit Restmenge, deren Range den
    /// Ort erreicht. Sortiert wie im EVE-Markt (höchster Preis zuerst, bei gleichem Preis die
    /// ältere Order). Reine Funktion — keine DB-/ESI-Zugriffe.
    /// </summary>
    public static IReadOnlyList<OrderBookLine> ReachableBuyDepth(IReadOnlyList<OrderBookLine>? buySide)
        => (buySide ?? Array.Empty<OrderBookLine>())
            .Where(o => !o.IsOwn && o.IsBuyOrder && o.VolumeRemain > 0 && o.CanReachOwnLocation)
            .OrderByDescending(o => o.Price)
            .ThenBy(o => o.Issued)
            .ThenByDescending(o => o.VolumeRemain)
            .ToList();

    /// <summary>
    /// Konkurrenz-Angebot am eigenen Ort: fremde Sell-Orders mit Restmenge an derselben
    /// Location, aufsteigend nach Preis (billigste zuerst, bei gleichem Preis die ältere Order).
    /// </summary>
    public static IReadOnlyList<OrderBookLine> CompetingSellQuotes(IReadOnlyList<OrderBookLine>? sellSide)
        => (sellSide ?? Array.Empty<OrderBookLine>())
            .Where(o => !o.IsOwn && !o.IsBuyOrder && o.VolumeRemain > 0 && o.IsSameLocation)
            .OrderBy(o => o.Price)
            .ThenBy(o => o.Issued)
            .ThenByDescending(o => o.VolumeRemain)
            .ToList();

    /// <summary>Buy-Orders, die als Nachfrage ausgeschlossen wurden (kein erreichbarer Ort / eigene Order).</summary>
    public static int IgnoredBuyOrderCount(IReadOnlyList<OrderBookLine>? buySide)
        => (buySide ?? Array.Empty<OrderBookLine>())
            .Count(o => !o.IsOwn && o.IsBuyOrder && o.VolumeRemain > 0 && !o.CanReachOwnLocation);

    /// <summary>Sell-Orders an anderen Orten — kein Konkurrenzangebot am eigenen Ort.</summary>
    public static int IgnoredSellQuoteCount(IReadOnlyList<OrderBookLine>? sellSide)
        => (sellSide ?? Array.Empty<OrderBookLine>())
            .Count(o => !o.IsOwn && !o.IsBuyOrder && o.VolumeRemain > 0 && !o.IsSameLocation);

    private static SellDecisionResult NotActionable(string reason, SellDecisionInput input, int owned)
        => new()
        {
            AlgorithmVersion = AlgorithmVersion,
            IsActionable = false,
            NotActionableReason = reason,
            SellNow = null,
            List = null,
            Hold = new SellHoldPlan { Quantity = Math.Max(0, owned), Reason = reason },
            RecommendedAction = SellDecisionAction.Hold,
            RecommendationReason = reason,
            Evidence = new[]
            {
                $"Ort {LocationLabel(input)}: {owned} Stück im Besitz.",
                $"Datenlage: Fremd-Orderbuch-Status {input.ForeignDataStatus}.",
                reason
            }
        };

    private static string LocationLabel(SellDecisionInput input)
        => input.LocationName is { Length: > 0 } name ? $"{name} ({input.LocationId})" : input.LocationId.ToString();
}
