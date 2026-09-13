using Microsoft.EntityFrameworkCore;
using WALLEve.Data;
using WALLEve.Models.Database;
using WALLEve.Models.Market;
using WALLEve.Services.Map.Interfaces;
using WALLEve.Services.Market.Interfaces;

namespace WALLEve.Services.Market;

/// <summary>
/// Persistente Hub- und Vergleichsmarktprofile (Issue #58).
///
/// Die automatische Hub-Wahl läuft über eine exakte BFS-Sprungdistanz im
/// SDE-Systemgraph (ungerichtete Kanten, ungewichtete Distanz in Sprüngen) —
/// bewusst OHNE den späteren M3-Routenplaner (RouteCalculationService bleibt
/// unberührt). Unerreichbare Systeme und fehlender Graph sind kein
/// Nullsprung-Fall: Sie führen zu keinem Kandidaten bzw. zu
/// <see cref="HubSelectionResult.GraphAvailable"/> = false und niemals zu
/// einem ausgewählten Hub mit erfundener 0-Distanz. Gleichstände werden
/// deterministisch über (RegionId, SystemId) aufgelöst. Der Vergleichsmarkt
/// fließt in die Auswahl nicht ein.
/// </summary>
public class HubSelectionService : IHubSelectionService
{
    private readonly WalletDbContext _db;
    private readonly IMapDataService _mapData;

    public HubSelectionService(WalletDbContext db, IMapDataService mapData)
    {
        _db = db;
        _mapData = mapData;
    }

    public async Task<List<MarketHubProfile>> GetProfilesAsync(CancellationToken ct = default)
        => await _db.MarketHubProfiles
            .AsNoTracking()
            .OrderBy(p => p.RegionId)
            .ThenBy(p => p.SystemId)
            .ToListAsync(ct);

    public async Task<MarketHubProfile?> GetComparisonMarketAsync(CancellationToken ct = default)
        => await _db.MarketHubProfiles
            .AsNoTracking()
            .OrderByDescending(p => p.UpdatedAt)
            .FirstOrDefaultAsync(p => p.IsComparisonMarket, ct);

    public async Task SaveProfileAsync(MarketHubProfile profile, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.SystemId <= 0)
        {
            throw new ArgumentException("SystemId muss ein gültiges Solar-System sein.", nameof(profile));
        }

        profile.UpdatedAt = DateTime.UtcNow;

        if (profile.Id == 0)
        {
            _db.MarketHubProfiles.Add(profile);
        }
        else
        {
            var existing = await _db.MarketHubProfiles.FindAsync(new object?[] { profile.Id }, ct);
            if (existing == null)
            {
                throw new InvalidOperationException($"Hub-Profil {profile.Id} existiert nicht.");
            }

            existing.Name = profile.Name;
            existing.RegionId = profile.RegionId;
            existing.SystemId = profile.SystemId;
            existing.LocationId = profile.LocationId;
            existing.IsActiveHub = profile.IsActiveHub;
            existing.IsComparisonMarket = profile.IsComparisonMarket;
            existing.UpdatedAt = profile.UpdatedAt;
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteProfileAsync(int profileId, CancellationToken ct = default)
    {
        var profile = await _db.MarketHubProfiles.FindAsync(new object?[] { profileId }, ct);
        if (profile == null) return;

        _db.MarketHubProfiles.Remove(profile);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<HubSelectionResult> SelectNearestActiveHubAsync(
        int fromSystemId,
        CancellationToken ct = default)
    {
        var activeHubs = await _db.MarketHubProfiles
            .AsNoTracking()
            .Where(p => p.IsActiveHub)
            .OrderBy(p => p.RegionId)
            .ThenBy(p => p.SystemId)
            .ToListAsync(ct);

        if (activeHubs.Count == 0)
        {
            return new HubSelectionResult { GraphAvailable = true };
        }

        // Exakter, ungewichteter kürzester Weg (BFS) über den SDE-Systemgraph.
        // Kanten werden symmetrisch normalisiert: Stargate-/Jump-Routen sind
        // in EVE bidirektional, eine einseitige Tabellenreihe täuscht sonst
        // kürzere oder unerreichbare Pfade vor.
        var graph = await BuildSymmetricGraphAsync(ct);

        if (graph.Count == 0)
        {
            // Graph nicht verfügbar: KEINE Auswahl, niemals 0-Sprung-Fiktion.
            return new HubSelectionResult { GraphAvailable = false };
        }

        var distances = ComputeExactJumpDistances(graph, fromSystemId, activeHubs.Select(h => h.SystemId));

        var candidates = activeHubs.Select(h => new HubDistance
        {
            ProfileId = h.Id,
            Name = h.Name,
            RegionId = h.RegionId,
            SystemId = h.SystemId,
            JumpDistance = distances.TryGetValue(h.SystemId, out var d) ? d : null
        }).ToList();

        // Unerreichbare Hubs ausschließen — nie als 0 Sprünge werten.
        var reachable = candidates.Where(c => c.Reachable).ToList();
        if (reachable.Count == 0)
        {
            return new HubSelectionResult { GraphAvailable = true, Candidates = candidates };
        }

        // Determinismus bei Gleichstand: (RegionId, SystemId) aufsteigend.
        var selected = reachable
            .OrderBy(c => c.JumpDistance)
            .ThenBy(c => c.RegionId)
            .ThenBy(c => c.SystemId)
            .First();

        return new HubSelectionResult
        {
            GraphAvailable = true,
            Selected = selected,
            Candidates = candidates
        };
    }

    /// <summary>
    /// Ungerichteter Systemgraph aus dem SDE (SystemID → Nachbarn).
    /// Nicht erreichbar heißt: kein Pfad im Graph — kein Nullsprung.
    /// </summary>
    internal async Task<Dictionary<int, List<int>>> BuildSymmetricGraphAsync(CancellationToken ct)
    {
        var raw = await _mapData.BuildSystemGraphAsync();
        ct.ThrowIfCancellationRequested();

        var symmetric = new Dictionary<int, List<int>>();
        foreach (var (from, neighbors) in raw)
        {
            foreach (var to in neighbors)
            {
                AddEdge(symmetric, from, to);
                AddEdge(symmetric, to, from);
            }
        }

        return symmetric;
    }

    private static void AddEdge(Dictionary<int, List<int>> graph, int from, int to)
    {
        if (!graph.TryGetValue(from, out var list))
        {
            list = new List<int>();
            graph[from] = list;
        }
        if (!list.Contains(to))
        {
            list.Add(to);
        }
    }

    /// <summary>
    /// Exakte ungewichtete Kürzest-Distanz von <paramref name="fromSystemId"/>
    /// zu allen Zielen per BFS. Nur erreichbare Ziele erhalten einen Wert;
    /// fehlende Einträge bedeuten NICHT 0 Sprünge, sondern unerreichbar.
    /// </summary>
    internal static Dictionary<int, int> ComputeExactJumpDistances(
        Dictionary<int, List<int>> graph,
        int fromSystemId,
        IEnumerable<int> targets)
    {
        var targetSet = targets.ToHashSet();
        var distances = new Dictionary<int, int>();

        // BFS vom Ausgangssystem: Jeder erreichbare Knoten erhält seine exakte
        // Kürzest-Distanz. Nicht im Graphen (unbekannt/isoliert) heißt: keine
        // Nachbarn — nur das Ausgangssystem selbst (0 Sprünge ist nur dort echt).
        if (!graph.TryGetValue(fromSystemId, out _))
        {
            if (targetSet.Contains(fromSystemId))
            {
                distances[fromSystemId] = 0;
            }
            return distances;
        }

        distances[fromSystemId] = 0;
        var queue = new Queue<int>();
        queue.Enqueue(fromSystemId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var currentDistance = distances[current];

            if (!graph.TryGetValue(current, out var neighbors)) continue;

            foreach (var next in neighbors)
            {
                if (distances.ContainsKey(next)) continue;

                distances[next] = currentDistance + 1;
                queue.Enqueue(next);
            }
        }

        return distances;
    }
}