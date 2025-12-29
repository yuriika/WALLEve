namespace WALLEve.Models.Map;

/// <summary>
/// Routing-Algorithmus für Routenberechnung
/// </summary>
public enum RoutingAlgorithm
{
    /// <summary>
    /// Lokaler Dijkstra-Algorithmus (offline, keine ESI-Tokens)
    /// </summary>
    LocalDijkstra,

    /// <summary>
    /// ESI API Routing (online, verwendet ESI-Tokens)
    /// </summary>
    EsiApi,

    /// <summary>
    /// Beide Algorithmen vergleichen
    /// </summary>
    Both
}
