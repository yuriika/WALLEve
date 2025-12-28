namespace WALLEve.Models.Map;

/// <summary>
/// Routing-Präferenz (wie soll Route berechnet werden)
/// </summary>
public enum RoutingPreference
{
    /// <summary>
    /// Kürzeste Route (minimale Anzahl Jumps)
    /// </summary>
    Shorter,

    /// <summary>
    /// Sichere Route (bevorzugt High-Sec)
    /// </summary>
    Safer,

    /// <summary>
    /// Unsichere Route (bevorzugt Low/Null-Sec)
    /// </summary>
    LessSecure
}
