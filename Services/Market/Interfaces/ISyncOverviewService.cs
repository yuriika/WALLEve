using WALLEve.Models.Database;

namespace WALLEve.Services.Market.Interfaces;

/// <summary>Ein Hintergrund-Sync des Charakters für die Übersicht.</summary>
public class CharacterSyncInfo
{
    public string JobType { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>Was macht der Sync, wofür ist er da?</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Welchen Zeitraum/Datenumfang deckt ein Lauf ab?</summary>
    public string Coverage { get; set; } = string.Empty;

    /// <summary>Wie oft läuft er (automatisch/manuell, Intervall)?</summary>
    public string Frequency { get; set; } = string.Empty;

    /// <summary>Letzter erfolgreicher Abschluss (CompletedAt).</summary>
    public DateTime? LastCompletedAt { get; set; }

    /// <summary>Fortschritt des letzten Abschlusses (wie viele Items/Einheiten).</summary>
    public int? LastCurrent { get; set; }
    public int? LastTotal { get; set; }

    /// <summary>"Running"/"Paused"/"Interrupted", falls gerade aktiv; sonst null (= bereit).</summary>
    public BackgroundJobStatus? ActiveStatus { get; set; }

    /// <summary>Fortschritt des laufenden Jobs (falls aktiv).</summary>
    public int? ActiveCurrent { get; set; }
    public int? ActiveTotal { get; set; }

    /// <summary>Ob dieser Sync manuell ausgelöst werden kann („Jetzt ausführen").</summary>
    public bool CanTrigger { get; set; }

    /// <summary>Kurzer Hinweis, falls er nicht manuell auslösbar ist.</summary>
    public string? TriggerHint { get; set; }
}

public interface ISyncOverviewService
{
    /// <summary>
    /// Liefert alle bekannten Hintergrund-Syncs mit Beschreibung und der letzten
    /// Ausführung für den Charakter. Neue Syncs hier/oben in den Definitionen eintragen.
    /// </summary>
    Task<List<CharacterSyncInfo>> GetSyncOverviewAsync(int characterId);
}