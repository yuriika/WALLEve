namespace WALLEve.Models.Trading;

/// <summary>
/// Validierbares Handelsprofil eines Charakters (Issue #44): harte Grenzen,
/// die VOR jeder Bewertung/Rankings auf eine Kandidaten-Opportunity angewendet
/// werden. Null-Werte bedeuten „Grenze nicht aktiv". Genau ein Profil je
/// Charakter (Unique-Index auf <see cref="CharacterId"/>); Profilwerte sind
/// isoliert pro Owner und beeinflussen niemals andere Charaktere.
/// </summary>
/// <remarks>
/// Die Wertegültigkeit (Bereiche, Pflichtfelder) erzwingt der
/// TradeProfileService beim Speichern; die Persistenz selbst ist eine
/// additive Tabelle ohne Eingriff in bestehende Daten.
/// </remarks>
public class TradeProfile
{
    public int Id { get; set; }

    /// <summary>Owner (Charakter), dem dieses Profil gehört.</summary>
    public int CharacterId { get; set; }

    /// <summary>Anzeigename des Profils.</summary>
    public string Name { get; set; } = string.Empty;

    public DateTime UpdatedAt { get; set; }

    // --- harte Grenzen (null = nicht aktiv) ---

    /// <summary>Maximal benötigtes Kapital in ISK (RequiredCapital).</summary>
    public decimal? MaxCapital { get; set; }

    /// <summary>Maximales Cargo-Volumen in m³ (Trade muss hineinpassen).</summary>
    public decimal? MaxCargoVolume { get; set; }

    /// <summary>Maximale Sprungdistanz (JumpDistance bzw. JumpCount).</summary>
    public int? MaxJumps { get; set; }

    /// <summary>Erlaubte Security-Zonen der Route (highsec/lowsec/nullsec).</summary>
    public bool AllowHighSec { get; set; }

    /// <summary>Erlaubte Security-Zonen der Route (highsec/lowsec/nullsec).</summary>
    public bool AllowLowSec { get; set; }

    /// <summary>Erlaubte Security-Zonen der Route (highsec/lowsec/nullsec).</summary>
    public bool AllowNullSec { get; set; }

    /// <summary>Mindestvolumen des Trades in m³.</summary>
    public decimal? MinVolumeM3 { get; set; }

    /// <summary>Mindest-Nettogewinn in ISK (EstimatedProfit).</summary>
    public decimal? MinProfit { get; set; }

    /// <summary>Mindest-Qualitäts-Score (Heuristik-Score 0-100).</summary>
    public int? MinQualityScore { get; set; }

    // --- Signal-Zustand (Issue #68) ---

    /// <summary>
    /// Cooldown in Minuten: nach der letzten gemeldeten Signal-Chance bleibt ein
    /// erneuter Signal-Report so lange gesperrt. 0 = kein Cooldown (jede materiell
    /// neue Chance wird sofort gemeldet). Grenze ist inklusiv: exakt zum Ablauf des
    /// Cooldowns ist ein neues Signal wieder erlaubt.
    /// </summary>
    public int CooldownMinutes { get; set; }

    /// <summary>
    /// Manuelle Deaktivierung durch den Owner: solange gesetzt, wird aus diesem
    /// Profil kein neues Trading-Signal gemeldet — unabhängig von Kandidaten,
    /// Fingerprint oder Cooldown.
    /// </summary>
    public bool SignalDeactivated { get; set; }

    /// <summary>
    /// Deterministischer Fingerprint der zuletzt gemeldeten Kandidaten
    /// (Issue #68). Ein identischer Fingerprint nach Neustart oder Wiederholung
    /// erzeugt bewusst KEIN neues Signal — der Meldungssturm wird verhindert,
    /// ohne dass der Report-Zustand flüchtig (nur im RAM) ist.
    /// </summary>
    public string? LastReportedFingerprint { get; set; }

    /// <summary>UTC-Zeitpunkt der letzten Signal-Meldung; Basis für den Cooldown.</summary>
    public DateTime? LastReportedAt { get; set; }
}