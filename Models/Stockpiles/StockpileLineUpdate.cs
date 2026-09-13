namespace WALLEve.Models.Stockpiles;

/// <summary>
/// Editier-Auftrag einer Tabellenzeile (Issue #53): neuer Zielwert, Notiz und
/// Ort/Container-Scope. Die UI erzeugt ihn nur bei valider Zielmenge (&gt; 0).
/// </summary>
public sealed record StockpileLineUpdate(long TargetId, int Quantity, string? Note);