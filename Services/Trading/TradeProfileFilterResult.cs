namespace WALLEve.Services.Trading;

/// <summary>
/// Ergebnis der harten Filterpipeline (Issue #44): bestanden oder abgelehnt
/// mit allen individuellen Ablehnungsbegründen in Pipeline-Reihenfolge.
/// Ein abgelehnter Kandidat verlässt die Pipeline und wird von keiner
/// späteren Bewertung/Gewichtung wieder aufgenommen.
/// </summary>
public sealed record TradeProfileFilterResult(int TradingOpportunityId, bool Passed, IReadOnlyList<string> RejectionReasons)
{
    public static TradeProfileFilterResult Pass(int tradingOpportunityId) => new(tradingOpportunityId, true, Array.Empty<string>());
}