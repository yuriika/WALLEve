namespace WALLEve.Configuration;

/// <summary>
/// Einstellungen für die optionale zKillboard-Risikoquelle (#73).
/// Die Quelle ist optional: Ist sie deaktiviert oder nicht erreichbar,
/// bleibt die Navigation funktionsfähig und die Route wird konservativ
/// als unbekannt geführt — niemals automatisch als sicher.
/// </summary>
public class ZkillboardSettings
{
    /// <summary>Quelle aktiv (optional; false = Quelle gilt als nicht verfügbar).</summary>
    public bool Enabled { get; set; } = true;

    public string BaseUrl { get; set; } = "https://zkillboard.com";

    public string UserAgent { get; set; } = "WALLEve/1.0 (EVE Companion App)";

    /// <summary>Mindestabstand zwischen zwei HTTP-Requests an zKillboard.</summary>
    public int MinRequestIntervalMs { get; set; } = 500;

    /// <summary>Verweildauer der lokalen Verlustzahlen im Cache.</summary>
    public int CacheTtlMinutes { get; set; } = 15;

    /// <summary>Fehlschläge in Folge, ab denen der Provider als ungesund gilt.</summary>
    public int FailureThreshold { get; set; } = 2;

    /// <summary>Cooldown ohne Requests, nachdem der Provider als ungesund gilt.</summary>
    public int UnhealthyCooldownSeconds { get; set; } = 60;
}