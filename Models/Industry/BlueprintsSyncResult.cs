namespace WALLEve.Models.Industry;

/// <summary>
/// Ergebnis eines Blueprint-Sync-Laufs (#49). Success=false bedeutet: ESI
/// lieferte kein vollständiges Ergebnis (Fehler/Abbruch) — es wurde nichts
/// geschrieben, der persistierte Blueprint-Bestand bleibt unverändert.
/// </summary>
public class BlueprintsSyncResult
{
    public bool Success { get; set; }

    public int Inserted { get; set; }

    public int Updated { get; set; }

    public int Total { get; set; }

    public string? Error { get; set; }
}