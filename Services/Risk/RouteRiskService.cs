using WALLEve.Models.Risk;
using WALLEve.Services.Risk.Interfaces;

namespace WALLEve.Services.Risk;

/// <summary>
/// Risikoerhebung für Routen (#73): führt ESI Jumps/Kills und zKillboard-Verluste
/// als getrennte Evidence (Quelle, Alter, Delayed-Hinweis) und fasst sie zu einer
/// konservativen Routen-Stufe zusammen.
///
/// Regeln:
/// - ESI-Aktivität wird einmal je Routenaufruf geladen (kein N+1); beide
///   Endpunkte decken alle Systeme ab.
/// - zKillboard wird je System über den Adapter abgefragt (Cache + Abstand dort).
/// - Jede nicht verfügbare Quelle erzeugt nicht verfügbare Evidence und landet
///   in <see cref="RouteRiskSummary.UnavailableSources"/>.
/// - Der Verlustnachweis (zKillboard) ist der Gatekeeper: Fehlt er für ein
///   System, gilt das System als unbekannt; ESI-Aktivität allein klassifiziert
///   nicht. Ist irgendein System unbekannt, bleibt die Gesamtstufe unbekannt.
/// - Die Erhebung wirft nie; Aufrufer-Cancellation wird durchgereicht.
/// </summary>
public sealed class RouteRiskService : IRouteRiskService
{
    // Bewusst einfache, dokumentierte Schwellen für die Stufen-Einschätzung.
    private const int HighLossesThreshold = 10;
    private const int HighKillsThreshold = 25;
    private const int HighJumpsThreshold = 50;
    private const int ElevatedLossesThreshold = 1;
    private const int ElevatedKillsThreshold = 5;
    private const int ElevatedJumpsThreshold = 15;

    private readonly IUniverseActivitySource _activitySource;
    private readonly IZkillboardClient _zkillboard;
    private readonly ILogger<RouteRiskService> _logger;

    public RouteRiskService(
        IUniverseActivitySource activitySource,
        IZkillboardClient zkillboard,
        ILogger<RouteRiskService> logger)
    {
        _activitySource = activitySource;
        _zkillboard = zkillboard;
        _logger = logger;
    }

    public async Task<RouteRiskSummary> CollectRouteRiskAsync(
        IReadOnlyList<int> systemIds,
        CancellationToken ct = default)
    {
        var summary = new RouteRiskSummary();

        if (systemIds == null || systemIds.Count == 0)
        {
            return summary;
        }

        var distinctSystemIds = systemIds.Distinct().ToList();
        var collectedAt = DateTimeOffset.UtcNow;
        var anySourceAvailable = false;

        // ESI-Aktivität: genau ein Request pro Endpunkt, deckt alle Systeme ab.
        IReadOnlyDictionary<int, int>? jumpsBySystem = null;
        IReadOnlyDictionary<int, int>? killsBySystem = null;
        try
        {
            jumpsBySystem = await _activitySource.GetJumpsBySystemAsync(ct);
            killsBySystem = await _activitySource.GetKillsBySystemAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RouteRiskService: ESI-Aktivitätsdaten nicht verfügbar");
        }

        var esiAvailable = jumpsBySystem != null && killsBySystem != null;
        if (esiAvailable)
        {
            anySourceAvailable = true;
        }
        else
        {
            summary.UnavailableSources.Add(RiskEvidenceSource.EsiJumpsKills);
        }

        // zKillboard: optionale Quelle je System (Cache + Abstand im Adapter).
        var zkBEnabled = _zkillboard.IsEnabled;
        var zkBResults = new Dictionary<int, ZkillboardLosses?>(distinctSystemIds.Count);
        if (!zkBEnabled)
        {
            summary.UnavailableSources.Add(RiskEvidenceSource.Zkillboard);
        }
        else
        {
            foreach (var systemId in distinctSystemIds)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    zkBResults[systemId] = await _zkillboard.GetLossesAsync(systemId, ct);
                    if (zkBResults[systemId] != null)
                    {
                        anySourceAvailable = true;
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "RouteRiskService: zKillboard-Abfrage für System {SystemId} fehlgeschlagen", systemId);
                    zkBResults[systemId] = null;
                }
            }

            if (zkBResults.Values.Any(v => v == null))
            {
                summary.UnavailableSources.Add(RiskEvidenceSource.Zkillboard);
            }
        }

        summary.LastCollectedAt = anySourceAvailable ? collectedAt : null;

        // Evidence je System aufbauen (Routenreihenfolge bleibt über BySystem erhalten).
        foreach (var systemId in distinctSystemIds)
        {
            var evidence = new List<SystemRiskEvidence>();

            if (esiAvailable)
            {
                evidence.Add(new SystemRiskEvidence
                {
                    SystemId = systemId,
                    Source = RiskEvidenceSource.EsiJumpsKills,
                    Jumps = jumpsBySystem!.GetValueOrDefault(systemId),
                    Kills = killsBySystem!.GetValueOrDefault(systemId),
                    CollectedAt = collectedAt,
                    IsDelayed = false,
                    IsAvailable = true
                });
            }
            else
            {
                evidence.Add(new SystemRiskEvidence
                {
                    SystemId = systemId,
                    Source = RiskEvidenceSource.EsiJumpsKills,
                    CollectedAt = collectedAt,
                    IsDelayed = false,
                    IsAvailable = false,
                    Error = "ESI-Aktivitätsdaten nicht verfügbar"
                });
            }

            if (zkBEnabled)
            {
                var zkB = zkBResults.GetValueOrDefault(systemId);
                if (zkB != null)
                {
                    evidence.Add(new SystemRiskEvidence
                    {
                        SystemId = systemId,
                        Source = RiskEvidenceSource.Zkillboard,
                        Losses = zkB.Losses,
                        CollectedAt = zkB.CollectedAt,
                        IsDelayed = zkB.IsDelayed,
                        IsAvailable = true
                    });
                }
                else
                {
                    evidence.Add(new SystemRiskEvidence
                    {
                        SystemId = systemId,
                        Source = RiskEvidenceSource.Zkillboard,
                        CollectedAt = collectedAt,
                        IsDelayed = true,
                        IsAvailable = false,
                        Error = "zKillboard nicht verfügbar (Rate-Limit, Timeout oder fehlende Daten)"
                    });
                }
            }
            else
            {
                evidence.Add(new SystemRiskEvidence
                {
                    SystemId = systemId,
                    Source = RiskEvidenceSource.Zkillboard,
                    CollectedAt = collectedAt,
                    IsDelayed = true,
                    IsAvailable = false,
                    Error = "zKillboard deaktiviert (optionale Quelle)"
                });
            }

            summary.Evidence.AddRange(evidence);
            summary.BySystem[systemId] = evidence;
        }

        // Gesamtstufe: konservativ. Der Verlustnachweis (zKillboard) ist der
        // Gatekeeper: Fehlt er für irgendein System, bleibt die Route unbekannt —
        // ESI-Aktivität allein klassifiziert nie, und niemals automatisch „sicher“.
        var anyUnknown = summary.BySystem.Any(kv =>
        {
            var lossEvidence = kv.Value.FirstOrDefault(e => e.Source == RiskEvidenceSource.Zkillboard);
            return lossEvidence == null || !lossEvidence.IsAvailable;
        });

        if (anyUnknown)
        {
            summary.RouteLevel = RiskLevel.Unknown;
        }
        else
        {
            var maxLevel = RiskLevel.Low;
            foreach (var systemEvidence in summary.BySystem.Values)
            {
                foreach (var evidence in systemEvidence.Where(e => e.IsAvailable))
                {
                    var level = Classify(evidence);
                    if (level > maxLevel)
                    {
                        maxLevel = level;
                    }
                }
            }

            summary.RouteLevel = maxLevel;
        }

        return summary;
    }

    /// <summary>Stufen-Einschätzung einer einzelnen verfügbaren Evidence.</summary>
    private static RiskLevel Classify(SystemRiskEvidence evidence)
    {
        var losses = evidence.Source == RiskEvidenceSource.Zkillboard ? evidence.Losses : 0;
        var kills = evidence.Kills;
        var jumps = evidence.Jumps;

        if (losses >= HighLossesThreshold || kills >= HighKillsThreshold || jumps >= HighJumpsThreshold)
        {
            return RiskLevel.High;
        }

        if (losses >= ElevatedLossesThreshold || kills >= ElevatedKillsThreshold || jumps >= ElevatedJumpsThreshold)
        {
            return RiskLevel.Elevated;
        }

        return RiskLevel.Low;
    }
}