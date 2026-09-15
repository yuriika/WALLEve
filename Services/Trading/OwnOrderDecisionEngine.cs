using WALLEve.Models.Esi.Character;
using WALLEve.Services.Market.Interfaces;

namespace WALLEve.Services.Trading;

/// <summary>
/// Entscheidung über eine EIGENE Order (Issue #64): vergleicht für die bestehende Order
/// „unverändert lassen" (Wait), „Modify" (Preis ändern, Relist-Gebühr) und
/// „Cancel-Recreate" (stornieren und neu einstellen, volle Broker-Fee) mit getrennten Zahlen
/// und gibt EINE begründete Empfehlung samt Formel und Annahmen aus.
///
/// Grundsätze:
/// - Die Queue wird inklusive Buy-Range bewertet: konkurrierende Orders am eigenen Ort bilden
///   die eigene Preisschlange, und nur Buy-Orders, deren Range den eigenen Ort erreicht
///   (<see cref="OrderBookLine.CanReachOwnLocation"/>, #31), sind Nachfrage bzw. Konkurrenz.
/// - Ein Preis wird nur empfohlen, wenn der erwartete Mehrwert die Aktionsgebühr
///   ÜBERSTEIGT (echte Verbesserung, nicht blindes Unterbieten).
/// - Es wird NICHTS ausgeführt: keine ESI-Schreiborder, kein Storno, kein Einstellen.
/// - Fehlende Pflichtdaten (fehlgeschlagenes Fremd-Orderbuch, keine Restmenge) ergeben KEINE
///   Empfehlung, sondern eine deutsche Begründung. Es werden keine Zahlen erfunden.
/// Reine, zustandsfreie Logik: keine Datenbank, kein ESI, kein LLM, keine Uhrzeit — gleiche
/// Eingaben ergeben exakt dasselbe Ergebnis und dieselben Belege.
/// </summary>
public static class OwnOrderDecisionEngine
{
    /// <summary>Version der Analyse; Bestandteil jedes Ergebnisses.</summary>
    public const string AlgorithmVersion = "own-order-decision-v1";

    /// <summary>
    /// Annahme (keine ESI-Angabe): zwischen zwei Preisänderungen derselben Order müssen
    /// mindestens 5 Minuten liegen. Über <see cref="OwnOrderDecisionInput.CooldownMinutesOverride"/>
    /// anpassbar, damit die Grenze testbar und konfigurierbar bleibt.
    /// </summary>
    public const double DefaultModifyCooldownMinutes = 5.0;

    /// <summary>Preis-Toleranz für „gleicher Preis" (wie im Orderbuch-Kontext).</summary>
    private const double PriceEqualityTolerance = 0.005;

    /// <summary>Toleranz für die Prüfung „Preis liegt auf einer Tick-Stufe".</summary>
    private const double TickTolerance = 1e-6;

    /// <summary>Marktschritt für einen Referenzpreis — dieselbe EVE-Regel wie in <see cref="SellDecisionEngine"/>.</summary>
    public static double DeriveTickSize(double referencePrice) => SellDecisionEngine.DeriveTickSize(referencePrice);

    /// <summary>Rundet einen Preis auf eine gültige Tick-Stufe AB (nie auf).</summary>
    public static double RoundDownToTick(double price, double tickSize) => SellDecisionEngine.RoundDownToTick(price, tickSize);

    /// <summary>
    /// Prüft, ob ein Preis auf einer gültigen Tick-Stufe liegt. Ein Preis zwischen zwei Stufen
    /// wäre handelbar nur über eine Verfälschung der Absicht und ist daher unzulässig.
    /// </summary>
    public static bool IsOnValidTick(double price, double tickSize)
    {
        if (!double.IsFinite(price) || !double.IsFinite(tickSize) || tickSize <= 0) return false;
        var steps = price / tickSize;
        return Math.Abs(steps - Math.Round(steps)) < TickTolerance;
    }

    /// <summary>
    /// Baut die Eingaben aus dem geladenen Orderbuch-Kontext (#31/#61): Sell-Seite als
    /// Konkurrenz am Ort, erreichbare Buy-Orders (Buy-Range) als Nachfrage. So nutzt die
    /// Entscheidung genau die Daten, die <see cref="Market.OrderIntelligenceService"/> schon
    /// berechnet — ohne erneuten ESI-Abruf und ohne UI-Automation.
    /// </summary>
    public static OwnOrderDecisionInput FromOrderBookContext(
        OrderBookContext context,
        double minutesSinceLastChange,
        CharacterSkills? skills = null,
        double? cooldownMinutesOverride = null,
        double? targetPriceOverride = null,
        double? tickSizeOverride = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new OwnOrderDecisionInput
        {
            OrderId = context.OwnOrderId,
            TypeName = context.TypeName,
            LocationId = context.OwnLocationId,
            LocationName = context.OwnLocationName,
            IsBuyOrder = context.OwnIsBuyOrder,
            CurrentPrice = context.OwnPrice,
            RemainingQuantity = context.OwnRemaining,
            MinutesSinceLastChange = minutesSinceLastChange,
            CooldownMinutesOverride = cooldownMinutesOverride,
            SameLocationSellQuotes = context.SellSide.Where(l => !l.IsOwn && !l.IsBuyOrder).ToList(),
            ReachableBuyOrders = context.BuySide.Where(l => !l.IsOwn && l.CanReachOwnLocation).ToList(),
            ForeignDataStatus = context.ForeignDataStatus,
            CostBasisPerUnit = context.CostBasisPerUnit,
            TargetPriceOverride = targetPriceOverride,
            TickSizeOverride = tickSizeOverride,
            Skills = skills
        };
    }

    /// <summary>
    /// Evaluiert die drei Optionen und empfiehlt genau eine. Wirft nur bei ungültiger
    /// Konfiguration des Aufrufers (Tick-Override, Zielpreis-Override, Cooldown oder Zeitangabe
    /// unbrauchbar), nie bei unvollständigen Marktdaten — die ergeben eine begründete
    /// „keine Empfehlung".
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Ein Override oder die Zeitangabe ist
    /// unbrauchbar; damit wäre jede Empfehlung erfunden.</exception>
    public static OwnOrderDecisionResult Evaluate(OwnOrderDecisionInput input, IFeeCalculatorService fees)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(fees);

        if (input.TickSizeOverride is { } tickOverride
            && (!double.IsFinite(tickOverride) || tickOverride <= 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(input),
                "Ungültiger Tick-Schritt (muss endlich und positiv sein) — ein Tick-Ziel ist damit nicht bestimmbar.");
        }

        if (input.TargetPriceOverride is { } targetOverride
            && (!double.IsFinite(targetOverride) || targetOverride <= 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(input),
                "Ungültiger Zielpreis (muss endlich und positiv sein) — eine Preisänderung ist damit nicht belegbar.");
        }

        var cooldown = input.CooldownMinutesOverride ?? DefaultModifyCooldownMinutes;
        if (!double.IsFinite(cooldown) || cooldown < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input), "Ungültiger Cooldown (muss endlich und nicht negativ sein).");
        }

        if (!double.IsFinite(input.MinutesSinceLastChange) || input.MinutesSinceLastChange < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input), "Ungültige Zeit seit der letzten Änderung (muss endlich und nicht negativ sein).");
        }

        // --- Pflichtdaten: fehlend ⇒ keine Empfehlung (kein erfundener Marktzustand) ---
        if (input.ForeignDataStatus == OrderBookDataStatus.Failed)
        {
            return NotActionable(
                "Fremd-Orderbuch nicht verfügbar (ESI-Abruf fehlgeschlagen) — Queue-Position, Konkurrenz " +
                "und Buy-Range sind nicht bewertbar. Keine Empfehlung.",
                input, cooldown);
        }

        if (input.RemainingQuantity <= 0)
        {
            return NotActionable(
                "Keine offene Restmenge an dieser Order — es gibt nichts zu ändern oder zu stornieren. Keine Empfehlung.",
                input, cooldown);
        }

        if (!double.IsFinite(input.CurrentPrice) || input.CurrentPrice <= 0)
        {
            return NotActionable(
                "Ungültiger aktueller Preis der eigenen Order — Mehrwert und Gebühren sind nicht belegbar. Keine Empfehlung.",
                input, cooldown);
        }

        var ownIsBuy = input.IsBuyOrder;

        // Konkurrenz am eigenen Ort: Sell-Seite für eine eigene Sell-Order (Käufer kaufen beim
        // billigsten Anbieter), erreichbare Buy-Seite für eine eigene Buy-Order (Verkäufer
        // verkaufen an den höchsten erreichbaren Käufer).
        var sellQuotes = input.SameLocationSellQuotes
            .Where(l => !l.IsOwn && !l.IsBuyOrder && l.VolumeRemain > 0)
            .OrderBy(l => l.Price)
            .ThenBy(l => l.Issued)
            .ToList();

        var reachableBuys = input.ReachableBuyOrders
            .Where(l => !l.IsOwn && l.IsBuyOrder && l.CanReachOwnLocation && l.VolumeRemain > 0)
            .OrderByDescending(l => l.Price)
            .ThenBy(l => l.Issued)
            .ToList();

        var competition = ownIsBuy ? reachableBuys : sellQuotes;

        var referencePrice = competition.Count > 0 ? competition[0].Price : input.CurrentPrice;
        var tick = input.TickSizeOverride ?? DeriveTickSize(referencePrice);
        var tolerance = Math.Max(tick / 2.0, PriceEqualityTolerance);

        // Queue jetzt: alles, was per Preis (und bei gleichem Preis per FIFO als ältere Order)
        // vor der eigenen Order bedient wird.
        var aheadNow = ownIsBuy
            ? competition.Where(q => q.Price >= input.CurrentPrice - tolerance).ToList()
            : competition.Where(q => q.Price <= input.CurrentPrice + tolerance).ToList();
        var behindNow = ownIsBuy
            ? competition.Where(q => q.Price < input.CurrentPrice - tolerance).ToList()
            : competition.Where(q => q.Price > input.CurrentPrice + tolerance).ToList();

        double? breakEven = null;
        if (!ownIsBuy && input.CostBasisPerUnit is { } costBasis
            && double.IsFinite(costBasis) && costBasis > 0)
        {
            breakEven = fees.CalculateBreakEvenSellPriceForStoredBasis(costBasis, input.Skills);
        }

        double? currentNetProceeds = ownIsBuy
            ? null
            : fees.CalculateSellProceeds(input.CurrentPrice, input.RemainingQuantity, input.Skills).NetAmount;

        var waitPlan = new OwnOrderWaitPlan
        {
            QueuePosition = aheadNow.Count + 1,
            CompetingQuantityAhead = aheadNow.Sum(q => q.VolumeRemain),
            CompetingQuantityBehind = behindNow.Sum(q => q.VolumeRemain),
            BestCompetingPrice = competition.Count > 0 ? competition[0].Price : null,
            IsBestAtOwnLocation = aheadNow.Count == 0,
            GapToBestCompetingPrice = competition.Count > 0
                ? Math.Abs(competition[0].Price - input.CurrentPrice)
                : null,
            ReachableDemandQuantity = reachableBuys.Sum(o => o.VolumeRemain),
            BestReachableBuyPrice = reachableBuys.Count > 0 ? reachableBuys[0].Price : null,
            CurrentNetProceeds = currentNetProceeds,
            BreakEvenPrice = breakEven,
            IsBelowBreakEven = breakEven is { } be && input.CurrentPrice < be - tolerance
        };

        // Ein Zielpreis ist nur mit Vergleichsquote ableitbar: eine Stufe besser als die beste
        // Konkurrenz (Sell: unterbieten, Buy: überbieten) — und immer auf gültigem Tick.
        double derivedTarget = 0;
        string? notDerivable = null;
        if (competition.Count == 0)
        {
            notDerivable = ownIsBuy
                ? "Keine erreichbare Buy-Order am Ort (keine Konkurrenz in der Buy-Range) — " +
                  "ein Tick-Ziel ist damit nicht ableitbar."
                : "Keine Konkurrenzquote am Ort — ein gültiges Tick-Ziel ist damit nicht ableitbar.";
        }
        else
        {
            derivedTarget = ownIsBuy
                ? RoundDownToTick(competition[0].Price + tick, tick)
                : RoundDownToTick(competition[0].Price - tick, tick);
            if (derivedTarget <= 0)
            {
                notDerivable = $"Die beste Konkurrenzquote {competition[0].Price:N2} ISK liegt unter einem " +
                               "Tick-Schritt — es gibt keinen positiven Zielpreis.";
            }
        }

        var target = input.TargetPriceOverride ?? derivedTarget;

        var modify = BuildPriceChangePlan(
            OwnOrderAction.Modify, input, fees, tick, tolerance, cooldown, breakEven, currentNetProceeds,
            ownIsBuy, competition, target, notDerivable);

        var cancelRecreate = BuildPriceChangePlan(
            OwnOrderAction.CancelRecreate, input, fees, tick, tolerance, cooldown, breakEven, currentNetProceeds,
            ownIsBuy, competition, target, notDerivable);

        var (action, reasonText) = Recommend(ownIsBuy, waitPlan, modify, cancelRecreate, input);

        return new OwnOrderDecisionResult
        {
            AlgorithmVersion = AlgorithmVersion,
            IsActionable = true,
            NotActionableReason = null,
            Wait = waitPlan,
            Modify = modify,
            CancelRecreate = cancelRecreate,
            RecommendedAction = action,
            RecommendationReason = reasonText,
            Evidence = BuildEvidence(input, ownIsBuy, waitPlan, modify, cancelRecreate, tick, cooldown),
            Assumptions = BuildAssumptions(input, cooldown, breakEven)
        };
    }

    // --- Eine Preisänderungs-Option (Modify bzw. Cancel-Recreate) ---

    private static OwnOrderPriceChangePlan BuildPriceChangePlan(
        string action,
        OwnOrderDecisionInput input,
        IFeeCalculatorService fees,
        double tick,
        double tolerance,
        double cooldown,
        double? breakEven,
        double? currentNetProceeds,
        bool ownIsBuy,
        IReadOnlyList<OrderBookLine> competition,
        double target,
        string? notDerivable)
    {
        var isModify = action == OwnOrderAction.Modify;

        // Gebühren: Modify über die EVE-Relist-Formel, Cancel-Recreate über die VOLLE Broker-Fee
        // der neuen Order (die Broker-Fee der stornierten Order wird nicht erstattet).
        var actionFee = isModify
            ? fees.CalculateOrderModifyFee(input.CurrentPrice, target, input.RemainingQuantity, input.Skills)
            : fees.CalculateOrderChangeCost(target, input.RemainingQuantity, input.Skills).BrokerFee;

        double? netProceedsAtTarget = null;
        double? benefit = null;
        double? netBenefit = null;

        if (!ownIsBuy)
        {
            netProceedsAtTarget = fees.CalculateSellProceeds(target, input.RemainingQuantity, input.Skills).NetAmount;
            benefit = netProceedsAtTarget - (currentNetProceeds ?? netProceedsAtTarget);
            netBenefit = benefit - actionFee;
        }

        var aheadAfter = ownIsBuy
            ? competition.Where(q => q.Price >= target - tolerance).ToList()
            : competition.Where(q => q.Price <= target + tolerance).ToList();

        var plan = new OwnOrderPriceChangePlan
        {
            Action = action,
            TargetPrice = target,
            TickSize = tick,
            IsOnValidTick = IsOnValidTick(target, tick),
            PriceChange = target - input.CurrentPrice,
            QueuePositionAfter = aheadAfter.Count + 1,
            CompetingQuantityAheadAfter = aheadAfter.Sum(q => q.VolumeRemain),
            IsBestAfter = aheadAfter.Count == 0,
            LosesTimePriority = true,
            ActionFee = actionFee,
            ExpectedBenefit = benefit,
            NetBenefit = netBenefit,
            NetProceedsAtTarget = netProceedsAtTarget,
            IsBelowBreakEven = breakEven is { } be && target < be - tolerance
        };

        // --- Zulässigkeit der Option (erster zutreffender Grund) ---
        string? ineligible = null;

        if (ownIsBuy)
        {
            ineligible = "Eigene Buy-Order: der monetäre Mehrwert eines höheren Kaufpreises ist ohne " +
                         "Weiterverkaufspreis nicht belegbar — deshalb keine Preisänderungs-Empfehlung.";
        }
        else if (notDerivable != null)
        {
            ineligible = notDerivable;
        }
        else if (!double.IsFinite(target) || target <= 0)
        {
            ineligible = "Zielpreis ist nicht endlich/positiv — eine Preisänderung ist damit nicht belegbar.";
        }
        else if (!plan.IsOnValidTick)
        {
            ineligible =
                $"Zielpreis {target:N2} ISK liegt auf keiner gültigen Tick-Stufe ({tick:0.######} ISK) — " +
                "die Order wäre so nicht handelbar.";
        }
        else if (plan.PriceChange <= tolerance)
        {
            ineligible =
                $"Zielpreis {target:N2} ISK verbessert den aktuellen Preis {input.CurrentPrice:N2} ISK " +
                "nicht um mindestens eine Tick-Stufe — kein belegbarer Mehrwert.";
        }
        else if (isModify && input.MinutesSinceLastChange < cooldown - TickTolerance)
        {
            ineligible =
                $"Cooldown noch nicht abgelaufen: Änderungen erst {cooldown:0.#} Minuten nach der letzten " +
                $"Änderung ({input.MinutesSinceLastChange:0.#} Minuten vergangen).";
        }
        else if (plan.IsBelowBreakEven)
        {
            ineligible =
                $"Zielpreis {target:N2} ISK liegt unter dem Break-even von {breakEven:N2} ISK — " +
                "ein Verkauf dort wäre ein belegter Verlust.";
        }

        return new OwnOrderPriceChangePlan
        {
            Action = plan.Action,
            IsEligible = ineligible == null,
            IneligibleReason = ineligible,
            TargetPrice = plan.TargetPrice,
            TickSize = plan.TickSize,
            IsOnValidTick = plan.IsOnValidTick,
            PriceChange = plan.PriceChange,
            QueuePositionAfter = plan.QueuePositionAfter,
            CompetingQuantityAheadAfter = plan.CompetingQuantityAheadAfter,
            IsBestAfter = plan.IsBestAfter,
            LosesTimePriority = plan.LosesTimePriority,
            ActionFee = plan.ActionFee,
            ExpectedBenefit = plan.ExpectedBenefit,
            NetBenefit = plan.NetBenefit,
            NetProceedsAtTarget = plan.NetProceedsAtTarget,
            IsBelowBreakEven = plan.IsBelowBreakEven
        };
    }

    // --- Empfehlung ---

    private static (string Action, string Reason) Recommend(
        bool ownIsBuy,
        OwnOrderWaitPlan wait,
        OwnOrderPriceChangePlan modify,
        OwnOrderPriceChangePlan cancelRecreate,
        OwnOrderDecisionInput input)
    {
        if (ownIsBuy)
        {
            return (OwnOrderAction.Wait,
                $"Wait: eigene Buy-Order — Queue-Position {wait.QueuePosition} " +
                $"({wait.CompetingQuantityAhead} Stück erreichbare Konkurrenz davor), " +
                $"Gebühren ausgewiesen (Modify {modify.ActionFee:N0} ISK, Cancel-Recreate " +
                $"{cancelRecreate.ActionFee:N0} ISK), aber der monetäre Mehrwert eines höheren " +
                "Kaufpreises ist ohne Weiterverkaufspreis nicht belegbar. Die Order bleibt unverändert.");
        }

        // Nur Optionen mit einem BELEGBAREN Mehrwert, der die Gebühr übersteigt: „Gebühr größer
        // als Mehrwert" ergibt ausdrücklich kein Modify.
        var candidates = new List<OwnOrderPriceChangePlan>();
        if (modify.IsEligible && modify.NetBenefit is > 0) candidates.Add(modify);
        if (cancelRecreate.IsEligible && cancelRecreate.NetBenefit is > 0) candidates.Add(cancelRecreate);

        if (candidates.Count == 0)
        {
            return (OwnOrderAction.Wait, BuildWaitReason(modify, cancelRecreate, wait, input, ownIsBuy));
        }

        // Bei gleichem Netto-Mehrwert gewinnt Modify: es behält die Order-ID und ist die
        // günstigere der beiden Änderungen.
        var winner = candidates
            .OrderByDescending(c => c.NetBenefit ?? double.NegativeInfinity)
            .ThenBy(c => c.Action == OwnOrderAction.Modify ? 0 : 1)
            .First();

        var runnerUp = candidates.FirstOrDefault(c => c.Action != winner.Action);
        var comparison = runnerUp == null
            ? "Die andere Änderungsoption ist nicht belegbar vorteilhaft."
            : $"Alternative {runnerUp.Action}: Netto {Money(runnerUp.NetBenefit)}.";

        return (winner.Action,
            $"Empfehlung {winner.Action}: Zielpreis {winner.TargetPrice:N2} ISK (Tick {winner.TickSize:0.##}) " +
            $"erwartet {Money(winner.ExpectedBenefit)} Mehrwert auf {input.RemainingQuantity} Stück und kostet " +
            $"{winner.ActionFee:N0} ISK Gebühr — Netto {Money(winner.NetBenefit)}. " +
            $"Queue-Position danach {winner.QueuePositionAfter} ({winner.CompetingQuantityAheadAfter} Stück davor), " +
            (winner.LosesTimePriority ? "Zeit-Priorität wird neu gesetzt. " : string.Empty) +
            $"{comparison} Es wird nichts ausgeführt.");
    }

    private static string BuildWaitReason(
        OwnOrderPriceChangePlan modify,
        OwnOrderPriceChangePlan cancelRecreate,
        OwnOrderWaitPlan wait,
        OwnOrderDecisionInput input,
        bool ownIsBuy)
    {
        var parts = new List<string>();

        if (modify.IsEligible)
        {
            parts.Add(
                $"Modify bringt {Money(modify.ExpectedBenefit)} erwarteten Mehrwert, kostet aber " +
                $"{modify.ActionFee:N0} ISK Relist-Gebühr (Netto {Money(modify.NetBenefit)}) — die Gebühr " +
                "übersteigt den Mehrwert, deshalb kein Modify.");
        }
        else
        {
            parts.Add($"Modify ist nicht zulässig: {modify.IneligibleReason}");
        }

        if (cancelRecreate.IsEligible)
        {
            parts.Add(
                $"Cancel-Recreate kostet die volle Broker-Fee von {cancelRecreate.ActionFee:N0} ISK ohne " +
                $"Relist-Rabatt und bringt netto {Money(cancelRecreate.NetBenefit)}.");
        }
        else
        {
            parts.Add($"Cancel-Recreate ist nicht zulässig: {cancelRecreate.IneligibleReason}");
        }

        var queueText = wait.IsBestAtOwnLocation
            ? $"Die eigene Order ist mit {input.CurrentPrice:N2} ISK bereits die beste am Ort (Position {wait.QueuePosition})."
            : $"Die eigene Order steht auf Position {wait.QueuePosition} ({wait.CompetingQuantityAhead} Stück Konkurrenz davor).";
        parts.Add(queueText);
        parts.Add("Damit bleibt die Order unverändert (Wait) — es wird nichts ausgeführt.");

        return $"Wait: {string.Join(" ", parts)}";
    }

    // --- Belege und Annahmen ---

    private static IReadOnlyList<string> BuildEvidence(
        OwnOrderDecisionInput input,
        bool ownIsBuy,
        OwnOrderWaitPlan wait,
        OwnOrderPriceChangePlan modify,
        OwnOrderPriceChangePlan cancelRecreate,
        double tick,
        double cooldown)
    {
        var evidence = new List<string>
        {
            $"Eigene {(ownIsBuy ? "Buy" : "Sell")}-Order {input.CurrentPrice:N2} ISK, offene Restmenge " +
            $"{input.RemainingQuantity} Stück am Ort {LocationLabel(input)}.",
            $"Queue: Position {wait.QueuePosition}, {wait.CompetingQuantityAhead} Stück Konkurrenz davor, " +
            $"{wait.CompetingQuantityBehind} Stück dahinter" +
            (wait.BestCompetingPrice is { } best ? $", beste Konkurrenzquote {best:N2} ISK." : ", keine Konkurrenzquote am Ort."),
            $"Buy-Range (#31): {wait.ReachableDemandQuantity} Stück erreichbare Nachfrage" +
            (wait.BestReachableBuyPrice is { } buy ? $", bester erreichbarer Kaufpreis {buy:N2} ISK." : ", keine erreichbare Buy-Order."),
            $"Tick {tick:0.######} ISK (EVE-Marktschritt aus dem Referenzpreis) — Zielpreis " +
            $"{modify.TargetPrice:N2} ISK, gültige Tick-Stufe: {(modify.IsOnValidTick ? "ja" : "nein")}.",
            $"Modify-Gebühr {modify.ActionFee:N0} ISK, erwarteter Mehrwert {Money(modify.ExpectedBenefit)}, " +
            $"Netto {Money(modify.NetBenefit)}; Zulässigkeit: {EligibilityText(modify)}.",
            $"Cancel-Recreate-Gebühr {cancelRecreate.ActionFee:N0} ISK (volle Broker-Fee der neuen Order), " +
            $"erwarteter Mehrwert {Money(cancelRecreate.ExpectedBenefit)}, Netto {Money(cancelRecreate.NetBenefit)}; " +
            $"Zulässigkeit: {EligibilityText(cancelRecreate)}.",
            $"Cooldown: {cooldown:0.#} Minuten, vergangen seit der letzten Änderung " +
            $"{input.MinutesSinceLastChange:0.#} Minuten."
        };

        if (!ownIsBuy && wait.CurrentNetProceeds is { } netNow)
        {
            evidence.Add($"Netto-Erlös der Restmenge beim aktuellen Preis: {netNow:N0} ISK" +
                (wait.BreakEvenPrice is { } be ? $"; Break-even {be:N2} ISK." : "; Cost Basis unbekannt, Break-even nicht bewertet."));
        }

        if (wait.BreakEvenPrice is { } breakEven && modify.IsBelowBreakEven)
        {
            evidence.Add($"Zielpreis liegt unter dem Break-even {breakEven:N2} ISK — die Änderung wäre ein Verlust.");
        }

        return evidence;
    }

    private static string EligibilityText(OwnOrderPriceChangePlan plan)
        => plan.IsEligible ? "zulässig" : plan.IneligibleReason ?? "nicht zulässig";

    private static IReadOnlyList<string> BuildAssumptions(
        OwnOrderDecisionInput input,
        double cooldownMinutes,
        double? breakEven)
    {
        var assumptions = new List<string>
        {
            "Es wird nichts ausgeführt: die Engine empfiehlt nur; keine ESI-Schreiborder, kein Storno, keine Neueinstellung.",
            "Mehrwert = zusätzlicher Netto-Erlös der offenen Restmenge durch den Zielpreis (vollständige " +
            "Füllung zum Zielpreis unterstellt); die tatsächliche Ausführung ist nicht garantiert.",
            "Modify-Fee = max(0, BR × (neuer Wert − alter Wert)) + (1 − RD) × BR × neuer Wert, Minimum 100 ISK; " +
            "BR = Broker-Rate, RD = Relist-Rabatt (Advanced Broker Relations) — IFeeCalculatorService.",
            "Cancel-Recreate kostet die VOLLE Broker-Fee der neuen Order (BR × neuer Orderwert); die Broker-Fee " +
            "der stornierten Order wird nicht erstattet.",
            $"Modify-Cooldown {cooldownMinutes:0.#} Minuten seit der letzten Änderung (EVE-Regel, über " +
            "CooldownMinutesOverride anpassbar).",
            "Beide Änderungen setzen die Zeit-Priorität (Issued/FIFO) neu; bei gleichem Preis gelten bestehende " +
            "Orders als älter und werden zuerst bedient.",
            $"Tick-Regel: {SellDecisionEngine.TickBelowThousand:0.##} ISK unter 1.000 ISK, " +
            $"{SellDecisionEngine.TickAtLeastThousand:0.##} ISK ab 1.000 ISK.",
            "Eine reine Queue-Verbesserung durch Preissenkung wird nicht empfohlen, weil sie den Erlös je Stück senkt."
        };

        assumptions.Add(breakEven is { } be
            ? $"Cost Basis bekannt: Break-even {be:N2} ISK (Erwerbskosten sind in der Cost Basis bereits genau einmal enthalten)."
            : "Cost Basis unbekannt: Break-even und Verlustgrenze werden nicht bewertet (die Gebührenrechnung bleibt belegbar).");

        return assumptions;
    }

    private static OwnOrderDecisionResult NotActionable(
        string reason, OwnOrderDecisionInput input, double cooldownMinutes)
        => new()
        {
            AlgorithmVersion = AlgorithmVersion,
            IsActionable = false,
            NotActionableReason = reason,
            Wait = null,
            Modify = null,
            CancelRecreate = null,
            RecommendedAction = OwnOrderAction.Wait,
            RecommendationReason = reason,
            Evidence = new[] { reason },
            Assumptions = BuildAssumptions(input, cooldownMinutes, null)
        };

    private static string Money(double? value)
        => value is { } v ? $"{v:N0} ISK" : "nicht belegbar";

    private static string LocationLabel(OwnOrderDecisionInput input)
        => !string.IsNullOrWhiteSpace(input.LocationName) ? input.LocationName! : $"Loc {input.LocationId}";
}
