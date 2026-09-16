namespace WALLEve.Models.Risk;

/// <summary>
/// Zusammenfassung der Risikoevidenz für eine Route und ihre Systeme.
///
/// Konservatives Default: Jedes System ohne verfügbare Evidenz wird als
/// <see cref="RiskLevel.Unknown"/> geführt; es gibt keine Annahme „sicher“.
/// </summary>
public class RouteRiskSummary
{
    /// <summary>Evidenz je System und Quelle (nur Systeme der Route).</summary>
    public List<SystemRiskEvidence> Evidence { get; set; } = new();

    /// <summary>Aggregierte Evidenz je System in Routenreihenfolge.</summary>
    public Dictionary<int, List<SystemRiskEvidence>> BySystem { get; set; } = new();

    /// <summary>
    /// Höchste ermittelte Stufe der Route. Solange irgendein System unbekannt ist,
    /// bleibt die Gesamtstufe konservativ „unbekannt“, bis die Quelle nachliefert.
    /// </summary>
    public RiskLevel RouteLevel { get; set; } = RiskLevel.Unknown;

    /// <summary>Zeitpunkt der letzten erfolgreichen Erhebung (null, wenn nie).</summary>
    public DateTimeOffset? LastCollectedAt { get; set; }

    /// <summary>Quellen, die während der Erhebung nicht verfügbar waren.</summary>
    public List<RiskEvidenceSource> UnavailableSources { get; set; } = new();
}