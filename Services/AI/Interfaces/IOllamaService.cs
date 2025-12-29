namespace WALLEve.Services.AI.Interfaces;

/// <summary>
/// Service für Ollama AI Integration
/// Kommuniziert mit lokaler Ollama Installation
/// </summary>
public interface IOllamaService
{
    /// <summary>
    /// Generiert Text basierend auf einem Prompt
    /// </summary>
    Task<string> GenerateAsync(string prompt, object? context = null, string? model = null);

    /// <summary>
    /// Generiert und parsed JSON-Response
    /// </summary>
    Task<T?> GenerateJsonAsync<T>(string prompt, object? context = null, string? model = null);

    /// <summary>
    /// Prüft ob Ollama verfügbar ist
    /// </summary>
    Task<bool> IsAvailableAsync();

    /// <summary>
    /// Listet verfügbare Modelle
    /// </summary>
    Task<List<string>?> GetAvailableModelsAsync();
}
