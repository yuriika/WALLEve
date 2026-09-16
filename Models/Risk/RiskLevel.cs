namespace WALLEve.Models.Risk;

/// <summary>
/// Risikostufe eines Systems.
/// Default ist <see cref="Unknown"/>: Fehlt ein Verlustnachweis, gilt die Lage
/// als unbekannt — niemals automatisch als sicher.
/// </summary>
public enum RiskLevel
{
    Unknown = 0,
    Low = 1,
    Elevated = 2,
    High = 3
}