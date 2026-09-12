using System.Text.Json.Serialization;

namespace WALLEve.Models.Esi.Universe;

/// <summary>
/// Ergebnis von GET /universe/structures/{structure_id}/ (zugängliche
/// Spielerstruktur). Die StructureId stammt aus der URL, nicht aus dem Body.
/// </summary>
public class EsiStructure
{
    public long StructureId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("solar_system_id")]
    public int SolarSystemId { get; set; }

    [JsonPropertyName("type_id")]
    public int TypeId { get; set; }
}

/// <summary>
/// Ergebnis der Struktur-Auflösung nach dem M0-ESI-Vertrag: Fehler werden als
/// Ergebnis geliefert (Error-Grund), nie geworfen. IsResolved genau dann, wenn
/// eine Struktur vorliegt. 403 (kein Zugriff) → Error "403".
/// </summary>
public class StructureLookupResult
{
    public EsiStructure? Structure { get; init; }

    /// <summary>Grund bei unaufgelöster Struktur: "403", "not-found", "unauthenticated", "unavailable".</summary>
    public string? Error { get; init; }

    public bool IsResolved => Structure != null;
}