using System.Text.Json;
using System.Text.Json.Serialization;
using WALLEve.Models.Database;
using WALLEve.Models.Trading;

namespace WALLEve.Services.Trading;

/// <summary>
/// Reine Filterpipeline (Issue #44): wendet die harten Grenzen eines
/// <see cref="TradeProfile"/> in fester Reihenfolge auf eine Kandidaten-
/// Opportunity AN, bevor irgendeine Bewertung/Ranking stattfindet. Die
/// Pipeline ist zustandsfrei und deterministisch — sie greift nie auf die
/// Datenbank oder externe Dienste zu. Abgelehnte Kandidaten können durch
/// spätere Gewichte/Scoring NICHT wieder zugelassen werden: Ranking
/// konsumiert ausschließlich das Pipeline-Ergebnis (Passed-Kandidaten).
/// </summary>
/// <remarks>
/// Reihenfolge der Prüfungen (eine Ablehnungsbegründung je verletzter
/// Grenze, alle Begründungen werden gesammelt):
/// <list type="number">
/// <item>Unbekannte erforderliche Daten blockieren (statt stiller Defaults).</item>
/// <item>Kapital: RequiredCapital &gt; MaxCapital.</item>
/// <item>Cargo: VolumeM3 &gt; MaxCargoVolume.</item>
/// <item>Sprünge: max(JumpDistance, JumpCount) &gt; MaxJumps.</item>
/// <item>Security: Route durchquert eine nicht erlaubte Zone.</item>
/// <item>Mindestvolumen: VolumeM3 &lt; MinVolumeM3.</item>
/// <item>Mindestgewinn: EstimatedProfit &lt; MinProfit.</item>
/// <item>Qualität: Score &lt; MinQualityScore.</item>
/// </list>
/// Grenzen sind inklusiv: ein Kandidat exakt auf dem Limit besteht.
/// </remarks>
public sealed class TradeProfileFilter
{
    /// <summary>
    /// Bewertet eine Kandidaten-Opportunity gegen ein Profil.
    /// <paramref name="volumeM3"/> ist das benötigte Cargo-Volumen in m³
    /// (TradingOpportunity speichert kein Volumen; die aufrufende Stelle
    /// liefert es aus SDE/Preisanalyse). null = unbekannt.
    /// </summary>
    public TradeProfileFilterResult Evaluate(TradeProfile profile, TradingOpportunity opportunity, decimal? volumeM3 = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(opportunity);

        var reasons = new List<string>();
        var security = ParseRouteSecurity(opportunity.RouteSecurityAnalysis);
        var jumps = opportunity.JumpDistance;

        // 0) Owner-Isolation: ein fremdes Profil bewertet keinen Kandidaten —
        //    auch nicht mit identischen Grenzwerten.
        if (profile.CharacterId != opportunity.CharacterId)
            reasons.Add($"Profil (Owner {profile.CharacterId}) gehört nicht zum Kandidaten-Owner ({opportunity.CharacterId}).");

        // 1) Unbekannte erforderliche Daten blockieren: eine aktive Grenze,
        //    deren Kandidaten-Datum unbekannt ist, führt zur Ablehnung statt
        //    zu einem stillen „gültig"-Default.
        if (profile.MaxCargoVolume is not null && volumeM3 is null)
            reasons.Add("Erforderliche Daten unbekannt: Cargo-Volumen (m³).");
        if (profile.MinVolumeM3 is not null && volumeM3 is null)
            reasons.Add("Erforderliche Daten unbekannt: Cargo-Volumen (m³).");
        if (profile.MaxJumps is not null && jumps is null)
            reasons.Add("Erforderliche Daten unbekannt: Sprungdistanz.");
        if (profile.AllowHighSec == false && profile.AllowLowSec == false && profile.AllowNullSec == false)
            reasons.Add("Profilfehler: keine Security-Zone erlaubt.");
        else if (security is null && !(profile.AllowHighSec && profile.AllowLowSec && profile.AllowNullSec))
            reasons.Add("Erforderliche Daten unbekannt: Sicherheitsanalyse der Route.");

        // 2) Kapital
        if (profile.MaxCapital is { } maxCapital && opportunity.RequiredCapital > (double)maxCapital)
            reasons.Add($"Benötigtes Kapital ({opportunity.RequiredCapital:N0} ISK) überschreitet Profil-Limit ({maxCapital:N0} ISK).");

        // 3) Cargo
        if (profile.MaxCargoVolume is { } maxCargo && volumeM3 is { } volume && volume > maxCargo)
            reasons.Add($"Cargo-Volumen ({volume:N0} m³) überschreitet Profil-Limit ({maxCargo:N0} m³).");

        // 4) Sprünge
        if (profile.MaxJumps is { } maxJumps && jumps is { } jumpCount && jumpCount > maxJumps)
            reasons.Add($"Sprungdistanz ({jumpCount}) überschreitet Profil-Limit ({maxJumps}).");

        // 5) Security: Route kreuzt eine nicht erlaubte Zone (Anzahl &gt; 0).
        if (security is not null)
        {
            if (!profile.AllowHighSec && security.HighSec > 0)
                reasons.Add("Route durchquert highsec, Profil erlaubt diese Zone nicht.");
            if (!profile.AllowLowSec && security.LowSec > 0)
                reasons.Add("Route durchquert lowsec, Profil erlaubt diese Zone nicht.");
            if (!profile.AllowNullSec && security.NullSec > 0)
                reasons.Add("Route durchquert nullsec, Profil erlaubt diese Zone nicht.");
        }

        // 6) Mindestvolumen
        if (profile.MinVolumeM3 is { } minVolume && volumeM3 is { } tradeVolume && tradeVolume < minVolume)
            reasons.Add($"Volumen ({tradeVolume:N0} m³) unter Mindestvolumen ({minVolume:N0} m³).");

        // 7) Mindestgewinn
        if (profile.MinProfit is { } minProfit && opportunity.EstimatedProfit < (double)minProfit)
            reasons.Add($"Geschätzter Gewinn ({opportunity.EstimatedProfit:N0} ISK) unter Mindestgewinn ({minProfit:N0} ISK).");

        // 8) Qualität (Heuristik-Score)
        if (profile.MinQualityScore is { } minQuality && opportunity.Score < minQuality)
            reasons.Add($"Quality-Score ({opportunity.Score:F0}) unter Mindestqualität ({minQuality}).");

        return new TradeProfileFilterResult(opportunity.Id, reasons.Count == 0, reasons);
    }

    /// <summary>RouteSecurityAnalysis (JSON {"highsec":n,"lowsec":n,"nullsec":n}) interpretieren.</summary>
    internal static RouteSecurityCounts? ParseRouteSecurity(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            var parsed = JsonSerializer.Deserialize<RouteSecurityCounts>(json, RouteSecurityJsonOptions);
            return parsed is null ? null : parsed;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static readonly JsonSerializerOptions RouteSecurityJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    internal sealed class RouteSecurityCounts
    {
        [JsonPropertyName("highsec")]
        public int HighSec { get; set; }

        [JsonPropertyName("lowsec")]
        public int LowSec { get; set; }

        [JsonPropertyName("nullsec")]
        public int NullSec { get; set; }
    }
}