using WALLEve.Models.Database;

namespace WALLEve.Services.Market.Interfaces;

/// <summary>
/// Service für AI-gestützte Market-Analyse
/// Nutzt Ollama für Opportunity Detection
/// </summary>
public interface IMarketAnalysisService
{
    /// <summary>
    /// Analysiert Market Snapshots und findet Trading Opportunities
    /// (erzeugt keine Duplikate, räumt abgelaufene auf)
    /// </summary>
    Task<List<TradingOpportunity>> AnalyzeMarketDataAsync();

    /// <summary>
    /// Liest aktive Trading Opportunities (kein Schreiben) — für reine Anzeige
    /// </summary>
    Task<List<TradingOpportunity>> GetActiveOpportunitiesAsync();

    /// <summary>
    /// Testet Ollama-Verbindung mit einfachem Prompt
    /// </summary>
    Task<string> TestOllamaConnectionAsync();
}
