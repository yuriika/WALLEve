namespace WALLEve.Services.Risk.Interfaces;

/// <summary>Ergebnis einer zKillboard-Verlustabfrage für ein System.</summary>
public sealed class ZkillboardLosses
{
    public int SystemId { get; init; }

    public int Losses { get; init; }

    /// <summary>Zeitpunkt der Erhebung (Alter der Daten).</summary>
    public DateTimeOffset CollectedAt { get; init; }

    /// <summary>zKillboard kann verzögert liefern; der Hinweis bleibt sichtbar.</summary>
    public bool IsDelayed { get; init; }
}

/// <summary>
/// Optionaler zKillboard-Adapter (#73): User-Agent, Compression, lokaler Cache,
/// Request-Abstand und Provider-Health. null (Rückgabe) bedeutet: Quelle nicht
/// verfügbar (Rate-Limit, Timeout, fehlende Daten, Cooldown oder deaktiviert).
/// Der Adapter wirft nie — Fehler landen in Log und Provider-Health.
/// </summary>
public interface IZkillboardClient
{
    /// <summary>true, solange die Quelle in den Einstellungen aktiv ist.</summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Liefert die Verlustanzahl eines Systems oder null, wenn die Quelle
    /// nicht verfügbar ist. Liefert 0 bei einer valide leeren Antwort
    /// (keine Verluste geführt) — das ist ein echtes Datum, kein Fehler.
    /// </summary>
    Task<ZkillboardLosses?> GetLossesAsync(int systemId, CancellationToken ct = default);
}