namespace WALLEve.Models.Mining;

/// <summary>
/// Filter für die Mining-Auswertung (#48). Datumsgrenzen sind normalisiert:
/// <see cref="From"/> inklusiv (00:00 Uhr), <see cref="To"/> exklusiv (00:00 Uhr) —
/// ein Zeitraum 01.09. bis 16.09. enthält den 15., aber nicht den 16. September.
/// </summary>
public sealed class MiningValuationFilter
{
    /// <summary>Zeitraum-Anfang, inklusiv, auf 00:00 Uhr normalisiert.</summary>
    public DateTime From { get; init; }

    /// <summary>Zeitraum-Ende, exklusiv, auf 00:00 Uhr normalisiert.</summary>
    public DateTime To { get; init; }

    /// <summary>Marktregion für die Bewertung („Marktwechsel“); nie 0/Default.</summary>
    public int RegionId { get; init; }

    /// <summary>true = zusätzlich nach Abbau-Sonnensystem gruppieren (verfügbare Systemdimension).</summary>
    public bool GroupBySolarSystem { get; init; }
}

/// <summary>
/// Eine gruppierte Auswertungszeile (#48): Erztyp (optional je System) mit
/// kumulierter Abbaumenge und Marktbewertung in einer Marktregion.
/// Bewertung stammt immer aus einem persistierten MarketSnapshot der Region;
/// fehlende Daten bleiben sichtbar unbekannt (null), nie 0 ISK.
/// </summary>
public sealed class MiningValuationRow
{
    public int TypeId { get; init; }

    /// <summary>null = Typname nicht auflösbar (SDE fehlt) — als „Unbekannt“ erhalten.</summary>
    public string? TypeName { get; init; }

    /// <summary>null, wenn nicht nach System gruppiert wird.</summary>
    public int? SolarSystemId { get; init; }

    /// <summary>null = Systemname nicht auflösbar — als „Unbekannt“ erhalten.</summary>
    public string? SystemName { get; init; }

    /// <summary>Kumulierte Abbaumenge im Zeitraum (Aktivität, kein Bestand).</summary>
    public long Quantity { get; init; }

    /// <summary>Bester Verkaufskurs (BestSellPrice) des neuesten Snapshots in der Region; null = unbekannt.</summary>
    public double? UnitPrice { get; init; }

    /// <summary>Menge × Stückpreis; null, wenn kein Preis vorliegt.</summary>
    public double? Value { get; init; }

    /// <summary>Zeitpunkt des zugrunde liegenden Snapshots — Basis des Preisalters.</summary>
    public DateTime? QuoteTimestamp { get; init; }

    /// <summary>true, wenn ein belastbarer Preis vorliegt (HasQuote).</summary>
    public bool HasValuation => QuoteTimestamp.HasValue && UnitPrice.HasValue;
}

/// <summary>
/// Ergebnis der Mining-Auswertung (#48). Reine Lese-Auswertung des persönlichen
/// Ledgers: Sie schreibt weder Bestand noch Kostenbasis — die Aktivitätsmenge
/// ist ausdrücklich kein automatischer aktueller Bestand (AK1, doppelte Übernahme).
/// </summary>
public sealed class MiningValuationReport
{
    public IReadOnlyList<MiningValuationRow> Rows { get; init; } = Array.Empty<MiningValuationRow>();

    /// <summary>Gesamte Abbaumenge im Zeitraum.</summary>
    public long TotalQuantity { get; init; }

    /// <summary>Summe der bewerteten Zeilen; null, wenn keine Zeile bewertet werden konnte.</summary>
    public double? TotalValue { get; init; }

    /// <summary>Anzahl Zeilen mit bekannter Bewertung.</summary>
    public int KnownCount { get; init; }

    /// <summary>Anzahl Zeilen ohne belastbare Bewertung (Unbekannt).</summary>
    public int UnknownCount { get; init; }

    public int RegionId { get; init; }

    /// <summary>null = Regionname nicht auflösbar.</summary>
    public string? RegionName { get; init; }

    public DateTime From { get; init; }

    public DateTime To { get; init; }

    public bool GroupBySolarSystem { get; init; }
}