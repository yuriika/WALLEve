using WALLEve.Models.Risk;

namespace WALLEve.Extensions;

/// <summary>
/// Deutsche Anzeigenamen für Risikostufen und Risikoquellen (#73).
/// UI-Texte folgen der Produktsprache (Deutsch).
/// </summary>
public static class RiskLevelExtensions
{
    /// <summary>Anzeigename einer Risikostufe ("Unbekannt", "Niedrig", "Erhöht", "Hoch").</summary>
    public static string GetDisplayName(this RiskLevel level) => level switch
    {
        RiskLevel.Low => "Niedrig",
        RiskLevel.Elevated => "Erhöht",
        RiskLevel.High => "Hoch",
        _ => "Unbekannt"
    };

    /// <summary>Anzeigename einer Risikoquelle ("ESI Jumps/Kills", "zKillboard").</summary>
    public static string GetDisplayName(this RiskEvidenceSource source) => source switch
    {
        RiskEvidenceSource.EsiJumpsKills => "ESI Jumps/Kills",
        _ => "zKillboard"
    };
}