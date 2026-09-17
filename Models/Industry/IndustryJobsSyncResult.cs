namespace WALLEve.Models.Industry;

/// <summary>
/// Ergebnis einer Industrie-Job-Synchronisation (#40).
/// Success=false bedeutet: ESI lieferte kein vollständiges Ergebnis
/// (Fehler/Abbruch im M0-Vertrag) — es wurden keine Daten geschrieben.
/// </summary>
public class IndustryJobsSyncResult
{
    public bool Success { get; init; }

    public int Inserted { get; init; }

    /// <summary>Anzahl der Jobs, deren Statusfelder geändert wurden.</summary>
    public int Updated { get; init; }

    /// <summary>Anzahl der vom ESI-Endpunkt gelieferten Jobs.</summary>
    public int Total { get; init; }

    public string? Error { get; init; }
}