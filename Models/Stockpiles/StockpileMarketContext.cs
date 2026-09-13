namespace WALLEve.Models.Stockpiles;

/// <summary>
/// Markt-Quote eines Stockpile-Items am konfigurierten Vergleichsmarkt (Issue #59).
/// Die Quelle ist ein persistierter MarketSnapshot der Vergleichsmarkt-Region;
/// fehlende/veraltete Snapshot-Daten werden NIE als Kurs 0 oder „gerade eben"
/// präsentiert, sondern als unbekannte Bewertung ("—").
/// </summary>
public sealed class StockpileMarketQuote
{
    /// <summary>EVE-TypeId des Items.</summary>
    public int TypeId { get; init; }

    /// <summary>Bester Verkaufskurs (Sell) im Vergleichsmarkt — was eine Beschaffung kosten würde.</summary>
    public double? BestSellPrice { get; init; }

    /// <summary>Bester Kaufkurs (Buy) im Vergleichsmarkt — was ein Verkauf dort brächte.</summary>
    public double? BestBuyPrice { get; init; }

    /// <summary>Zeitpunkt des zugrunde liegenden Snapshot — Grundlage des Preisalters.</summary>
    public DateTime? QuoteTimestamp { get; init; }

    /// <summary>true, wenn ein Snapshot im Vergleichsmarkt vorliegt (Bewertung bekannt).</summary>
    public bool HasQuote => QuoteTimestamp.HasValue && (BestSellPrice.HasValue || BestBuyPrice.HasValue);
}

/// <summary>
/// Vergleichsmarkt- und Hub-Kontext für die Fehlmengenliste (Issue #59).
/// Name/Region des Vergleichsmarkts sowie pro auflösbarem Ziel-Ort der
/// nächstgelegene aktive Hub mit exakter Sprungdistanz. Kein konfigurierter
/// Vergleichsmarkt, kein Graph und kein auflösbares System sind kein
/// Null-Fall: Sie bleiben sichtbar unbekannt.
/// </summary>
public sealed class StockpileMarketContext
{
    /// <summary>Anzeigename des Vergleichsmarkts; null = keiner konfiguriert.</summary>
    public string? ComparisonMarketName { get; init; }

    /// <summary>Region des Vergleichsmarkts; null = keiner konfiguriert.</summary>
    public int? ComparisonMarketRegionId { get; init; }

    /// <summary>Quotes je TypeId (nur für Shortage-Relevante Items gefüllt).</summary>
    public IReadOnlyDictionary<int, StockpileMarketQuote> Quotes { get; init; }
        = new Dictionary<int, StockpileMarketQuote>();

    /// <summary>Nächster aktiver Hub je auflösbarer Ziel-Location (System).</summary>
    public IReadOnlyDictionary<long, StockpileLocationHub> HubsByLocation { get; init; }
        = new Dictionary<long, StockpileLocationHub>();

    /// <summary>true, wenn ein Vergleichsmarkt konfiguriert ist.</summary>
    public bool HasComparisonMarket => ComparisonMarketRegionId.HasValue;
}

/// <summary>
/// Hub-Ergebnis für eine Ziel-Location (Issue #59). Nur aufgelöste Systeme
/// erhalten einen Eintrag; Container/Strukturen ohne System bleiben unbekannt
/// und werden in der UI als solche angezeigt.
/// </summary>
public sealed class StockpileLocationHub
{
    /// <summary>Name des nächstgelegenen aktiven Hubs.</summary>
    public string? HubName { get; init; }

    /// <summary>Exakte ungewichtete Sprungdistanz; null = nicht erreichbar.</summary>
    public int? JumpDistance { get; init; }

    /// <summary>true, wenn das System der Location aufgelöst werden konnte.</summary>
    public bool SystemResolved { get; init; }

    /// <summary>true, wenn der SDE-Graph verfügbar war (Hub-Auswahl belastbar).</summary>
    public bool GraphAvailable { get; init; }

    /// <summary>Bewertungstext für die UI: bekannte Distanz, oder konkreter Grund.</summary>
    public string Describe() => !SystemResolved
        ? "Ort/System unbekannt"
        : !GraphAvailable
            ? "Systemkarte nicht verfügbar"
            : HubName is null
                ? "kein aktiver Hub konfiguriert"
                : JumpDistance.HasValue
                    ? $"{HubName} ({JumpDistance.Value} Sprünge)"
                    : $"{HubName} (nicht erreichbar)";
}