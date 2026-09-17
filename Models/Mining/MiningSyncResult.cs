namespace WALLEve.Models.Mining;

/// <summary>
/// Ergebnis eines Mining-Ledger-Syncs (#39).
/// Success=false bedeutet: ESI lieferte kein vollständiges Ergebnis
/// (Fehler/Abbruch im M0-Vertrag) — es wurden keine Daten geschrieben.
/// </summary>
public class MiningSyncResult
{
    public bool Success { get; init; }

    public int Inserted { get; init; }

    public int Updated { get; init; }

    /// <summary>Anzahl der vom ESI-Ledger gelieferten Zeilen.</summary>
    public int Total { get; init; }

    public string? Error { get; init; }
}