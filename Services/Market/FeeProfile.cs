using WALLEve.Models.Market;

namespace WALLEve.Services.Market;

/// <summary>
/// Effektive Gebühren-Eingaben mit Herkunft je Eingabe (Issue #46).
/// Erlaubt der Analyse, nachvollziehbar zu machen, ob ein Ergebnis auf
/// echten ESI-Skills, einem manuellen Override oder konservativen Schätzungen
/// beruht — und blockiert, wenn eine notwendige Eingabe unbekannt ist.
/// </summary>
public class FeeProfile
{
    /// <summary>Effektiver Broker-Fee-Satz (Dezimal, z. B. 0.015 = 1,5 %).</summary>
    public double BrokerFeeRate { get; set; }

    /// <summary>Effektiver Sales-Tax-Satz (Dezimal).</summary>
    public double SalesTaxRate { get; set; }

    /// <summary>Relist-Discount (Dezimal) für die Modify-Fee.</summary>
    public double RelistDiscountRate { get; set; }

    public FeeInputOrigin BrokerRateOrigin { get; set; }

    public FeeInputOrigin SalesTaxOrigin { get; set; }

    /// <summary>
    /// Herkunft des Standing-Anteils der Broker-Fee. Standings liegen über ESI
    /// aktuell nicht belegbar vor (Scope angefragt, Endpoint nicht implementiert) —
    /// der Anteil ist deshalb ohne manuellen Override nur eine konservative
    /// Schätzung (0), niemals ein stiller garantiert korrekter Wert.
    /// </summary>
    public FeeInputOrigin StandingsOrigin { get; set; }

    /// <summary>Zeitpunkt der Ermittlung dieser Gebühren-Eingaben (UTC).</summary>
    public DateTime EvaluatedAtUtc { get; set; }

    /// <summary>
    /// true = alle notwendigen Eingaben sind exakt belegt (Automatic oder
    /// ManualOverride). false = mindestens eine Eingabe ist Estimated
    /// (konservative, begrenzte Spanne) oder Unknown.
    /// </summary>
    public bool IsPrecise =>
        BrokerRateOrigin is FeeInputOrigin.Automatic or FeeInputOrigin.ManualOverride
        && SalesTaxOrigin is FeeInputOrigin.Automatic or FeeInputOrigin.ManualOverride;

    /// <summary>
    /// true = eine notwendige Eingabe ist Unknown — eine präzise actionable
    /// Berechnung ist blockiert (Issue #46, Akzeptanzkriterium 3).
    /// </summary>
    public bool HasUnknownInput =>
        BrokerRateOrigin == FeeInputOrigin.Unknown
        || SalesTaxOrigin == FeeInputOrigin.Unknown
        || StandingsOrigin == FeeInputOrigin.Unknown;
}

/// <summary>
/// Manuelle Overrides für die Gebührenberechnung (Konfiguration, Sektion
/// "FeeCalculator"). Jeder gesetzte Override ersetzt die automatische
/// Ermittlung und wird als FeeInputOrigin.ManualOverride nachvollziehbar.
/// Die Standard-Abschnitte werden NICHT überschrieben, solange die Felder
/// fehlen — die App bleibt dann bei der konservativen Schätzung.
/// </summary>
public class FeeOverrideSettings
{
    /// <summary>Manueller Broker-Fee-Satz (Dezimal, 0..1). Ersetzt Skill-Berechnung inkl. Standing-Anteil.</summary>
    public double? BrokerFeeRate { get; set; }

    /// <summary>Manueller Sales-Tax-Satz (Dezimal, 0..1). Ersetzt die Accounting-Skill-Berechnung.</summary>
    public double? SalesTaxRate { get; set; }

    /// <summary>
    /// Manueller Standing-Rabatt auf die Broker-Fee (Dezimal, 0..1), z. B.
    /// 0.0003 je Faction-Standing-Punkt. Ohne Override bleibt der Standing-Anteil
    /// eine konservative Schätzung (0) — Standings sind über ESI nicht belegbar.
    /// </summary>
    public double? StandingsDiscount { get; set; }
}