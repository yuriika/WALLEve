namespace WALLEve.Configuration;

/// <summary>
/// Configuration für AI/Ollama Integration.
/// Die Marktanalyse ist deterministisch (kein LLM); ihre Provenienz ist in den
/// Trading-Opportunities gespeichert (Issue #33). Diese Settings betreffen nur
/// die optionale LLM-Anbindung.
/// </summary>
public class AISettings
{
    public OllamaSettings Ollama { get; set; } = new();
}

public class OllamaSettings
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string DefaultModel { get; set; } = "llama3.1:8b";
    public int TimeoutSeconds { get; set; } = 30;
}