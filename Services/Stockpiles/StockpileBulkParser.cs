using WALLEve.Models.Holdings;
using WALLEve.Models.Stockpiles;

namespace WALLEve.Services.Stockpiles;

/// <summary>
/// Parst und validiert Bulk-Zielpflege-Text (Issue #53) ohne Datenbankzugriff,
/// damit die Regeln deterministisch testbar sind.
/// Format je Zeile: <c>TypeId|Itemname;Menge[;LocationId][;Notiz]</c>
/// - Leere Zeilen und Zeilen ab '#' werden ignoriert.
/// - Menge muss eine ganze Zahl größer als 0 sein (Service-Regel #36).
/// - LocationId optional, muss eine positive Zahl sein.
/// - Notiz höchstens 500 Zeichen (Modell-[MaxLength]).
/// - Doppelte aktive Ziele (gleicher Type, gleicher Owner) werden abgelehnt,
///   passend zum Duplikat-Guard des StockpileService (#36).
/// Fehler tragen die Zeilennummer; gültige Einträge für den Speichervorgang.
/// </summary>
public static class StockpileBulkParser
{
    private const int Separators = 4;

    /// <summary>
    /// Parst den Text. <paramref name="resolveTypeByName"/> löst einen Namen
    /// (erste Spalte, nicht numerisch) in eine TypeId auf — die UI reicht die
    /// SDE-Itemsuche durch, Tests verwenden eine feste Tabelle.
    /// <paramref name="existingTargets"/> sind die aktiven Ziele desselben
    /// Owners; sie werden für den Duplikat-Check herangezogen.
    /// </summary>
    public static StockpileBulkParseResult Parse(
        string? text,
        Func<string, int?>? resolveTypeByName = null,
        IReadOnlyCollection<StockpileTarget>? existingTargets = null,
        OwnerType? ownerType = null,
        int? ownerId = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new StockpileBulkParseResult();
        }

        var entries = new List<StockpileBulkEntry>();
        var errors = new List<StockpileBulkError>();
        var seen = new HashSet<(int TypeId, long? LocationId)>();

        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var raw = lines[i].Trim();
            if (raw.Length == 0 || raw.StartsWith('#'))
            {
                continue;
            }

            var lineNumber = i + 1;
            var fields = raw.Split(';', StringSplitOptions.TrimEntries);

            int? typeId = null;
            long? locationId = null;
            string? note = null;
            var ok = true;

            if (fields.Length < 2)
            {
                errors.Add(new StockpileBulkError
                {
                    LineNumber = lineNumber,
                    Line = raw,
                    Message = "Zeile muss mindestens <Item>;<Menge> enthalten."
                });
                continue;
            }

            // Spalte 1: TypeId (numerisch) oder Itemname (aufgelöst über die Itemsuche).
            if (int.TryParse(fields[0], out var parsedTypeId) && parsedTypeId > 0)
            {
                typeId = parsedTypeId;
            }
            else if (resolveTypeByName is not null)
            {
                typeId = resolveTypeByName(fields[0]);
                if (typeId is null)
                {
                    errors.Add(new StockpileBulkError
                    {
                        LineNumber = lineNumber,
                        Line = raw,
                        Message = $"Unbekanntes Item: \"{fields[0]}\"."
                    });
                    ok = false;
                }
            }
            else
            {
                errors.Add(new StockpileBulkError
                {
                    LineNumber = lineNumber,
                    Line = raw,
                    Message = $"\"{fields[0]}\" ist weder eine TypeId noch auflösbar."
                });
                ok = false;
            }

            // Spalte 2: Menge (> 0, ganzzahlig).
            if (!int.TryParse(fields[1], out var quantity) || quantity <= 0)
            {
                errors.Add(new StockpileBulkError
                {
                    LineNumber = lineNumber,
                    Line = raw,
                    Message = "Zielmenge muss eine ganze Zahl größer als 0 sein."
                });
                ok = false;
            }

            // Spalte 3 (optional): LocationId.
            if (fields.Length > 2 && fields[2].Length > 0)
            {
                if (!long.TryParse(fields[2], out var parsedLocation) || parsedLocation <= 0)
                {
                    errors.Add(new StockpileBulkError
                    {
                        LineNumber = lineNumber,
                        Line = raw,
                        Message = "Ort/Container muss eine positive Id sein (leer lassen für den Gesamtbestand)."
                    });
                    ok = false;
                }
                else
                {
                    locationId = parsedLocation;
                }
            }

            // Spalte 4 (optional): Notiz.
            if (fields.Length > 3 && fields[3].Length > 0)
            {
                note = fields[3];
                if (note.Length > 500)
                {
                    errors.Add(new StockpileBulkError
                    {
                        LineNumber = lineNumber,
                        Line = raw,
                        Message = "Notiz darf höchstens 500 Zeichen enthalten."
                    });
                    ok = false;
                }
            }

            if (fields.Length > Separators)
            {
                errors.Add(new StockpileBulkError
                {
                    LineNumber = lineNumber,
                    Line = raw,
                    Message = "Zeile enthält zu viele Felder (erwartet: <Item>;<Menge>[;Ort][;Notiz])."
                });
                ok = false;
            }

            if (typeId is null || quantity <= 0)
            {
                continue;
            }

            // Duplikat im Eingabetext (gleicher Type + Scope).
            if (!seen.Add((typeId.Value, locationId)))
            {
                errors.Add(new StockpileBulkError
                {
                    LineNumber = lineNumber,
                    Line = raw,
                    Message = $"Ziel für Type {typeId.Value} ist im Text doppelt vorhanden."
                });
                ok = false;
            }

            // Duplikat gegen bestehende aktive Ziele des Owners (Guard #36).
            // Der Owner-Filter ist Pflicht-Semantik: Ziele ANDERER Owner gelten
            // nie als Konflikt (Owner-Wechsel zeigt keine fremden Ziele).
            if (existingTargets is not null
                && existingTargets.Any(t => (!ownerType.HasValue || t.OwnerType == ownerType.Value)
                                            && (!ownerId.HasValue || t.OwnerId == ownerId.Value)
                                            && t.TypeId == typeId.Value
                                            && !t.IsArchived))
            {
                errors.Add(new StockpileBulkError
                {
                    LineNumber = lineNumber,
                    Line = raw,
                    Message = $"Für Type {typeId.Value} existiert bereits ein aktives Ziel dieses Owners."
                });
                ok = false;
            }

            if (!ok)
            {
                continue;
            }

            entries.Add(new StockpileBulkEntry
            {
                LineNumber = lineNumber,
                TypeId = typeId.Value,
                Quantity = quantity,
                LocationId = locationId,
                Note = note
            });
        }

        return new StockpileBulkParseResult
        {
            Entries = entries,
            Errors = errors
        };
    }
}