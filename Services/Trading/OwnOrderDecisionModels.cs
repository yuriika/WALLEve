using WALLEve.Models.Esi.Character;
using WALLEve.Services.Market.Interfaces;

namespace WALLEve.Services.Trading;

/// <summary>
/// Zulässige Aktionen für eine EIGENE Order (Issue #64). Konstanten statt Strings,
/// damit Erklärung, Empfehlung und Auswertung nie auseinanderlaufen.
/// </summary>
public static class OwnOrderAction
{
    /// <summary>Order unverändert lassen — keine Aktion, keine Gebühr.</summary>
    public const string Wait = "wait";

    /// <summary>
    /// Preis der bestehenden Order ändern (Relist-/Modify-Gebühr, Order-ID und Einstellung bleiben,
    /// die Zeit-Priorität in der Schlange geht verloren).
    /// </summary>
    public const string Modify = "modify";

    /// <summary>
    /// Order stornieren und neu einstellen: Stornieren ist kostenlos, die neue Order kostet die
    /// VOLLE Broker-Fee (ohne Relist-Rabatt), und die Zeit-Priorität (Issued/FIFO) ist verloren.
    /// </summary>
    public const string CancelRecreate = "cancel-recreate";
}

/// <summary>
/// Eingaben einer Entscheidung über EINE eigene Order (Issue #64): aktueller Preis, Restmenge,
/// Zeit seit der letzten Änderung sowie die Konkurrenz am eigenen Ort und die erreichbare
/// Nachfrage (Buy-Range, #31). Reine Daten — die Engine entscheidet, was Pflicht ist.
/// </summary>
public sealed class OwnOrderDecisionInput
{
    public long OrderId { get; init; }

    public string? TypeName { get; init; }

    public long LocationId { get; init; }

    public string? LocationName { get; init; }

    /// <summary>Seite der eigenen Order. Buy-Orders werden berichtet, aber nicht monetär bewertet.</summary>
    public bool IsBuyOrder { get; init; }

    /// <summary>Aktueller Preis pro Einheit der eigenen Order.</summary>
    public double CurrentPrice { get; init; }

    /// <summary>Noch offene Menge der eigenen Order.</summary>
    public int RemainingQuantity { get; init; }

    /// <summary>Ursprünglich eingestellte Menge (nur für Belege „x von y").</summary>
    public int? OriginalQuantity { get; init; }

    /// <summary>
    /// Minuten seit dem Einstellen bzw. der letzten Preisänderung dieser Order. Bewusst als Zahl
    /// statt Zeitstempel: die Engine liest keine Uhr und ist damit deterministisch testbar.
    /// </summary>
    public double MinutesSinceLastChange { get; init; }

    /// <summary>Expliziter Cooldown für Tests/abweichende Regeln; <c>null</c> = <see cref="OwnOrderDecisionEngine.DefaultModifyCooldownMinutes"/>.</summary>
    public double? CooldownMinutesOverride { get; init; }

    /// <summary>
    /// Fremde Sell-Orders AM EIGENEN ORT (Konkurrenz um dieselben Käufer). Aufrufer übergibt sie
    /// ortsgefiltert; die Engine filtert zusätzlich eigene/billige Orders und Nullmengen heraus.
    /// </summary>
    public IReadOnlyList<OrderBookLine> SameLocationSellQuotes { get; init; } = Array.Empty<OrderBookLine>();

    /// <summary>
    /// Fremde Buy-Orders, deren Range den eigenen Ort erreicht (<see cref="OrderBookLine.CanReachOwnLocation"/>).
    /// Das ist die Buy-Range: nur erreichbare Orders sind Nachfrage bzw. Konkurrenz (#31).
    /// </summary>
    public IReadOnlyList<OrderBookLine> ReachableBuyOrders { get; init; } = Array.Empty<OrderBookLine>();

    /// <summary>
    /// Datenqualität der fremden Orders. <see cref="OrderBookDataStatus.Failed"/> ist kein leeres
    /// Orderbuch und ergibt keine Empfehlung (kein erfundener Marktzustand).
    /// </summary>
    public OrderBookDataStatus ForeignDataStatus { get; init; } = OrderBookDataStatus.Ok;

    /// <summary>
    /// Cost Basis pro Einheit, falls bekannt. <c>null</c> blockiert die Empfehlung NICHT — es
    /// entfällt dann lediglich die Break-even-/Gewinnbewertung (die Gebührenrechnung bleibt belegbar).
    /// </summary>
    public double? CostBasisPerUnit { get; init; }

    /// <summary>
    /// Expliziter Zielpreis für Tests/UI. <c>null</c> = Ableitung als Unterbietung der besten
    /// Konkurrenzquote um genau einen gültigen Tick.
    /// </summary>
    public double? TargetPriceOverride { get; init; }

    /// <summary>Expliziter Tick-Schritt; <c>null</c> = EVE-Regel (<see cref="OwnOrderDecisionEngine.DeriveTickSize"/>).</summary>
    public double? TickSizeOverride { get; init; }

    /// <summary>Skills für die Gebührenberechnung; fehlend ⇒ konservative Schätzung des Rechners.</summary>
    public CharacterSkills? Skills { get; init; }
}

/// <summary>Option „unverändert lassen" (Issue #64): Queue-Position und aktuelle Werte ohne Aktion.</summary>
public sealed class OwnOrderWaitPlan
{
    /// <summary>Position der eigenen Order in der eigenen Schlange (1 = vorne).</summary>
    public int QueuePosition { get; init; }

    /// <summary>Menge der Konkurrenz, die vor der eigenen Order bedient wird.</summary>
    public int CompetingQuantityAhead { get; init; }

    /// <summary>Menge der Konkurrenz hinter der eigenen Order.</summary>
    public int CompetingQuantityBehind { get; init; }

    /// <summary>Beste Konkurrenzquote am Ort (<c>null</c> = keine Konkurrenz).</summary>
    public double? BestCompetingPrice { get; init; }

    /// <summary>Ist die eigene Order aktuell die beste am Ort?</summary>
    public bool IsBestAtOwnLocation { get; init; }

    /// <summary>Preisabstand zur besten Konkurrenzquote (positiv = eigene Order ist schlechter platziert).</summary>
    public double? GapToBestCompetingPrice { get; init; }

    /// <summary>Erreichbare Nachfrage (Buy-Range): Menge, die den eigenen Ort erreicht.</summary>
    public int ReachableDemandQuantity { get; init; }

    /// <summary>Bester erreichbarer Kaufpreis (Buy-Range), <c>null</c> = keine erreichbare Nachfrage.</summary>
    public double? BestReachableBuyPrice { get; init; }

    /// <summary>Netto-Erlös der Restmenge beim aktuellen Preis (nur Sell-Orders).</summary>
    public double? CurrentNetProceeds { get; init; }

    /// <summary>Break-even-Preis (nur mit bekannter Cost Basis).</summary>
    public double? BreakEvenPrice { get; init; }

    /// <summary>Aktueller Preis liegt unter dem Break-even — die Order stünde im Verlust.</summary>
    public bool IsBelowBreakEven { get; init; }
}

/// <summary>
/// Eine Preisänderungs-Option (Modify oder Cancel-Recreate, Issue #64) mit getrennten Zahlen:
/// Zielpreis, Aktionsgebühr, erwarteter Mehrwert und Netto-Mehrwert nach Gebühr.
/// </summary>
public sealed class OwnOrderPriceChangePlan
{
    /// <summary>Werte aus <see cref="OwnOrderAction"/>.</summary>
    public string Action { get; init; } = OwnOrderAction.Wait;

    /// <summary>false = diese Option ist nicht zulässig/belegbar; Grund in <see cref="IneligibleReason"/>.</summary>
    public bool IsEligible { get; init; }

    public string? IneligibleReason { get; init; }

    /// <summary>Angestrebter Preis pro Einheit (bei fehlender Konkurrenz 0 = nicht ableitbar).</summary>
    public double TargetPrice { get; init; }

    /// <summary>Benutzter Tick-Schritt (ISK) — EVE-Regel oder expliziter Override.</summary>
    public double TickSize { get; init; }

    /// <summary>Zielpreis liegt auf einer gültigen Tick-Stufe (nicht zwischen zwei handelbaren Stufen).</summary>
    public bool IsOnValidTick { get; init; }

    /// <summary>Preisänderung pro Einheit gegenüber dem aktuellen Preis.</summary>
    public double PriceChange { get; init; }

    /// <summary>Queue-Position nach der Änderung (Preis-Einsortierung; FIFO-Neubewertung siehe <see cref="LosesTimePriority"/>).</summary>
    public int QueuePositionAfter { get; init; }

    /// <summary>Konkurrenzmenge, die nach der Änderung vor der eigenen Order steht.</summary>
    public int CompetingQuantityAheadAfter { get; init; }

    /// <summary>Wäre die eigene Order nach der Änderung die beste am Ort?</summary>
    public bool IsBestAfter { get; init; }

    /// <summary>Diese Option verliert die Zeit-Priorität (Issued/FIFO) der bestehenden Order.</summary>
    public bool LosesTimePriority { get; init; }

    /// <summary>Aktionsgebühr (Modify-Fee bzw. volle Broker-Fee der neuen Order), in ISK.</summary>
    public double ActionFee { get; init; }

    /// <summary>
    /// Erwarteter Mehrwert: zusätzlicher Netto-Erlös der Restmenge durch den Zielpreis.
    /// <c>null</c> = nicht belegbar (z. B. eigene Buy-Order: höherer Kaufpreis ist kein Erlös).
    /// </summary>
    public double? ExpectedBenefit { get; init; }

    /// <summary>Mehrwert nach Aktionsgebühr; <c>null</c> = nicht belegbar. Positiv = Änderung lohnt sich.</summary>
    public double? NetBenefit { get; init; }

    /// <summary>Netto-Erlös der Restmenge beim Zielpreis (nur Sell-Orders).</summary>
    public double? NetProceedsAtTarget { get; init; }

    /// <summary>Zielpreis liegt unter dem Break-even (nur mit bekannter Cost Basis beurteilbar).</summary>
    public bool IsBelowBreakEven { get; init; }
}

/// <summary>
/// Gesamtergebnis einer Entscheidung über eine eigene Order (Issue #64): die Optionen
/// „unverändert lassen", „Modify" und „Cancel-Recreate" mit getrennten Zahlen und EINE
/// begründete Empfehlung samt Formel und Annahmen. Es wird nie eine Order ausgeführt.
/// </summary>
public sealed class OwnOrderDecisionResult
{
    public string AlgorithmVersion { get; init; } = OwnOrderDecisionEngine.AlgorithmVersion;

    /// <summary>false = Pflichtdaten fehlen/unbrauchbar; keine Empfehlung.</summary>
    public bool IsActionable { get; init; }

    public string? NotActionableReason { get; init; }

    public OwnOrderWaitPlan? Wait { get; init; }

    public OwnOrderPriceChangePlan? Modify { get; init; }

    public OwnOrderPriceChangePlan? CancelRecreate { get; init; }

    /// <summary>Empfohlene Option (Werte aus <see cref="OwnOrderAction"/>).</summary>
    public string RecommendedAction { get; init; } = OwnOrderAction.Wait;

    public string RecommendationReason { get; init; } = string.Empty;

    /// <summary>Belege je Empfehlungsteil: Queue, Buy-Range, Tick, Gebühren, Mehrwert.</summary>
    public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();

    /// <summary>Formel und Annahmen der Rechnung (Roadmap §3.1: „display the formula and assumptions").</summary>
    public IReadOnlyList<string> Assumptions { get; init; } = Array.Empty<string>();
}
