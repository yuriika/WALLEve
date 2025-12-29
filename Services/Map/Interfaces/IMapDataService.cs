using WALLEve.Models.Map;

namespace WALLEve.Services.Map.Interfaces;

/// <summary>
/// Service für SDE-Zugriff auf Map-Daten (Regionen, Systeme, Verbindungen)
/// </summary>
public interface IMapDataService
{
    /// <summary>
    /// Holt alle Regionen (exklusive Wormholes)
    /// </summary>
    Task<List<MapRegionNode>> GetAllRegionsAsync();

    /// <summary>
    /// Holt eine spezifische Region
    /// </summary>
    Task<MapRegionNode?> GetRegionAsync(int regionId);

    /// <summary>
    /// Holt alle Systeme in einer Region
    /// </summary>
    Task<List<MapSolarSystemNode>> GetSystemsInRegionAsync(int regionId);

    /// <summary>
    /// Holt ein spezifisches Sonnensystem
    /// </summary>
    Task<MapSolarSystemNode?> GetSolarSystemAsync(int solarSystemId);

    /// <summary>
    /// Holt Systeme mehrere System-IDs (bulk operation)
    /// </summary>
    Task<List<MapSolarSystemNode>> GetSystemsByIdsAsync(List<int> systemIds);

    /// <summary>
    /// Holt Systeme innerhalb X Jumps von einem Origin-System (BFS)
    /// </summary>
    Task<List<MapSolarSystemNode>> GetSystemsWithinJumpsAsync(int originSystemId, int maxJumps);

    /// <summary>
    /// Holt Verbindungen zwischen Regionen (für Region-View)
    /// </summary>
    Task<List<MapConnection>> GetRegionConnectionsAsync();

    /// <summary>
    /// Holt Verbindungen innerhalb einer Region
    /// </summary>
    Task<List<MapConnection>> GetSystemConnectionsInRegionAsync(int regionId);

    /// <summary>
    /// Holt Cross-Region Verbindungen für eine Region (Border-Systeme)
    /// </summary>
    Task<List<MapConnection>> GetCrossRegionConnectionsForSystemAsync(int regionId);

    /// <summary>
    /// Holt Verbindungen für eine Liste von System-IDs
    /// </summary>
    Task<List<MapConnection>> GetConnectionsForSystemsAsync(List<int> systemIds);

    /// <summary>
    /// Baut System-Graph für Routing (SystemID -> [Nachbar-IDs])
    /// </summary>
    Task<Dictionary<int, List<int>>> BuildSystemGraphAsync();

    /// <summary>
    /// Holt Live-Aktivitätsdaten (Kills + Jumps) für eine Liste von System-IDs
    /// Kombiniert ESI /universe/system_kills/ und /universe/system_jumps/
    /// </summary>
    Task<Dictionary<int, SystemActivity>> GetSystemActivitiesAsync(List<int> systemIds);
}
