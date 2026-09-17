namespace WALLEve.Models.Industry;

/// <summary>
/// Persistente Zeile eines Character-Industriejobs (#40).
/// Additiv: pro (CharacterId, JobId) existiert genau eine Zeile; eine erneute
/// Synchronisation aktualisiert nur die mutablen Statusfelder (Status, Termine,
/// Cost, SuccessfulRuns) und löscht nie Zeilen — Jobs, die das ESI-Fenster
/// (~90 Tage) verlassen haben, bleiben als Historie erhalten.
/// Owner-/Ortskontext wird je Job vollständig gespiegelt.
/// </summary>
public class IndustryJobEntry
{
    public long Id { get; set; }

    /// <summary>Owner: der Character, dem der Job gehört.</summary>
    public int CharacterId { get; set; }

    /// <summary>ESI-Schlüssel: eindeutige Job-Id je Character.</summary>
    public int JobId { get; set; }

    /// <summary>ESI activity_id (z. B. 1 = Manufacturing).</summary>
    public int ActivityId { get; set; }

    public long BlueprintId { get; set; }

    public int BlueprintTypeId { get; set; }

    /// <summary>Lager-Ort des Blueprints (ESI station/structure_id).</summary>
    public long BlueprintLocationId { get; set; }

    /// <summary>Lager-Ort des Produkts/Lieferorts.</summary>
    public long OutputLocationId { get; set; }

    public long FacilityId { get; set; }

    public int StationId { get; set; }

    /// <summary>null bei Jobs ohne Produkt (z. B. Reprocessing ohne Output).</summary>
    public int? ProductTypeId { get; set; }

    /// <summary>ESI-Status: active/cancelled/delivered/finished/paused/ready/rejected.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Start der Job-Laufzeit (UTC).</summary>
    public DateTime StartDate { get; set; }

    /// <summary>Geplantes/endgültiges Ende der Job-Laufzeit (UTC).</summary>
    public DateTime EndDate { get; set; }

    /// <summary>Abschlusszeitpunkt (UTC); null, solange der Job nicht abgeschlossen ist.</summary>
    public DateTime? CompletedDate { get; set; }

    /// <summary>Pausezeitpunkt (UTC); null, solange der Job nicht pausiert ist.</summary>
    public DateTime? PauseDate { get; set; }

    public int Runs { get; set; }

    public int? LicensedRuns { get; set; }

    public int? SuccessfulRuns { get; set; }

    public double? Cost { get; set; }

    public int Duration { get; set; }

    public int InstallerId { get; set; }

    public DateTime UpdatedAt { get; set; }
}