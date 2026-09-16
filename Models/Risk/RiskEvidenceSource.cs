namespace WALLEve.Models.Risk;

/// <summary>
/// Quelle einer System-Risikoevidenz.
/// ESI Jumps/Kills und zKillboard-Verluste werden als getrennte Evidenz geführt.
/// </summary>
public enum RiskEvidenceSource
{
    EsiJumpsKills = 0,
    Zkillboard = 1
}