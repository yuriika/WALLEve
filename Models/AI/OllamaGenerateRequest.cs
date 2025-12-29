using System.Text.Json.Serialization;

namespace WALLEve.Models.AI;

/// <summary>
/// Request model for Ollama /api/generate endpoint
/// </summary>
public class OllamaGenerateRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = "llama3.1:8b";

    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } = string.Empty;

    [JsonPropertyName("stream")]
    public bool Stream { get; set; } = false;

    [JsonPropertyName("options")]
    public OllamaOptions? Options { get; set; }
}

public class OllamaOptions
{
    [JsonPropertyName("temperature")]
    public double Temperature { get; set; } = 0.7;

    [JsonPropertyName("top_p")]
    public double TopP { get; set; } = 0.9;

    [JsonPropertyName("top_k")]
    public int? TopK { get; set; }

    [JsonPropertyName("num_predict")]
    public int? NumPredict { get; set; }
}
