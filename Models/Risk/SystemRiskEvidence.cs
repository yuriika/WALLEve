namespace WALLEve.Models.Risk;

/// <summary>
/// Eine einzelne Risikoevidenz für ein System, strikt nach Quelle getrennt
/// (ESI Jumps/Kills bzw. zKillboard-Verluste).
///
/// Alter (<see cref="CollectedAt"/>) und Verzögerungs-Hinweis
/// (<see cref="IsDelayed"/>) machen die Frische der Daten sichtbar:
/// Eine Quelle, die nichts liefert (Rate-Limit, Timeout, fehlende Daten),
/// erzeugt eine nicht verfügbare Evidenz (<see cref="IsAvailable"/> == false),
/// die als unbekannt → <see cref="RiskLevel.Unknown"/> behandelt wird.
/// </summary>
public class SystemRiskEvidence
{
    public int SystemId { get; set; }

    public RiskEvidenceSource Source { get; set; }

    /// <summary>Sprungaufkommen im System (ESI system/jumps).</summary>
    public int Jumps { get; set; }

    /// <summary>Killaufkommen im System (ESI system/kills).</summary>
    public int Kills { get; set; }

    /// <summary>Verluste im System (zKillboard losses).</summary>
    public int Losses { get; set; }

    /// <summary>Alter der Daten (Zeitpunkt der Erhebung).</summary>
    public DateTimeOffset CollectedAt { get; set; }

    /// <summary>Hinweis, dass die Quelle verzögerte Daten liefern kann.</summary>
    public bool IsDelayed { get; set; }

    /// <summary>
    /// false, wenn die Quelle für dieses System nichts liefern konnte
    /// (Rate-Limit, Timeout oder fehlende Daten).
    /// </summary>
    public bool IsAvailable { get; set; }

    public string? Error { get; set; }
}