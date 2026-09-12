using WALLEve.Models.Database;

namespace WALLEve.Services.Market.Interfaces;

/// <summary>
/// Service für die deterministische Markt-Analyse (Heuristik, kein LLM).
/// Analysiert den BESTAND des Charakters und findet Trading Opportunities,
/// ohne dass ein lokaler LLM-Server (Ollama) erreichbar oder konfiguriert sein muss.
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
    Task<List<TradingOpportunity>> GetActiveOpportunitiesAsync(int? characterId = null);
}
