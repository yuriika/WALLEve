namespace WALLEve.Models.Configuration;

/// <summary>
/// Konfiguration des begrenzten Regionalscan-Messlaufs (#67), Sektion
/// „Measurement:RegionalScan“ in appsettings.json. Default: deaktiviert — der
/// Messlauf ist ein expliziter Einmallauf, kein Produktionscollector.
/// </summary>
public class RegionalScanMeasurementSettings
{
    /// <summary>Messlauf beim App-Start ausführen (nur bei true).</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Repräsentative Regionen für den Messlauf. WICHTIG: ohne Default-Initializer —
    /// ConfigurationBinder hängt an eine vorinitialisierte Array-Property an statt sie
    /// zu ersetzen (5 Default + 5 aus appsettings wären 10 = doppelter Messlauf).
    /// Der Host setzt den Fallback (vier Handelszentren + ein Randgebiet) in Program.cs,
    /// wenn die Sektion keine Regionen enthält.
    /// </summary>
    public int[] Regions { get; set; } = Array.Empty<int>();

    /// <summary>Gzip-Probe je Region (eine Zusatzanfrage; misst die komprimierte Transfergröße).</summary>
    public bool ProbeGzip { get; set; } = true;
}

/// <summary>
/// Zur Laufzeit vom Host aufgelöste Pfade/Adressen für den Messdienst (#67).
/// </summary>
public class RegionalScanMeasurementOptions
{
    public string DatabasePath { get; set; } = "";
    public string ArtifactDirectory { get; set; } = "";
    public string EsiBaseUrl { get; set; } = "";
    public bool ProbeGzip { get; set; } = true;
}