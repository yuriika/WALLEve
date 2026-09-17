namespace WALLEve.Models.Industry;

/// <summary>
/// Eine Material-Bedarfszeile mit Marktpreis für die Baukosten-Ansicht (#65).
/// Der Preis stammt aus dem persistierten Markt-Snapshot des Vergleichsmarkts;
/// fehlt er, bleibt die Zeile sichtbar unbekannt (nie 0 ISK).
/// </summary>
public sealed record MaterialCostLine
{
    /// <summary>EVE-TypeId des Materials.</summary>
    public required int MaterialTypeId { get; init; }

    /// <summary>ME-bereinigte Endmenge für den kompletten Job (ManufacturingMath).</summary>
    public required long RequiredQuantity { get; init; }

    /// <summary>Bester Verkaufskurs je Einheit im Vergleichsmarkt; null = unbekannt.</summary>
    public decimal? UnitPrice { get; init; }

    /// <summary>Zeilensumme (Menge × Kurs); null = unbekannt (nie 0).</summary>
    public decimal? LineCost => UnitPrice.HasValue ? RequiredQuantity * UnitPrice : null;
}

/// <summary>
/// Eingabe-Annahmen der Baukosten-Rechnung (#65). Alle Geldwerte sind explizite,
/// aufklappbare Annahmen — nie stille Fantasiewerte. Fehlende Annahmen (null)
/// ergeben in der Ableitung einen sichtbaren „unbekannt"-Zustand mit Grund.
/// </summary>
public sealed record BuildCostAssumptions
{
    /// <summary>Runs des Jobs (≥ 1).</summary>
    public required int Runs { get; init; }

    /// <summary>Materialeffizienz 0..10 (nur für den Materialbedarf; Job-Basis bleibt ME0).</summary>
    public required int MaterialEfficiency { get; init; }

    /// <summary>Zeiteffizienz 0..20 (nur Anzeige der geschätzten Jobdauer).</summary>
    public required int TimeEfficiency { get; init; }

    /// <summary>true = BPC (Kopie), false = BPO (Original) — Qualitätszustand der Anzeige.</summary>
    public required bool IsBlueprintCopy { get; init; }

    /// <summary>Verbleibende Runs der BPC (Anzeige; null bei BPO).</summary>
    public int? RemainingRuns { get; init; }

    /// <summary>
    /// Bester Verkaufskurs (BestSell) je TypeId im Vergleichsmarkt — was die
    /// Beschaffung des Materials kostet. Fehlende Einträge = unbekannter Preis.
    /// </summary>
    public IReadOnlyDictionary<int, decimal> MaterialSellPrices { get; init; }
        = new Dictionary<int, decimal>();

    /// <summary>Bester Verkaufskurs (BestSell) des Produkts — was der Kauf am Markt kostet.</summary>
    public decimal? ProductSellPrice { get; init; }

    /// <summary>Bester Kaufkurs (BestBuy) des Produkts — was ein Verkauf dort brächte.</summary>
    public decimal? ProductBuyPrice { get; init; }

    /// <summary>
    /// System-Cost-Index-Annahme in Prozent (z. B. 5 = 5 %); null = unbekannt.
    /// Der Cost Index ist dynamisch (System/Facility) und wird nicht live abgefragt.
    /// </summary>
    public double? SystemCostIndexPercent { get; init; }

    /// <summary>
    /// Facility-Steuer-Annahme in Prozent (Struktur-Steuer des Betreibers);
    /// null = unbekannte Structure-Kosten. Niemals durch einen Fantasiewert ersetzen.
    /// </summary>
    public double? FacilityTaxPercent { get; init; }

    /// <summary>
    /// SCC-Zuschlag in Prozent — EVE-Konstante: 4 % seit den Viridian-Tax-Reforms
    /// (2024-02-01, von zuvor 1,5 % angehoben). Quelle: EVE University, „Manufacturing",
    /// Abschnitt „Total job cost". Alpha-Clone-Steuer ist nicht enthalten.
    /// </summary>
    public const double SccSurchargePercent = 4.0;

    /// <summary>Verkaufssteuer-Annahme in Prozent beim Verkauf des Produkts (Standard 1 %).</summary>
    public double SalesTaxPercent { get; init; } = 1.0;

    /// <summary>Brokergebühr-Annahme in Prozent bei Marktkäufen (Standard 1 %).</summary>
    public double BrokerFeePercent { get; init; } = 1.0;

    /// <summary>Name des Vergleichsmarkts für die Anzeige; null = keiner konfiguriert.</summary>
    public string? ComparisonMarketName { get; init; }
}

/// <summary>
/// Baukosten-Schätzung und Build-vs-Buy-Vergleich (#65). Kostenkomponenten
/// (Material, Job/Installation, Facility, Steuer/Zuschlag) und Marktpreise sind
/// getrennt geführt; jede unbekannte Komponente bleibt null mit Grund in
/// <see cref="UnknownNotes"/> — nie ein erfundener Wert.
/// </summary>
public sealed record BuildCostEstimate
{
    /// <summary>Blueprint-TypeId.</summary>
    public required int BlueprintTypeId { get; init; }

    /// <summary>Produkt-TypeId je Run.</summary>
    public required int ProductTypeId { get; init; }

    /// <summary>Produktmenge je Run laut Rezept.</summary>
    public required long ProductQuantityPerRun { get; init; }

    /// <summary>Runs des Jobs.</summary>
    public required int Runs { get; init; }

    /// <summary>Materialeffizienz des Blueprints (Qualitätszustand).</summary>
    public required int MaterialEfficiency { get; init; }

    /// <summary>Zeiteffizienz des Blueprints (Qualitätszustand).</summary>
    public required int TimeEfficiency { get; init; }

    /// <summary>true = BPC, false = BPO (Qualitätszustand).</summary>
    public required bool IsBlueprintCopy { get; init; }

    /// <summary>Verbleibende Runs der BPC; null bei BPO.</summary>
    public int? RemainingRuns { get; init; }

    /// <summary>Name des Vergleichsmarkts, aus dem die Preise stammen; null = keiner konfiguriert.</summary>
    public string? ComparisonMarketName { get; init; }

    /// <summary>Annahme Cost Index in Prozent (Anzeige in den aufklappbaren Annahmen); null = nicht angenommen.</summary>
    public double? SystemCostIndexPercent { get; init; }

    /// <summary>Annahme Facility-Steuer in Prozent (Anzeige); null = Struktursteuer unbekannt.</summary>
    public double? FacilityTaxPercent { get; init; }

    /// <summary>Annahme Verkaufssteuer in Prozent (Anzeige).</summary>
    public double SalesTaxPercent { get; init; }

    /// <summary>Annahme Brokergebühr in Prozent (Anzeige).</summary>
    public double BrokerFeePercent { get; init; }

    /// <summary>Geschätzte Jobdauer in Sekunden (ManufacturingMath, TE-bereinigt).</summary>
    public required long JobTimeSeconds { get; init; }

    /// <summary>Bedarfszeilen je Material mit Preis; Preis null = unbekannt.</summary>
    public IReadOnlyList<MaterialCostLine> Materials { get; init; } = Array.Empty<MaterialCostLine>();

    /// <summary>
    /// Materialkosten gesamt (Σ Zeilen). Nur bekannt, wenn ALLE Preise bekannt
    /// sind — ein einzelner fehlender Preis macht die Summe unbekannt (nie 0).
    /// </summary>
    public decimal? MaterialCost { get; init; }

    /// <summary>
    /// Geschätzter Item-Wert (EVE-Job-Formel): ME0-Materialwert × Runs — die
    /// Berechnungsbasis der Job-Gebühr. Unabhängig vom ME des Blueprints.
    /// </summary>
    public decimal? EstimatedItemValue { get; init; }

    /// <summary>Job-Installationsanteil: EIV × Cost Index (Annahme).</summary>
    public decimal? SystemCostIndexFee { get; init; }

    /// <summary>Facility-Steuer-Anteil: EIV × Struktursteuer (Annahme); null = Struktur unbekannt.</summary>
    public decimal? FacilityTax { get; init; }

    /// <summary>SCC-Zuschlag: EIV × 4 % (EVE-Konstante).</summary>
    public decimal? SccSurcharge { get; init; }

    /// <summary>
    /// Job-Gebühr gesamt (Installation + Facility + SCC). Nur bekannt, wenn alle
    /// drei Anteile bekannt sind; sonst null mit Grund in <see cref="UnknownNotes"/>.
    /// </summary>
    public decimal? JobCost { get; init; }

    /// <summary>Brokergebühr auf Marktkäufe der Materialien (Annahme).</summary>
    public decimal? BrokerFeeOnMaterials { get; init; }

    /// <summary>Baukosten gesamt = Material + Broker + Job-Gebühr.</summary>
    public decimal? BuildCost { get; init; }

    /// <summary>Bester Verkaufskurs des Produkts am Vergleichsmarkt (Kauf-Preis); null = unbekannt.</summary>
    public decimal? ProductSellPrice { get; init; }

    /// <summary>Bester Kaufkurs des Produkts (Verkaufserlös); null = unbekannt.</summary>
    public decimal? ProductBuyPrice { get; init; }

    /// <summary>Erwarteter Netto-Verkaufserlös: Produkt-Buy × Menge × (1 − Verkaufssteuer).</summary>
    public decimal? SellProceedsNet { get; init; }

    /// <summary>Buy-Kosten: Produkt-Sell × Menge × (1 + Broker) — Kauf statt Bauen.</summary>
    public decimal? BuyCost { get; init; }

    /// <summary>Build-vs-Buy-Ersparnis: Buy − Build; positiv = Bauen lohnt sich.</summary>
    public decimal? BuildVsBuySavings { get; init; }

    /// <summary>
    /// Sichtbare Gründe für unbekannte Werte (Deutsch), z. B. fehlender
    /// Marktpreis, nicht angenommener Cost Index oder unbekannte Struktursteuer.
    /// Leer = alle Komponenten bekannt.
    /// </summary>
    public IReadOnlyList<string> UnknownNotes { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Ergebnis einer Baukosten-Berechnung (#65). Ein Fehlschlag (unbekanntes Rezept)
/// liefert absichtlich KEINE Schätzung — es darf nie eine scheinbar valide
/// Kostenaussage ohne Rezept entstehen.
/// </summary>
public sealed class BuildCostResult
{
    private BuildCostResult(bool isSuccess, BuildCostEstimate? estimate, string? failureReason)
    {
        IsSuccess = isSuccess;
        Estimate = estimate;
        FailureReason = failureReason;
    }

    public bool IsSuccess { get; }

    /// <summary>Kostenschätzung mit Build-vs-Buy-Vergleich. Nur bei Erfolg.</summary>
    public BuildCostEstimate? Estimate { get; }

    /// <summary>Nutzerlesbare Fehlerursache (deutsch). Nur bei Misserfolg.</summary>
    public string? FailureReason { get; }

    public static BuildCostResult Success(BuildCostEstimate estimate) => new(true, estimate, null);

    public static BuildCostResult Failed(string reason) => new(false, null, reason);
}