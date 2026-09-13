using WALLEve.Models.Database;
using WALLEve.Models.Market;

namespace WALLEve.Services.Market.Interfaces;

/// <summary>
/// Persistente Hub- und Vergleichsmarktprofile (Issue #58).
///
/// Profile speichern additiv Region/System/Location, den aktivierten Hub und
/// einen unabhängigen Vergleichsmarkt. Die automatische Hub-Wahl verwendet
/// ausschließlich die exakte ungewichtete Sprungdistanz im SDE-Graph
/// (BFS, keine Abhängigkeit zum späteren M3-Routenplaner); Gleichstände
/// werden deterministisch aufgelöst, unerreichbare/unknown Systeme gelten
/// nie als Nullsprünge. Der Vergleichsmarkt verändert die Hub-Wahl nicht.
/// </summary>
public interface IHubSelectionService
{
    /// <summary>Alle gespeicherten Hub-Profile.</summary>
    Task<List<MarketHubProfile>> GetProfilesAsync(CancellationToken ct = default);

    /// <summary>Der unabhängige Vergleichsmarkt (null, wenn keiner gesetzt).</summary>
    Task<MarketHubProfile?> GetComparisonMarketAsync(CancellationToken ct = default);

    /// <summary>Profil anlegen oder aktualisieren (Upsert).</summary>
    Task SaveProfileAsync(MarketHubProfile profile, CancellationToken ct = default);

    /// <summary>Profil löschen.</summary>
    Task DeleteProfileAsync(int profileId, CancellationToken ct = default);

    /// <summary>
    /// Wählt den nächsten aktivierten Hub zum Ausgangssystem über die exakte
    /// ungewichtete Sprungdistanz im SDE-Graph. Unerreichbare Hubs werden
    /// ausgeschlossen; ist kein Hub erreichbar oder der Graph nicht verfügbar,
    /// bleibt Selected null (nie eine erfundene 0-Sprung-Distanz).
    /// </summary>
    Task<HubSelectionResult> SelectNearestActiveHubAsync(int fromSystemId, CancellationToken ct = default);
}