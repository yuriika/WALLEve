namespace WALLEve.Services.Notifications;

/// <summary>
/// Eine einzelne In-App-Meldung für eine Trading-Chance (Issue #70).
/// Unveränderlich bis auf den Gelesen-Status. Die Meldung verlinkt immer auf
/// die konkrete Opportunity (<see cref="LinkUrl"/>, z.&nbsp;B. "/trading#opp-42").
/// Browser-Benachrichtigungen sind ein optionaler Seitenkanal über
/// <see cref="IBrowserNotificationService"/> — die In-App-Meldung existiert
/// unabhängig davon.
/// </summary>
public sealed class TradingNotification
{
    /// <summary>Monoton steigende ID innerhalb der Feed-Instanz (pro Charakter).</summary>
    public long Id { get; init; }

    /// <summary>Owner der Meldung (Owner-Isolation wie bei Empfehlungen, #45).</summary>
    public int CharacterId { get; init; }

    /// <summary>Opportunity, auf die die Meldung verweist.</summary>
    public int OpportunityId { get; init; }

    /// <summary>Art der Chance, z.&nbsp;B. "inventory_sell" (Anzeige/Kategorisierung).</summary>
    public string OpportunityType { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    /// <summary>Relativer Link zur Opportunity in der Web-App.</summary>
    public string LinkUrl { get; init; } = string.Empty;

    /// <summary>Zeitpunkt der Erkennung der Chance — Grundlage des Datenalters.</summary>
    public DateTime DetectedAtUtc { get; init; }

    /// <summary>true, sobald der Nutzer die Meldung gesehen/gelesen hat.</summary>
    public bool IsSeen { get; internal set; }
}

/// <summary>
/// Reine Formatierungs-Hilfe für das Datenalter einer Meldung (Akzeptanzkriterium
/// „neue Meldung zeigt Datenalter"). Bewusst statisch und ohne UI-Abhängigkeit,
/// damit die Ausgabe deterministisch testbar ist.
/// </summary>
public static class TradingNotificationFormat
{
    public static string FormatDataAge(DateTime detectedAtUtc, DateTime utcNow)
    {
        var age = utcNow - detectedAtUtc;
        if (age < TimeSpan.Zero)
            return "gerade eben";
        if (age.TotalMinutes < 1)
            return "gerade eben";
        if (age.TotalHours < 1)
            return $"vor {Math.Max(1, (int)age.TotalMinutes)} Min";
        if (age.TotalDays < 1)
            return $"vor {(int)age.TotalHours} Std";
        return $"vor {(int)age.TotalDays} Tag(en)";
    }
}