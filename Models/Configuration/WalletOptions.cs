namespace WALLEve.Models.Configuration;

/// <summary>
/// Konfiguration für Wallet-Service Features
/// </summary>
public class WalletOptions
{
    /// <summary>
    /// Dateiname für die Wallet-Datenbank
    /// Standard: wallet.db
    /// </summary>
    public string LocalFileName { get; set; } = "wallet.db";

    /// <summary>
    /// Zeitfenster in Sekunden für Tax-Linking Heuristik
    /// Standard: 60 Sekunden (Transaktionen und Steuern können bis zu 60 Sek. auseinander liegen)
    /// </summary>
    public int TaxLinkingTimeWindowSeconds { get; set; } = 60;

    /// <summary>
    /// Minimale Steuerrate (mit maximalen Accounting Skills)
    /// Standard: 0.02 (2%)
    /// </summary>
    public double TaxMinPercentage { get; set; } = 0.02;

    /// <summary>
    /// Maximale Steuerrate (ohne Skills)
    /// Standard: 0.08 (8%)
    /// </summary>
    public double TaxMaxPercentage { get; set; } = 0.08;

    /// <summary>
    /// Toleranz für Tax-Matching (z.B. 0.10 = 10% Abweichung erlaubt)
    /// Standard: 0.10 (10%)
    /// </summary>
    public double TaxTolerancePercentage { get; set; } = 0.10;

    /// <summary>
    /// Aktiviert skill-basierte Tax-Berechnung (nutzt Accounting Skill)
    /// Standard: false (nutzt Min/Max Tax Range)
    /// </summary>
    public bool UseSkillBasedTaxCalculation { get; set; } = false;
}

