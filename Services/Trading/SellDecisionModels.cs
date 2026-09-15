using WALLEve.Models.Esi.Character;
using WALLEve.Services.Market.Interfaces;

namespace WALLEve.Services.Trading;

/// <summary>
/// Zulässige Verkaufsentscheidungen einer ortsgenauen Bestandsmenge (Issue #61).
/// Konstanten statt Strings, damit Erklärung und Auswertung nie auseinanderlaufen.
/// </summary>
public static class SellDecisionAction
{
    /// <summary>Sofort gegen vorhandene Buy-Orders verkaufen (bestätigte Ausführung).</summary>
    public const string SellNow = "sell-now";

    /// <summary>Neue Sell-Order auf einem gültigen Tick einstellen (Ausführung nicht garantiert).</summary>
    public const string List = "list";

    /// <summary>Nicht verkaufen (keine belegbar vorteilhafte Option).</summary>
    public const string Hold = "hold";
}

/// <summary>
/// Eingaben EINER ortsgenauen Verkaufsentscheidung (Issue #61): Besitzmenge am Ort,
/// Cost Basis und das fremde Orderbuch dieser Region. Reine Daten — die Engine
/// entscheidet, was davon Pflicht ist und was eine Empfehlung blockiert.
/// </summary>
public sealed class SellDecisionInput
{
    public int TypeId { get; init; }

    public string? TypeName { get; init; }

    /// <summary>Ort (Station/Struktur), an dem die Bestandsmenge liegt und gehandelt würde.</summary>
    public long LocationId { get; init; }

    public string? LocationName { get; init; }

    /// <summary>
    /// Besitzmenge an diesem Ort. Begrenzt jede Verkaufsmenge nach oben: Bestände an
    /// anderen Orten sind hier nicht verkaufbar (kein stiller Transfer, kein Region-Trading).
    /// </summary>
    public int OwnedQuantityAtLocation { get; init; }

    /// <summary>
    /// Cost Basis pro Einheit (enthält die Erwerbskosten bereits genau einmal, #4/#46).
    /// <c>null</c> oder ungültig ⇒ keine Empfehlung, weil Break-even und Gewinn nicht belegbar sind.
    /// </summary>
    public double? CostBasisPerUnit { get; init; }

    /// <summary>
    /// Fremde Buy-Orders der Region (Kontext aus dem Orderbuch). Ausführbar ist nur, wessen
    /// Range den eigenen Ort erreicht (<see cref="OrderBookLine.CanReachOwnLocation"/>) —
    /// Orders an anderen Orten zählen weder als Nachfrage noch als ausführbare Menge.
    /// </summary>
    public IReadOnlyList<OrderBookLine> BuySide { get; init; } = Array.Empty<OrderBookLine>();

    /// <summary>
    /// Fremde Sell-Orders am eigenen Ort (Konkurrenz). Sie bestimmen die beste Quote und
    /// damit das Tick-/Queue-Ziel einer neuen Sell-Order.
    /// </summary>
    public IReadOnlyList<OrderBookLine> SellSide { get; init; } = Array.Empty<OrderBookLine>();

    /// <summary>
    /// Datenqualität der fremden Orders. <see cref="OrderBookDataStatus.Failed"/> ist kein
    /// leeres Orderbuch und blockiert jede Empfehlung (kein erfundener Marktzustand).
    /// </summary>
    public OrderBookDataStatus ForeignDataStatus { get; init; } = OrderBookDataStatus.Ok;

    /// <summary>Skills für die Gebührenberechnung; fehlend ⇒ konservative Schätzung des Rechners.</summary>
    public CharacterSkills? Skills { get; init; }

    /// <summary>
    /// Expliziter Tick-Schritt für Tests/abweichende Märkte. <c>null</c> = EVE-Regel
    /// (<see cref="SellDecisionEngine.DeriveTickSize"/>); ungültige Werte blockieren die Empfehlung.
    /// </summary>
    public double? TickSizeOverride { get; init; }
}

/// <summary>Ergebnis der sofortigen Ausführung gegen vorhandene Buy-Orders (Issue #61).</summary>
public sealed class SellNowPlan
{
    /// <summary>false, wenn keine ausführbare Nachfrage am Ort vorliegt.</summary>
    public bool IsEvaluable { get; init; }

    public string? NotEvaluableReason { get; init; }

    /// <summary>Ausgeführte Menge = min(Besitz am Ort, erreichbare Buy-Tiefe).</summary>
    public int Quantity { get; init; }

    /// <summary>Besitzmenge am Ort, die die Nachfrage nicht abdeckt (Rest bleibt liegen).</summary>
    public int UnfilledQuantity { get; init; }

    public bool IsPartialFill { get; init; }

    /// <summary>Bester erreichbarer Buy-Preis (höchster Käufer) — erste ausgeführte Stufe.</summary>
    public double BestPrice { get; init; }

    /// <summary>Mengengewichteter Durchschnittspreis der ausgeführten Menge.</summary>
    public double AveragePrice { get; init; }

    /// <summary>Anzahl benutzter Preisstufen der Buy-Tiefe (mehrstufige Ausführung).</summary>
    public int LevelsUsed { get; init; }

    public double GrossAmount { get; init; }

    public double BrokerFee { get; init; }

    public double SalesTax { get; init; }

    /// <summary>Erlös nach Gebühren — Gebühren genau einmal je ausgeführter Menge.</summary>
    public double NetAmount { get; init; }

    public double EffectiveFeeRatePercent { get; init; }

    public double BreakEvenPrice { get; init; }

    /// <summary>Netto-Gewinn gegen die Cost Basis; <c>null</c> = nicht berechenbar.</summary>
    public double? NetProfit { get; init; }

    public double? RoiPercent { get; init; }

    public bool IsProfitable => NetProfit is > 0;
}

/// <summary>Ergebnis der neuen Sell-Order auf gültigem Tick-/Queue-Ziel (Issue #61).</summary>
public sealed class SellListPlan
{
    /// <summary>false, wenn keine Vergleichsquote am Ort (oder kein gültiges Tick-Ziel) existiert.</summary>
    public bool IsEvaluable { get; init; }

    public string? NotEvaluableReason { get; init; }

    /// <summary>Benutzter Tick-Schritt (ISK) — EVE-Regel oder expliziter Override.</summary>
    public double TickSize { get; init; }

    /// <summary>Günstigste fremde Sell-Quote am eigenen Ort (Referenz für den Unterbietungsschritt).</summary>
    public double BestCompetingSellPrice { get; init; }

    /// <summary>Gültiger Tick-Zielpreis = eine Tick-Stufe unter der besten Quote.</summary>
    public double TargetPrice { get; init; }

    /// <summary>Preisabstand zur besten Quote (≥ eine Tick-Stufe).</summary>
    public double UndercutAmount { get; init; }

    /// <summary>Listbare Menge (= Besitz am Ort; eine Order kann den gesamten Bestand listen).</summary>
    public int Quantity { get; init; }

    /// <summary>Menge, die in der Verkaufsschlange vor der neuen Order steht (billigeres Angebot).</summary>
    public int CheaperQuantityAhead { get; init; }

    /// <summary>
    /// Menge der Konkurrenz, die erst HINTER der neuen Order bedient wird (teureres Angebot).
    /// Das ist die Menge, die den Zielpreis nicht unterbietet.
    /// </summary>
    public int CompetingQuantityBehind { get; init; }

    /// <summary>Queue-Ziel: Position der neuen Order am Ort (1 = vorne).</summary>
    public int QueuePosition { get; init; }

    /// <summary>Wäre die neue Order der günstigste Anbieter am Ort?</summary>
    public bool WouldBeBestAtLocation { get; init; }

    public double GrossAmount { get; init; }

    public double BrokerFee { get; init; }

    public double SalesTax { get; init; }

    public double NetAmount { get; init; }

    public double EffectiveFeeRatePercent { get; init; }

    public double BreakEvenPrice { get; init; }

    /// <summary>Netto-Gewinn gegen die Cost Basis, wenn die Order vollständig gefüllt würde.</summary>
    public double? NetProfit { get; init; }

    public double? RoiPercent { get; init; }

    /// <summary>Zielpreis unter dem Break-even — ein Verkauf dort wäre ein Verlust.</summary>
    public bool IsBelowBreakEven { get; init; }

    public bool IsProfitable => NetProfit is > 0;
}

/// <summary>Ergebnis der Option „nicht verkaufen“ (Issue #61).</summary>
public sealed class SellHoldPlan
{
    /// <summary>Bestand, der liegen bleibt.</summary>
    public int Quantity { get; init; }

    /// <summary>Grund, warum Nicht-Verkaufen hier belegt ist.</summary>
    public string Reason { get; init; } = string.Empty;
}

/// <summary>
/// Gesamtergebnis einer ortsgenauen Verkaufsentscheidung (Issue #61): die drei Optionen
/// Sell-now, List und Hold mit getrennten Zahlen und EINER begründeten Empfehlung.
/// Fehlende Pflichtdaten ergeben keine Empfehlung — niemals erfundene Mengen, Gebühren
/// oder Preise.
/// </summary>
public sealed class SellDecisionResult
{
    public string AlgorithmVersion { get; init; } = SellDecisionEngine.AlgorithmVersion;

    /// <summary>false = mindestens eine Pflichteingabe fehlt/ist ungültig; keine Empfehlung.</summary>
    public bool IsActionable { get; init; }

    public string? NotActionableReason { get; init; }

    public SellNowPlan? SellNow { get; init; }

    public SellListPlan? List { get; init; }

    public SellHoldPlan Hold { get; init; } = new();

    /// <summary>Empfohlene Option (Werte aus <see cref="SellDecisionAction"/>).</summary>
    public string RecommendedAction { get; init; } = SellDecisionAction.Hold;

    public string RecommendationReason { get; init; } = string.Empty;

    /// <summary>Belege je Empfehlungsteil: Menge, Tiefe, Gebühren (einmal), Tick, Break-even.</summary>
    public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();
}
