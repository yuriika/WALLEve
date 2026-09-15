namespace WALLEve.Models.Trading;

/// <summary>
/// Explizite Annahmen der Rang-Bewertung (Issue #54): Szenario-Faktoren für
/// die geschätzte Füllzeit (konservativ/realistisch/optimistisch) und die
/// Gewichte je Dimension. Die Konstanten sind Teil des Algorithmus
/// (Version „trade-ranking-v1") — sie dokumentieren die Szenarien, ohne
/// eine exakte Füllzeit zu behaupten.
/// </summary>
/// <remarks>
/// Konservativ = längere Füllzeit (Faktor &gt; 1), Optimistisch = kürzere
/// Füllzeit (Faktor &lt; 1). Die Faktoren werden auf die als Eingabe
/// gelieferte Basis-Füllzeit angewendet; daraus folgt je Szenario eine
/// andere geschätzte ISK/Stunde, nie eine exakte Angabe.
/// </remarks>
public sealed record TradeRankingAssumptions(
    double ConservativeFillTimeMultiplier,
    double RealisticFillTimeMultiplier,
    double OptimisticFillTimeMultiplier,
    double NetProfitWeight,
    double RoiWeight,
    double CapitalBindingWeight,
    double LiquidityWeight,
    double RiskWeight,
    double IskPerHourWeight)
{
    /// <summary>Standard-Annahmen von „trade-ranking-v1".</summary>
    public static TradeRankingAssumptions Default { get; } = new(
        ConservativeFillTimeMultiplier: 1.5,
        RealisticFillTimeMultiplier: 1.0,
        OptimisticFillTimeMultiplier: 0.6,
        NetProfitWeight: 0.25,
        RoiWeight: 0.25,
        CapitalBindingWeight: 0.10,
        LiquidityWeight: 0.15,
        RiskWeight: 0.10,
        IskPerHourWeight: 0.15);
}