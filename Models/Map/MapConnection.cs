namespace WALLEve.Models.Map;

/// <summary>
/// ========================================================================================
/// MAP CONNECTION MODEL - Stargate-Verbindungen zwischen Solar Systems
/// ========================================================================================
///
/// ZWECK:
/// Repräsentiert eine gerichtete Verbindung (Edge) zwischen zwei Solar Systems via Stargate.
/// Diese Verbindungen bilden das Netzwerk, auf dem die Map-Visualisierung basiert.
///
/// DATENQUELLE:
/// SDE (Static Data Export) Tabelle: mapSolarSystemJumps
/// SQL: SELECT fromSolarSystemID, toSolarSystemID FROM mapSolarSystemJumps
///
/// HIERARCHIE IN EVE ONLINE:
/// Universe
///   ↓
/// Region (z.B. "The Forge") ← ca. 60-100 Regionen
///   ↓
/// Constellation (z.B. "Kimotoro") ← ca. 5-15 Constellations pro Region
///   ↓
/// Solar System (z.B. "Jita") ← ca. 5-30 Systeme pro Constellation
///   ↓
/// Stargates (verbinden Systeme)
///
/// DREI TYPEN VON VERBINDUNGEN:
/// 1. Intra-Constellation: Beide Systeme in gleicher Constellation
///    → Eng verbundene Systeme (meist viele Verbindungen)
///    → Visualisierung: GRÜN, solid line
///
/// 2. Cross-Constellation: Verschiedene Constellations, gleiche Region
///    → Verbindungen zwischen Constellation-Clustern
///    → Visualisierung: CYAN, solid line
///
/// 3. Cross-Region: Verschiedene Regionen
///    → Region-Übergänge (meist nur 1-3 pro Region)
///    → Visualisierung: ORANGE, dashed line → Dummy-Node am Rand
///
/// VERWENDUNG:
/// - In Map.razor: Geladen via MapDataService
/// - In MapCanvas.razor: Konvertiert zu Cytoscape Edges mit Klassifizierung
/// - In cytoscape-map.js: Styling basierend auf Edge-Typ
///
/// GRAPH-THEORIE:
/// - Graph: G = (V, E) wobei V = Solar Systems, E = Stargates
/// - Gerichtet: Jede Verbindung hat Richtung (aber meist bidirektional)
/// - Gewichtet: Alle Edges haben Gewicht 1 (für Routing/Pathfinding)
/// ========================================================================================
/// </summary>
public class MapConnection
{
    /// <summary>
    /// Solar System ID des Ausgangs-Systems
    /// Beispiel: 30000142 (Jita) → 30002187 (Perimeter)
    /// </summary>
    public int FromSystemId { get; set; }

    /// <summary>
    /// Solar System ID des Ziel-Systems
    /// Beispiel: 30000142 (Jita) → 30002187 (Perimeter)
    /// </summary>
    public int ToSystemId { get; set; }

    /// <summary>
    /// Region ID des Ausgangs-Systems
    /// Wird benötigt um Verbindungs-Typ zu klassifizieren
    /// </summary>
    public int FromRegionId { get; set; }

    /// <summary>
    /// Region ID des Ziel-Systems
    /// Wird benötigt um Verbindungs-Typ zu klassifizieren
    /// </summary>
    public int ToRegionId { get; set; }

    /// <summary>
    /// Constellation ID des Ausgangs-Systems
    ///
    /// NEU SEIT 2025-12-29: Für Constellation-basiertes Edge-Styling
    ///
    /// Constellations sind Gruppen von 5-30 Systemen innerhalb einer Region.
    /// Sie bilden natürliche "Cluster" in der Map-Darstellung.
    /// </summary>
    public int FromConstellationId { get; set; }

    /// <summary>
    /// Constellation ID des Ziel-Systems
    ///
    /// NEU SEIT 2025-12-29: Für Constellation-basiertes Edge-Styling
    /// </summary>
    public int ToConstellationId { get; set; }

    /// <summary>
    /// Name der Ziel-Region (für Cross-Region Connections)
    ///
    /// VERWENDUNG:
    /// Wird für Dummy-Nodes verwendet, die am Map-Rand erscheinen.
    /// Beispiel: Wenn Jita (The Forge) zu Amarr (Domain) verbindet,
    /// erscheint ein Dummy-Node "Domain" am Rand der Forge-Map.
    ///
    /// Nur gesetzt bei Cross-Region Connections, sonst null.
    /// </summary>
    public string? ToRegionName { get; set; }

    /// <summary>
    /// BERECHNETE PROPERTY: Ist dies eine Cross-Region Verbindung?
    ///
    /// DEFINITION: FromRegionId != ToRegionId
    ///
    /// BEISPIEL:
    /// Jita (The Forge, RegionID=10000002) → Perimeter (The Forge, RegionID=10000002)
    /// → IsCrossRegion = false
    ///
    /// EC-P8R (Delve, RegionID=10000060) → Y-2ANO (Querious, RegionID=10000050)
    /// → IsCrossRegion = true
    ///
    /// VERWENDUNG:
    /// In MapCanvas.razor:
    /// - false → Normale Edge zwischen Systemen
    /// - true → Edge zu Dummy-Node (orange, gestrichelt)
    ///
    /// HÄUFIGKEIT:
    /// Pro Region gibt es meist nur 1-5 Cross-Region Connections.
    /// Diese sind strategisch wichtig für Inter-Region Bewegung.
    /// </summary>
    public bool IsCrossRegion => FromRegionId != ToRegionId;

    /// <summary>
    /// BERECHNETE PROPERTY: Cross-Constellation Verbindung (innerhalb derselben Region)?
    ///
    /// DEFINITION:
    /// - FromConstellationId != ToConstellationId (verschiedene Constellations)
    /// - UND FromRegionId == ToRegionId (gleiche Region)
    ///
    /// BEISPIEL:
    /// Jita (Kimotoro Constellation) → Maurasi (Kikkia Constellation)
    /// Beide in "The Forge" Region, aber verschiedene Constellations
    /// → IsCrossConstellation = true
    ///
    /// VISUALISIERUNG:
    /// Cyan-farbene Linie (heller als grün, dunkler als orange)
    /// Zeigt Verbindungen zwischen Constellation-Clustern in derselben Region
    ///
    /// VERWENDUNG:
    /// In cytoscape-map.js:
    /// selector: 'edge[crossConstellation]'
    /// style: { 'line-color': '#00ddff', 'width': 2 }
    ///
    /// TYPISCHE SZENARIEN:
    /// - Verbindungen zwischen Handels-Hubs und Produktions-Systemen
    /// - Hauptrouten durch eine Region
    /// </summary>
    public bool IsCrossConstellation => FromConstellationId != ToConstellationId && FromRegionId == ToRegionId;

    /// <summary>
    /// BERECHNETE PROPERTY: Intra-Constellation Verbindung (innerhalb derselben Constellation)?
    ///
    /// DEFINITION: FromConstellationId == ToConstellationId
    ///
    /// BEISPIEL:
    /// Jita → Perimeter (beide in Kimotoro Constellation)
    /// → IsIntraConstellation = true
    ///
    /// VISUALISIERUNG:
    /// Grüne Linie (Standard-Farbe für lokale Verbindungen)
    /// Zeigt enge Nachbarschaft innerhalb eines Constellation-Clusters
    ///
    /// VERWENDUNG:
    /// In cytoscape-map.js:
    /// selector: 'edge[intraConstellation]'
    /// style: { 'line-color': '#44dd88', 'width': 1.5 }
    ///
    /// HÄUFIGKEIT:
    /// Die Mehrheit aller Verbindungen sind Intra-Constellation.
    /// Constellations sind dicht vernetzt intern, dünn extern.
    ///
    /// GRAPH-EIGENSCHAFTEN:
    /// - Hohe Cluster-Koeffizienz innerhalb Constellations
    /// - Niedrige Betweenness-Centrality (nicht auf Hauptrouten)
    /// </summary>
    public bool IsIntraConstellation => FromConstellationId == ToConstellationId;
}
