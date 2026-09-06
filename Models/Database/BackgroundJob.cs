namespace WALLEve.Models.Database;

/// <summary>Lebenszyklus-Status eines Hintergrund-Tasks.</summary>
public enum BackgroundJobStatus
{
    /// <summary>Task läuft gerade.</summary>
    Running = 0,
    /// <summary>Vom Nutzer pausiert.</summary>
    Paused = 1,
    /// <summary>Durch App-Neustart unterbrochen (kann fortgesetzt werden).</summary>
    Interrupted = 2,
    /// <summary>Erfolgreich abgeschlossen.</summary>
    Completed = 3,
    /// <summary>Mit Fehler beendet.</summary>
    Failed = 4
}

/// <summary>
/// Persistierter Zustand eines Hintergrund-Tasks.
/// Überlebt App-Neustarts: Current/Total erlauben das Fortsetzen an der
/// abgebrochenen Stelle (idempotent, da Fortschritt in der DB steht).
/// </summary>
public class BackgroundJob
{
    public long Id { get; set; }

    /// <summary>Maschinenlesbarer Task-Typ (z.B. "CostBasisCollector").</summary>
    public string JobType { get; set; } = string.Empty;

    /// <summary>Anzeigename für die Settings-Übersicht.</summary>
    public string DisplayName { get; set; } = string.Empty;

    public int? CharacterId { get; set; }

    public BackgroundJobStatus Status { get; set; }

    /// <summary>Abgearbeitete Einheiten (z.B. Items oder Seiten).</summary>
    public int Current { get; set; }

    /// <summary>Gesamtzahl der Einheiten (0 = unbekannt/unbegrenzt).</summary>
    public int Total { get; set; }

    /// <summary>Zusätzliche, task-spezifische Parameter (z.B. Schätzregion) als JSON.</summary>
    public string? ParametersJson { get; set; }

    /// <summary>Letzte Fehlermeldung, falls vorhanden.</summary>
    public string? LastError { get; set; }

    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}