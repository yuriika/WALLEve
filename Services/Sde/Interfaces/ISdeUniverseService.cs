using WALLEve.Models.Sde;

namespace WALLEve.Services.Sde.Interfaces;

/// <summary>
/// Service für SDE-Zugriff auf Universe-Daten (Types, Systems, Regions, etc.)
/// </summary>
public interface ISdeUniverseService
{
    /// <summary>
    /// Prüft ob die SDE-Datenbank verfügbar und nutzbar ist
    /// </summary>
    Task<bool> IsDatabaseAvailableAsync();

    /// <summary>
    /// Holt den Namen eines Types (z.B. Schiff, Item, Skill)
    /// </summary>
    Task<string?> GetTypeNameAsync(int typeId);

    /// <summary>
    /// Holt die Gruppe eines Types (z.B. "Battleship", "Cruiser")
    /// </summary>
    Task<string?> GetTypeGroupAsync(int typeId);

    /// <summary>
    /// Holt Informationen über ein Sonnensystem
    /// </summary>
    Task<SolarSystemInfo?> GetSolarSystemAsync(int solarSystemId);

    /// <summary>
    /// Holt den Namen einer Region
    /// </summary>
    Task<string?> GetRegionNameAsync(int regionId);

    /// <summary>
    /// Holt den Namen einer Location (Station, Citadel, etc.)
    /// </summary>
    Task<string?> GetLocationNameAsync(long locationId);

    /// <summary>
    /// Holt alle handelbaren Items (market items) aus der SDE
    /// </summary>
    Task<Dictionary<int, string>> GetAllMarketItemsAsync();

    /// <summary>
    /// Holt alle Regionen aus der SDE
    /// </summary>
    Task<Dictionary<int, string>> GetAllRegionsAsync();

    /// <summary>
    /// Sucht nach Sonnensystemen nach Namen (für Autocomplete)
    /// </summary>
    Task<Dictionary<int, string>> SearchSolarSystemsAsync(string searchQuery, int maxResults = 10);
}
