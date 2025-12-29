using Microsoft.EntityFrameworkCore;
using WALLEve.Data;
using WALLEve.Models.Database;
using WALLEve.Services.AI.Interfaces;
using WALLEve.Services.Market.Interfaces;

namespace WALLEve.Services.Market;

/// <summary>
/// Market Analysis Service mit AI Integration
/// Nutzt Ollama für intelligente Trading Opportunity Detection
/// </summary>
public class MarketAnalysisService : IMarketAnalysisService
{
    private readonly IOllamaService _ollama;
    private readonly WalletDbContext _dbContext;
    private readonly ILogger<MarketAnalysisService> _logger;

    public MarketAnalysisService(
        IOllamaService ollama,
        WalletDbContext dbContext,
        ILogger<MarketAnalysisService> logger)
    {
        _ollama = ollama;
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Testet die Ollama-Verbindung mit einem EVE-spezifischen Prompt
    /// </summary>
    /// <returns>Formatierte Test-Ergebnisse mit verfügbaren Modellen und Test-Response</returns>
    public async Task<string> TestOllamaConnectionAsync()
    {
        try
        {
            _logger.LogInformation("Testing Ollama connection...");

            // Check if Ollama is available
            var isAvailable = await _ollama.IsAvailableAsync();
            if (!isAvailable)
            {
                _logger.LogWarning("Ollama is not available at configured endpoint");
                return "ERROR: Ollama not available. Make sure Ollama is running on localhost:11434";
            }

            // Get available models
            var models = await _ollama.GetAvailableModelsAsync();
            if (models == null || !models.Any())
            {
                _logger.LogWarning("No Ollama models available");
                return "ERROR: No Ollama models found. Run 'ollama pull llama3.1:8b' to download a model.";
            }

            _logger.LogInformation("Ollama is available with {Count} models: {Models}",
                models.Count, string.Join(", ", models));

            // Test simple prompt
            var testPrompt = "Explain arbitrage trading in EVE Online in exactly one sentence.";
            var response = await _ollama.GenerateAsync(testPrompt);

            _logger.LogInformation("Ollama test successful. Response length: {Length} characters", response.Length);

            return $"✅ Ollama Connection Successful!\n\nAvailable Models: {string.Join(", ", models)}\n\nTest Response:\n{response}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error testing Ollama connection");
            return $"❌ ERROR: {ex.Message}\n\nMake sure Ollama is installed and running:\n" +
                   "1. Install: curl -fsSL https://ollama.com/install.sh | sh\n" +
                   "2. Pull model: ollama pull llama3.1:8b\n" +
                   "3. Verify: ollama list";
        }
    }

    /// <summary>
    /// Analysiert Market Snapshots und findet Trading Opportunities
    /// Nutzt heuristische Spread-Analyse (>3%) und erstellt Opportunities mit Confidence-Scoring
    /// </summary>
    /// <returns>Liste von Trading Opportunities, sortiert nach Avg Spread</returns>
    public async Task<List<TradingOpportunity>> AnalyzeMarketDataAsync()
    {
        try
        {
            _logger.LogInformation("Starting AI-powered market analysis...");

            // Get recent market snapshots (last hour)
            var recentSnapshots = await _dbContext.MarketSnapshots
                .Where(s => s.Timestamp > DateTime.UtcNow.AddHours(-1))
                .OrderByDescending(s => s.Timestamp)
                .Take(100)
                .ToListAsync();

            if (!recentSnapshots.Any())
            {
                _logger.LogWarning("No recent market snapshots available for analysis");
                return new List<TradingOpportunity>();
            }

            _logger.LogInformation("Analyzing {Count} market snapshots with AI", recentSnapshots.Count);

            // Group by TypeId to find items with good spread
            var goodSpreads = recentSnapshots
                .Where(s => s.Spread.HasValue && s.Spread.Value > 3.0) // Min 3% spread
                .GroupBy(s => s.TypeId)
                .Select(g => new
                {
                    TypeId = g.Key,
                    AvgSpread = g.Average(s => s.Spread ?? 0),
                    MaxSpread = g.Max(s => s.Spread ?? 0),
                    BestBuy = g.Max(s => s.BestBuyPrice ?? 0),
                    BestSell = g.Min(s => s.BestSellPrice ?? 0),
                    Regions = g.Select(s => s.RegionId).Distinct().ToList(),
                    // Get snapshot with best spread for location info
                    BestSnapshot = g.OrderByDescending(s => s.Spread ?? 0).First()
                })
                .OrderByDescending(x => x.AvgSpread)
                .Take(10)
                .ToList();

            var opportunities = new List<TradingOpportunity>();

            foreach (var item in goodSpreads)
            {
                // Create basic opportunity (without AI for now - we'll add AI analysis later)
                var opportunity = new TradingOpportunity
                {
                    TypeId = item.TypeId,
                    OpportunityType = "station_trading",
                    BuyPrice = item.BestBuy,
                    SellPrice = item.BestSell,
                    BuyLocationId = item.BestSnapshot.BestBuyLocationId,
                    SellLocationId = item.BestSnapshot.BestSellLocationId,
                    BuySystemId = item.BestSnapshot.BestBuySystemId,
                    SellSystemId = item.BestSnapshot.BestSellSystemId,
                    EstimatedProfit = (item.BestSell - item.BestBuy) * 0.95, // After fees
                    RequiredCapital = item.BestSell,
                    Confidence = Math.Min(95, 60 + (item.AvgSpread * 2)), // Simple confidence scoring
                    AIModel = "heuristic", // Placeholder
                    Reasoning = $"Spread of {item.AvgSpread:F2}% detected. Buy at {item.BestBuy:N0} ISK, sell at {item.BestSell:N0} ISK.",
                    DetectedAt = DateTime.UtcNow,
                    ExpiresAt = DateTime.UtcNow.AddHours(1),
                    Status = "active"
                };

                opportunities.Add(opportunity);
            }

            if (opportunities.Any())
            {
                await _dbContext.TradingOpportunities.AddRangeAsync(opportunities);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("Created {Count} trading opportunities", opportunities.Count);
            }

            return opportunities;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing market data");
            return new List<TradingOpportunity>();
        }
    }
}
