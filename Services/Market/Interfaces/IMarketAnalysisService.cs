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
    /// </summary>
    Task<List<TradingOpportunity>> AnalyzeMarketDataAsync();

    /// <summary>
    /// Testet Ollama-Verbindung mit einfachem Prompt
    /// </summary>
    Task<string> TestOllamaConnectionAsync();
}
