using WALLEve.Models.Database;

namespace WALLEve.Services.Trading.Interfaces;

/// <summary>
/// Station-Trade-Analyse (Issue #71): erzeugt aus dem Regions-Cache und der
/// Markthistorie StationTrade-Kandidaten (kumulative Tiefe, historische Mengen,
/// Ausreißer, Kapital, Gebühren) und persistiert ausführbare Opportunities +
/// unveränderliche Verträge. Analysen ohne belastbare Chance (manipulierte
/// Top-Order, illiquider Spread, fehlende History, negative Nettomarge)
/// erzeugen keine Opportunity — bestehende werden invalidiert.
/// </summary>
public interface IStationTradeAnalysisService
{
    /// <summary>
    /// Führt die Station-Trade-Analyse für den authentifizierten Charakter aus
    /// und gibt die aktiven Trading-Opportunities zurück (order nach Score absteigend).
    /// </summary>
    Task<List<TradingOpportunity>> AnalyzeStationTradesAsync(CancellationToken ct = default);
}