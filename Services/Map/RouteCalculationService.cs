using WALLEve.Models.Map;
using WALLEve.Services.Map.Interfaces;

namespace WALLEve.Services.Map;

/// <summary>
/// Routenberechnung über den SDE-System-Graphen (lokaler Dijkstra).
///
/// Vertrag: Das Ergebnis (RouteResult) ist der gemeinsame Routenvertrag für Filter,
/// Karte und Rechnung — Systemfolge (Path + Systems) und Security-Zusammenfassung
/// werden einheitlich anhand der Security-Klassen von <see cref="MapSolarSystemNode"/>
/// (highsec &gt;= 0.5, lowsec &gt;= 0.1, sonst nullsec) berechnet.
///
/// Der Dienst ist zustandslos: Jeder Aufruf erhält Start und Ziel explizit. Ein
/// Charakterwechsel invalidiert den Ursprung damit automatisch — es wird nie ein
/// früherer Ursprung wiederverwendet (siehe Regressionstests).
/// </summary>
public class RouteCalculationService : IRouteCalculationService
{
    private const int HighSecPenalty = 10;
    private const int NonHighSecPenalty = 10;

    private readonly IMapDataService _mapData;
    private readonly ILogger<RouteCalculationService> _logger;

    public RouteCalculationService(
        IMapDataService mapData,
        ILogger<RouteCalculationService> logger)
    {
        _mapData = mapData;
        _logger = logger;
    }

    public async Task<RouteResult> CalculateRouteLocalAsync(
        int originId,
        int destinationId,
        RoutingPreference preference)
    {
        if (originId <= 0)
        {
            return Failed($"Unbekanntes Start-System: {originId}");
        }

        if (destinationId <= 0)
        {
            return Failed($"Unbekanntes Ziel-System: {destinationId}");
        }

        if (originId == destinationId)
        {
            return await ZeroJumpRouteAsync(originId);
        }

        var graph = await _mapData.BuildSystemGraphAsync();
        if (graph.Count == 0)
        {
            _logger.LogWarning("RouteCalculationService: System-Graph ist leer");
            return Failed("System-Graph nicht verfügbar");
        }

        if (!graph.ContainsKey(originId))
        {
            return Failed($"Start-System {originId} ist nicht im System-Graph");
        }

        if (!graph.ContainsKey(destinationId))
        {
            return Failed($"Ziel-System {destinationId} ist nicht im System-Graph");
        }

        // Security-Lookup für Kantengewichte: einmal je Routenaufruf laden.
        // Fehlende Einträge gelten als unbekannt → neutrales Gewicht (1), aber
        // für die Zusammenfassung als nullsec klassifiziert (konservativ).
        var securityById = await LoadSecurityLookupAsync(graph.Keys.ToList());

        var path = FindShortestPath(graph, originId, destinationId, preference, securityById);

        if (path == null)
        {
            _logger.LogInformation(
                "RouteCalculationService: Kein Pfad von {Origin} nach {Destination} ({Preference})",
                originId, destinationId, preference);
            return Failed($"Kein Pfad von System {originId} nach System {destinationId} gefunden");
        }

        return await BuildSuccessResultAsync(path, securityById);
    }

    public async Task<RouteResult> CalculateRouteEsiAsync(
        int originId,
        int destinationId,
        RoutingPreference preference)
    {
        _logger.LogWarning("RouteCalculationService: ESI routing not yet implemented");
        return await Task.FromResult(new RouteResult
        {
            Success = false,
            Error = "ESI routing not yet implemented"
        });
    }

    public async Task<RouteComparisonResult> CalculateRouteComparisonAsync(
        int originId,
        int destinationId,
        RoutingPreference preference)
    {
        var local = await CalculateRouteLocalAsync(originId, destinationId, preference);
        var esi = await CalculateRouteEsiAsync(originId, destinationId, preference);

        return new RouteComparisonResult
        {
            LocalRoute = local,
            EsiRoute = esi,
            RoutesMatch = local.Success && esi.Success && SequenceEquals(local.Path, esi.Path),
            JumpDifference = (local.Path?.Count ?? 0) - (esi.Path?.Count ?? 0),
            LocalOnlySystemIds = local.Success && esi.Success
                ? (local.Path ?? new List<int>()).Except(esi.Path ?? new List<int>()).ToList()
                : new List<int>(),
            EsiOnlySystemIds = local.Success && esi.Success
                ? (esi.Path ?? new List<int>()).Except(local.Path ?? new List<int>()).ToList()
                : new List<int>()
        };
    }

    // ------------------------------------------------------------------
    // Dijkstra
    // ------------------------------------------------------------------

    /// <summary>
    /// Findet den gewichteten kürzesten Pfad (Dijkstra) im System-Graphen.
    /// Rückgabe: Systemfolge von Origin nach Destination inklusive beider Enden,
    /// oder null, wenn das Ziel nicht erreichbar ist.
    /// </summary>
    private static List<int>? FindShortestPath(
        Dictionary<int, List<int>> graph,
        int originId,
        int destinationId,
        RoutingPreference preference,
        Dictionary<int, float> securityById)
    {
        var distances = new Dictionary<int, long>();
        var previous = new Dictionary<int, int>();
        var visited = new HashSet<int>();
        var queue = new PriorityQueue<int, long>();
        var weightCache = new Dictionary<(int from, int to), long>();

        distances[originId] = 0;
        queue.Enqueue(originId, 0);

        while (queue.TryDequeue(out var current, out var currentDistance))
        {
            if (!visited.Add(current))
            {
                continue;
            }

            if (current == destinationId)
            {
                break;
            }

            if (!graph.TryGetValue(current, out var neighbors))
            {
                continue;
            }

            foreach (var next in neighbors)
            {
                if (visited.Contains(next))
                {
                    continue;
                }

                var weight = GetEdgeWeight(current, next, preference, securityById, weightCache);
                var newDistance = currentDistance + weight;

                if (newDistance < distances.GetValueOrDefault(next, long.MaxValue))
                {
                    distances[next] = newDistance;
                    previous[next] = current;
                    queue.Enqueue(next, newDistance);
                }
            }
        }

        if (!distances.ContainsKey(destinationId))
        {
            return null;
        }

        // Pfad rückwärts rekonstruieren: Destination → Origin.
        var path = new List<int> { destinationId };
        var cursor = destinationId;
        while (cursor != originId && previous.TryGetValue(cursor, out var prev))
        {
            path.Add(prev);
            cursor = prev;
        }

        if (cursor != originId)
        {
            return null;
        }

        path.Reverse();
        return path;
    }

    /// <summary>
    /// Kantengewicht gemäß Routing-Präferenz. Gewicht 1 = neutraler Sprung;
    /// höhere Gewichte drücken die Route von unerwünschten Security-Klassen weg.
    /// </summary>
    private static long GetEdgeWeight(
        int from,
        int to,
        RoutingPreference preference,
        Dictionary<int, float> securityById,
        Dictionary<(int from, int to), long> weightCache)
    {
        if (preference == RoutingPreference.Shorter)
        {
            return 1;
        }

        if (weightCache.TryGetValue((from, to), out var cached))
        {
            return cached;
        }

        var weight = 1L;
        var targetSecurity = securityById.GetValueOrDefault(to, -1.0f);

        switch (preference)
        {
            case RoutingPreference.Safer:
                // High-Sec bevorzugen: Sprünge in Low-/Null-Sec stark verteuern.
                if (targetSecurity < 0.5f)
                {
                    weight = NonHighSecPenalty;
                }

                break;

            case RoutingPreference.LessSecure:
                // Low-/Null-Sec bevorzugen: High-Sec-Sprünge verteuern.
                if (targetSecurity >= 0.5f)
                {
                    weight = HighSecPenalty;
                }

                break;
        }

        weightCache[(from, to)] = weight;
        return weight;
    }

    // ------------------------------------------------------------------
    // Ergebnis-Aufbau
    // ------------------------------------------------------------------

    private static RouteResult Failed(string error) => new()
    {
        Success = false,
        Error = error
    };

    private async Task<RouteResult> ZeroJumpRouteAsync(int systemId)
    {
        var system = await _mapData.GetSolarSystemAsync(systemId);
        if (system == null)
        {
            return Failed($"Start-System {systemId} ist nicht im System-Graph");
        }

        return await BuildSuccessResultAsync(new List<int> { systemId }, new Dictionary<int, float> { [systemId] = system.Security });
    }

    private async Task<Dictionary<int, float>> LoadSecurityLookupAsync(List<int> systemIds)
    {
        var systems = await _mapData.GetSystemsByIdsAsync(systemIds);
        var lookup = new Dictionary<int, float>(systems.Count);
        foreach (var system in systems)
        {
            lookup[system.SolarSystemId] = system.Security;
        }

        return lookup;
    }

    /// <summary>
    /// Baut das Routen-Ergebnis aus der Systemfolge: Systems-Liste, TotalJumps,
    /// AverageSecurity und Sprünge je Security-Klasse. Die Klassifikation nutzt
    /// exakt die Schwellen von <see cref="MapSolarSystemNode.SecurityClass"/>,
    /// damit Karte, Filter und Rechnung dieselbe Zusammenfassung liefern.
    /// </summary>
    private async Task<RouteResult> BuildSuccessResultAsync(
        List<int> path,
        Dictionary<int, float> securityById)
    {
        var systems = await _mapData.GetSystemsByIdsAsync(path);

        var systemsByPathId = new Dictionary<int, MapSolarSystemNode>(systems.Count);
        foreach (var system in systems)
        {
            systemsByPathId[system.SolarSystemId] = system;
        }

        // Reihenfolge der Systemfolge beibehalten; fehlende SDE-Daten als
        // Platzhalter ergänzen, damit Zählung und Anzeige nie auseinanderlaufen.
        var orderedSystems = new List<MapSolarSystemNode>(path.Count);
        var orderedSecurities = new List<float>(path.Count);
        foreach (var systemId in path)
        {
            if (systemsByPathId.TryGetValue(systemId, out var node))
            {
                orderedSystems.Add(node);
                orderedSecurities.Add(node.Security);
            }
            else
            {
                var fallback = new MapSolarSystemNode { SolarSystemId = systemId, Name = $"System {systemId}", Security = securityById.GetValueOrDefault(systemId, -1.0f) };
                orderedSystems.Add(fallback);
                orderedSecurities.Add(fallback.Security);
            }
        }

        var totalJumps = Math.Max(0, path.Count - 1);
        var highSecJumps = 0;
        var lowSecJumps = 0;
        var nullSecJumps = 0;

        // Sprünge je Klasse: Ziel-System jedes Sprungs wird gezählt.
        for (var i = 1; i < path.Count; i++)
        {
            switch (Classify(orderedSecurities[i]))
            {
                case SecurityClass.HighSec: highSecJumps++; break;
                case SecurityClass.LowSec: lowSecJumps++; break;
                default: nullSecJumps++; break;
            }
        }

        var averageSecurity = orderedSecurities.Count > 0
            ? orderedSecurities.Average()
            : 0f;

        return new RouteResult
        {
            Success = true,
            Path = path,
            Systems = orderedSystems,
            TotalJumps = totalJumps,
            AverageSecurity = averageSecurity,
            HighSecJumps = highSecJumps,
            LowSecJumps = lowSecJumps,
            NullSecJumps = nullSecJumps
        };
    }

    private static bool SequenceEquals(List<int>? a, List<int>? b)
    {
        if (a == null || b == null)
        {
            return a == b;
        }

        return a.SequenceEqual(b);
    }

    private static SecurityClass Classify(float security) => security switch
    {
        >= 0.5f => SecurityClass.HighSec,
        >= 0.1f => SecurityClass.LowSec,
        _ => SecurityClass.NullSec
    };

    private enum SecurityClass
    {
        HighSec,
        LowSec,
        NullSec
    }
}