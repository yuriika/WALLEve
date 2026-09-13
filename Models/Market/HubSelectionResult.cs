namespace WALLEve.Models.Market;

/// <summary>
/// Sprungdistanz-Ergebnis eines Hub-Kandidaten (Issue #58).
/// Nullbare Distanz: null = nicht erreichbar (nie als 0 Sprünge zu werten).
/// </summary>
public class HubDistance
{
    public int ProfileId { get; set; }

    public string Name { get; set; } = string.Empty;

    public int RegionId { get; set; }

    public int SystemId { get; set; }

    /// <summary>
    /// Exakte ungewichtete Sprungdistanz im SDE-Graph; null, wenn der Hub
    /// vom Ausgangssystem aus nicht erreichbar ist. Unerreichbar/unknown
    /// wird nie als Nullsprünge interpretiert.
    /// </summary>
    public int? JumpDistance { get; set; }

    public bool Reachable => JumpDistance.HasValue;
}

/// <summary>
/// Ergebnis der automatischen Hub-Wahl (Issue #58).
/// </summary>
public class HubSelectionResult
{
    /// <summary>Gewählter aktivierter Hub; null, wenn keiner auswählbar ist.</summary>
    public HubDistance? Selected { get; set; }

    /// <summary>Alle aktivierten Hubs mit ihrer realen Distanz (Bewertungsgrundlage).</summary>
    public List<HubDistance> Candidates { get; set; } = new();

    /// <summary>
    /// false, wenn der SDE-Graph nicht verfügbar ist — dann wird NIE ein Hub
    /// mit fiktiven 0 Sprüngen ausgewählt.
    /// </summary>
    public bool GraphAvailable { get; set; }
}