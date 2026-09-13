namespace WALLEve.Models.Stockpiles;

/// <summary>Eine gültig geparste Bulk-Zeile (Issue #53).</summary>
public sealed record StockpileBulkEntry
{
    /// <summary>Zeilennummer (1-basiert) im Eingabetext.</summary>
    public int LineNumber { get; init; }

    public int TypeId { get; init; }

    /// <summary>Zielmenge; immer größer als 0 (Service-Regel #36).</summary>
    public int Quantity { get; init; }

    /// <summary>Optionaler Ort-/Container-Scope.</summary>
    public long? LocationId { get; init; }

    /// <summary>Freitext-Notiz (max. 500 Zeichen wie im Modell).</summary>
    public string? Note { get; init; }
}

/// <summary>Ein Validierungsfehler einer Bulk-Zeile.</summary>
public sealed record StockpileBulkError
{
    /// <summary>Zeilennummer (1-basiert) oder 0 für globale Fehler.</summary>
    public int LineNumber { get; init; }

    /// <summary>Ursprungstext der fehlerhaften Zeile.</summary>
    public string Line { get; init; } = string.Empty;

    /// <summary>Deutsche Fehlermeldung.</summary>
    public string Message { get; init; } = string.Empty;
}

/// <summary>Ergebnis des Bulk-Parsings: gültige Einträge plus Fehler je Zeile.</summary>
public sealed class StockpileBulkParseResult
{
    public IReadOnlyList<StockpileBulkEntry> Entries { get; init; } = Array.Empty<StockpileBulkEntry>();
    public IReadOnlyList<StockpileBulkError> Errors { get; init; } = Array.Empty<StockpileBulkError>();

    public bool HasErrors => Errors.Count > 0;
}