namespace WALLEve.Configuration;

/// <summary>
/// Configuration für AI/Ollama Integration
/// </summary>
public class AISettings
{
    public OllamaSettings Ollama { get; set; } = new();
    public MarketAnalysisSettings MarketAnalysis { get; set; } = new();
}

public class OllamaSettings
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string DefaultModel { get; set; } = "llama3.1:8b";
    public int TimeoutSeconds { get; set; } = 30;
}

public class MarketAnalysisSettings
{
    public int UpdateIntervalMinutes { get; set; } = 15;
    public double MinimumProfitPercentage { get; set; } = 5.0;
    public int MaxOpportunitiesPerType { get; set; } = 10;
    public int[] TrackedRegions { get; set; } = { 10000002, 10000043, 10000032 }; // Jita, Amarr, Dodixie
    public bool UseMapForRouteCalculation { get; set; } = true;
}
