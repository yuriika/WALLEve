namespace WALLEve.Models.Map;

/// <summary>
/// Ansichtsmodus der Karte
/// </summary>
public enum MapViewMode
{
    /// <summary>
    /// Zeigt alle Regionen als Nodes
    /// </summary>
    Region,

    /// <summary>
    /// Zeigt alle Systeme in einer Region
    /// </summary>
    System,

    /// <summary>
    /// Zeigt Systeme in X Jumps Entfernung vom Charakter
    /// </summary>
    LocalEnvironment
}
