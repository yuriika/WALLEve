using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using WALLEve.Configuration;
using WALLEve.Models.AI;
using WALLEve.Services.AI.Interfaces;

namespace WALLEve.Services.AI;

/// <summary>
/// Service für Kommunikation mit Ollama (lokales LLM)
/// </summary>
public class OllamaService : IOllamaService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OllamaService> _logger;
    private readonly string _defaultModel;
    private readonly string _baseUrl;

    public OllamaService(
        IHttpClientFactory httpClientFactory,
        IOptions<AISettings> settings,
        ILogger<OllamaService> logger)
    {
        _httpClient = httpClientFactory.CreateClient("Ollama");
        _logger = logger;
        _defaultModel = settings.Value.Ollama.DefaultModel;
        _baseUrl = settings.Value.Ollama.BaseUrl;

        _logger.LogInformation("Ollama Service initialized with base URL: {BaseUrl}, Default Model: {Model}",
            _baseUrl, _defaultModel);
    }

    /// <summary>
    /// Prüft ob Ollama erreichbar ist
    /// </summary>
    /// <returns>True wenn Ollama verfügbar ist</returns>
    public async Task<bool> IsAvailableAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/tags");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ollama not available at {BaseUrl}", _baseUrl);
            return false;
        }
    }

    /// <summary>
    /// Holt Liste aller verfügbaren Ollama-Modelle
    /// </summary>
    /// <returns>Liste der Model-Namen oder null bei Fehler</returns>
    public async Task<List<string>?> GetAvailableModelsAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/tags");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(content);

            if (doc.RootElement.TryGetProperty("models", out var modelsElement))
            {
                var models = new List<string>();
                foreach (var model in modelsElement.EnumerateArray())
                {
                    if (model.TryGetProperty("name", out var nameElement))
                    {
                        models.Add(nameElement.GetString() ?? "");
                    }
                }
                return models;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching available models from Ollama");
            return null;
        }
    }

    /// <summary>
    /// Generiert Text-Antwort von Ollama basierend auf einem Prompt
    /// </summary>
    /// <param name="prompt">Der Prompt für das LLM</param>
    /// <param name="context">Optionales Kontext-Objekt (wird als JSON serialisiert)</param>
    /// <param name="model">Optionales Model-Override (default: llama3.1:latest)</param>
    /// <returns>Generated text response</returns>
    public async Task<string> GenerateAsync(string prompt, object? context = null, string? model = null)
    {
        try
        {
            var effectiveModel = model ?? _defaultModel;

            // Build full prompt with context if provided
            var fullPrompt = context != null
                ? $"{prompt}\n\nContext Data:\n{JsonSerializer.Serialize(context, new JsonSerializerOptions { WriteIndented = true })}"
                : prompt;

            var request = new OllamaGenerateRequest
            {
                Model = effectiveModel,
                Prompt = fullPrompt,
                Stream = false,
                Options = new OllamaOptions
                {
                    Temperature = 0.7,
                    TopP = 0.9
                }
            };

            _logger.LogDebug("Sending request to Ollama: Model={Model}, PromptLength={Length}",
                effectiveModel, fullPrompt.Length);

            var jsonContent = JsonSerializer.Serialize(request);
            var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("/api/generate", httpContent);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            var ollamaResponse = JsonSerializer.Deserialize<OllamaGenerateResponse>(responseContent);

            if (ollamaResponse == null)
            {
                _logger.LogError("Failed to deserialize Ollama response");
                throw new InvalidOperationException("Invalid Ollama response");
            }

            _logger.LogInformation("Ollama generated response: Model={Model}, Length={Length}, Duration={Duration}ms",
                ollamaResponse.Model,
                ollamaResponse.Response.Length,
                ollamaResponse.TotalDuration.HasValue ? ollamaResponse.TotalDuration.Value / 1_000_000 : 0);

            return ollamaResponse.Response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating response from Ollama");
            throw;
        }
    }

    /// <summary>
    /// Generiert strukturierte JSON-Antwort von Ollama
    /// </summary>
    /// <typeparam name="T">Der zu deserialisierende Typ</typeparam>
    /// <param name="prompt">Der Prompt für das LLM</param>
    /// <param name="context">Optionales Kontext-Objekt</param>
    /// <param name="model">Optionales Model-Override</param>
    /// <returns>Deserialisiertes Objekt vom Typ T oder default bei Fehler</returns>
    public async Task<T?> GenerateJsonAsync<T>(string prompt, object? context = null, string? model = null)
    {
        try
        {
            // Add JSON instruction to prompt
            var jsonPrompt = $"{prompt}\n\nIMPORTANT: You must respond with ONLY valid JSON. Do not include any explanatory text, markdown formatting, or code blocks. Return pure JSON only.";

            var response = await GenerateAsync(jsonPrompt, context, model);

            if (string.IsNullOrWhiteSpace(response))
            {
                _logger.LogWarning("Ollama returned empty response for JSON request");
                return default;
            }

            // Extract JSON from response (handle markdown code blocks)
            var json = ExtractJson(response);

            if (string.IsNullOrWhiteSpace(json))
            {
                _logger.LogWarning("Could not extract JSON from Ollama response: {Response}",
                    response.Length > 200 ? response.Substring(0, 200) + "..." : response);
                return default;
            }

            _logger.LogDebug("Extracted JSON: {Json}",
                json.Length > 200 ? json.Substring(0, 200) + "..." : json);

            return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Error parsing JSON from Ollama response");
            throw new InvalidOperationException("Failed to parse JSON from AI response", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating JSON from Ollama");
            throw;
        }
    }

    /// <summary>
    /// Extrahiert JSON aus Response (behandelt Markdown Code Blocks)
    /// </summary>
    private string ExtractJson(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return string.Empty;

        // Try to find JSON in markdown code block
        var codeBlockMatch = Regex.Match(response, @"```(?:json)?\s*(\{[\s\S]*?\}|\[[\s\S]*?\])\s*```", RegexOptions.Multiline);
        if (codeBlockMatch.Success)
        {
            return codeBlockMatch.Groups[1].Value.Trim();
        }

        // Try to find raw JSON object or array
        var jsonMatch = Regex.Match(response, @"(\{[\s\S]*\}|\[[\s\S]*\])", RegexOptions.Multiline);
        if (jsonMatch.Success)
        {
            return jsonMatch.Groups[1].Value.Trim();
        }

        // Return original response if no JSON found
        return response.Trim();
    }
}
