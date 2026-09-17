using WALLEve.Models.Industry;

namespace WALLEve.Components.Industry;

/// <summary>
/// Vollständigkeitszustand eines Industrie-Abschnitts für die Seite (#55-Akzeptanzkriterium
/// „Stale/partial/leer getrennt“). Partial ist der Zustand nach einem fehlgeschlagenen oder
/// abgebrochenen Sync (M0-Vertrag: Success=false, der alte Snapshot bleibt stehen) — der
/// angezeigte Bestand ist dann ausdrücklich NICHT als vollständig bestätigt, unabhängig vom
/// Alter, und auch ein leeres Ergebnis kein gültiges „leer“. None ist gültig leer (noch kein
/// Bestand oder erfolgreicher Sync ohne Daten).
/// </summary>
public enum IndustrySyncState
{
    None,
    Complete,
    Stale,
    Partial
}

/// <summary>
/// Reine Darstellungs-Logik der Industrie-Seite (#55): BPO/BPC-Semantik,
/// Run-/ME/TE-/Alter-Formatierung und Jobstatus-Klassifikation.
/// Bewusst ohne Blazor-/EF-Abhängigkeit, damit die Akzeptanzkriterien
/// (BPO/BPC/ME/TE/Runs korrekt dargestellt, Stale/leer getrennt)
/// deterministisch testbar sind.
/// </summary>
public static class IndustryDisplay
{
    public const string BpoLabel = "BPO (Original)";
    public const string BpcLabel = "BPC (Kopie)";

    /// <summary>BPO/BPC-Status aus dem booleschen ESI-Kopierstatus (#49).</summary>
    public static string FormatBlueprintKind(bool isBlueprintCopy)
        => isBlueprintCopy ? BpcLabel : BpoLabel;

    /// <summary>
    /// Runs-Darstellung: Der BPO-Sentinel (Runs = -1) wird nie als
    /// "minus 1 Run" angezeigt, sondern als unbefristetes Original.
    /// </summary>
    public static string FormatRuns(BlueprintEntry blueprint)
        => blueprint.IsBlueprintCopy
            ? $"{blueprint.Runs} Run{(blueprint.Runs == 1 ? "" : "s")}"
            : "∞ (unbefristet)";

    /// <summary>Material- und Zeiteffizienz als Qualitätskennzahlen des Blueprints.</summary>
    public static string FormatMeTe(BlueprintEntry blueprint)
        => $"ME {blueprint.MaterialEfficiency} / TE {blueprint.TimeEfficiency}";

    /// <summary>Alter eines Synchronisationsstands (UpdatedAt) als deutsche Kurzform.</summary>
    public static string FormatSyncAge(DateTime updatedAtUtc, DateTime nowUtc)
    {
        var age = nowUtc - updatedAtUtc;
        if (age < TimeSpan.Zero) age = TimeSpan.Zero;
        return age switch
        {
            var a when a.TotalMinutes < 60 => $"vor {(int)a.TotalMinutes} min",
            var a when a.TotalHours < 24 => $"vor {(int)a.TotalHours} Std.",
            var a when a.TotalDays < 30 => $"vor {(int)a.TotalDays} Tagen",
            _ => $"vor {(int)(age.TotalDays / 30)} Monaten"
        };
    }

    /// <summary>true, wenn der Sync-Stand älter als die Schwelle ist (Stale-Markierung).</summary>
    public static bool IsStale(DateTime updatedAtUtc, DateTime nowUtc, TimeSpan threshold)
        => nowUtc - updatedAtUtc > threshold;

    /// <summary>
    /// Leitet den Vollständigkeitszustand eines Abschnitts aus der letzten Synchronisation ab.
    /// Ein fehlgeschlagener Sync gewinnt immer (Partial): Weder ein vorhandener Snapshot noch
    /// ein leeres Ergebnis darf nach einem Fehler/Abbruch als vollständig oder gültig leer gelten.
    /// Danach entscheidet der Bestand (None bei leer) und das Alter (Stale über der Schwelle).
    /// </summary>
    public static IndustrySyncState ComputeSyncState(
        bool hasData,
        bool? lastSyncSucceeded,
        DateTime? lastSyncAtUtc,
        DateTime nowUtc,
        TimeSpan staleThreshold)
    {
        if (lastSyncSucceeded == false)
        {
            return IndustrySyncState.Partial;
        }

        if (!hasData)
        {
            return IndustrySyncState.None;
        }

        if (lastSyncAtUtc is { } at && IsStale(at, nowUtc, staleThreshold))
        {
            return IndustrySyncState.Stale;
        }

        return IndustrySyncState.Complete;
    }

    /// <summary>ESI-Status → deutscher Anzeigetext; unbekannte Status bleiben sichtbar.</summary>
    public static string MapJobStatus(string esiStatus)
        => esiStatus switch
        {
            "active" => "Aktiv",
            "paused" => "Pausiert",
            "ready" => "Bereit",
            "delivered" => "Geliefert",
            "finished" => "Fertiggestellt",
            "cancelled" => "Storniert",
            "rejected" => "Abgelehnt",
            _ => $"Unbekannt ({esiStatus})"
        };

    /// <summary>
    /// Laufende (aktive/pausierte/bereite) Jobs gehören zur "aktiven" Gruppe,
    /// alle Abschluss-Status zur historischen Gruppe.
    /// </summary>
    public static bool IsActiveJobStatus(string esiStatus)
        => esiStatus is "active" or "paused" or "ready";

    /// <summary>Fertigstellungszeit: abgeschlossener Zeitpunkt, sonst geplantes Ende (UTC).</summary>
    public static DateTime GetJobEndTime(IndustryJobEntry job)
        => job.CompletedDate ?? job.EndDate;

    /// <summary>
    /// Character-Scope (#55-Akzeptanzkriterium "keine Aktivitätsdaten anderer
    /// Charaktere"): Seitenabfragen filtern IMMER auf den angemeldeten Owner.
    /// Als eigene Methode, damit der Filter deterministisch testbar bleibt
    /// und die Seite ihn nicht versehentlich weglässt.
    /// </summary>
    public static IQueryable<IndustryJobEntry> JobsForCharacter(IQueryable<IndustryJobEntry> source, int characterId)
        => source.Where(job => job.CharacterId == characterId);

    /// <inheritdoc cref="JobsForCharacter"/>
    public static IQueryable<BlueprintEntry> BlueprintsForCharacter(IQueryable<BlueprintEntry> source, int characterId)
        => source.Where(blueprint => blueprint.CharacterId == characterId);
}