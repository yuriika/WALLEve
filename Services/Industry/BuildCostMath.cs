using WALLEve.Models.Industry;

namespace WALLEve.Services.Industry;

/// <summary>
/// Reine, deterministische Formeln für Baukosten und Build-vs-Buy (#65).
/// Keine I/O, keine ESI-Abhängigkeit, keine stillen Korrekturen — ungültige
/// Eingaben (negative Mengen/Preise/Sätze) werfen <see cref="ArgumentOutOfRangeException"/>.
///
/// Fixierte Regeln (Quellen, siehe auch Tests in BuildCostMathTests):
/// - Kostenkomponenten runden konservativ NACH OBEN auf ganze ISK (nie eine
///   unterschätzte Kostenaussage); Erlöse runden abwärts (nie überschätzter Gewinn).
/// - Materialbedarf kommt aus <c>ManufacturingMath</c> (ME-abhängig). Die
///   Preissumme skaliert mit Runs und ME.
/// - Job-Gebühr (EVE-Formel „Total job cost", EVE University, Abschnitt
///   „Manufacturing"): geschätzter Item-Wert × (Cost Index + Facility-Steuer +
///   SCC-Zuschlag). Der geschätzte Item-Wert ist der <b>ME0</b>-Materialwert ×
///   Runs (laut EVE „cost estimation of the materials for a ME0 blueprint",
///   unabhängig vom ME des Blueprints): besseres ME senkt die Materialkosten,
///   aber NICHT die Job-Gebühr.
/// - <b>Einmalige Kostenanrechnung:</b> jede Prozent-Komponente wird GENAU
///   EINMAL auf den geschätzten Item-Wert angewendet — nie pro Materialzeile,
///   nie zusätzlich je Run. Ein Fixture beweist das (siehe Tests).
/// - Unbekannte Struktur-/Cost-Index-Anteile (null) ergeben null mit korrektem
///   Grund — nie 0 ISK, nie einen erfundenen Wert. Der SCC-Zuschlag ist mit
///   4 % eine belegte EVE-Konstante (Viridian Tax Reforms, 2024-02-01).
/// </summary>
public static class BuildCostMath
{
    /// <summary>SCC-Zuschlag in Prozent (EVE-Konstante, siehe Klassen-Kommentar).</summary>
    public const double SccSurchargePercent = BuildCostAssumptions.SccSurchargePercent;

    /// <summary>
    /// Materialkosten gesamt: Σ (Menge × Kurs). Nur bekannt, wenn ALLE Kurse
    /// bekannt sind; ein fehlender Kurs macht die Summe unbekannt (nie 0).
    /// </summary>
    /// <param name="lines">(Menge, Kurs) je Material; Kurs null = unbekannt.</param>
    public static decimal? MaterialCost(IReadOnlyList<(long Quantity, decimal? UnitPrice)> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        decimal total = 0m;
        foreach (var (quantity, unitPrice) in lines)
        {
            ValidateQuantity(quantity);
            if (unitPrice is null)
            {
                return null;
            }

            total += quantity * unitPrice.Value;
        }

        return RoundUpToIsk(total);
    }

    /// <summary>
    /// Geschätzter Item-Wert (Basis der Job-Gebühr): Σ (ME0-Basismenge × Kurs) × Runs.
    /// Nur bekannt, wenn alle Materialkurse bekannt sind.
    /// </summary>
    /// <param name="baseQuantities">(ME0-Basismenge je Run, Kurs) je Material.</param>
    /// <param name="runs">Runs des Jobs (≥ 1).</param>
    public static decimal? EstimatedItemValue(IReadOnlyList<(long BaseQuantity, decimal? UnitPrice)> baseQuantities, int runs)
    {
        ArgumentNullException.ThrowIfNull(baseQuantities);
        ValidateRuns(runs);

        decimal total = 0m;
        foreach (var (baseQuantity, unitPrice) in baseQuantities)
        {
            ValidateQuantity(baseQuantity);
            if (unitPrice is null)
            {
                return null;
            }

            total += baseQuantity * unitPrice.Value;
        }

        return RoundUpToIsk(total * runs);
    }

    /// <summary>
    /// Job-Gebühr-Komponenten. Jede Prozent-Komponente wird genau einmal auf den
    /// geschätzten Item-Wert angewendet. Fehlende Annahmen (null) bleiben null;
    /// die Job-Gebühr ist nur bekannt, wenn alle drei Anteile bekannt sind.
    /// </summary>
    /// <param name="estimatedItemValue">Bereits bekannter ME0-Item-Wert (nicht null).</param>
    /// <param name="systemCostIndexPercent">Cost-Index-Annahme in Prozent; null = unbekannt.</param>
    /// <param name="facilityTaxPercent">Facility-Steuer-Annahme in Prozent; null = unbekannte Structure-Kosten.</param>
    /// <returns>Anteile und Gesamt-Job-Gebühr (jeweils null bei unbekannter Komponente).</returns>
    public static JobCostComponents JobCostComponents(
        decimal estimatedItemValue,
        double? systemCostIndexPercent,
        double? facilityTaxPercent)
    {
        if (estimatedItemValue < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(estimatedItemValue), "Der geschätzte Item-Wert darf nicht negativ sein.");
        }

        if (systemCostIndexPercent is not null)
        {
            ValidatePercent(systemCostIndexPercent.Value, nameof(systemCostIndexPercent));
        }

        if (facilityTaxPercent is not null)
        {
            ValidatePercent(facilityTaxPercent.Value, nameof(facilityTaxPercent));
        }

        var scc = RoundUpToIsk(estimatedItemValue * (decimal)(SccSurchargePercent / 100.0));

        // Jede Komponente ist unabhängig: sie wird berechnet, sobald ihre eigene
        // Annahme vorliegt. Nur die Gesamt-Job-Gebühr verlangt alle drei Anteile.
        decimal? systemCostIndexFee = systemCostIndexPercent is null
            ? null
            : RoundUpToIsk(estimatedItemValue * (decimal)(systemCostIndexPercent.Value / 100.0));

        decimal? facilityTax = facilityTaxPercent is null
            ? null
            : RoundUpToIsk(estimatedItemValue * (decimal)(facilityTaxPercent.Value / 100.0));

        decimal? total = systemCostIndexFee.HasValue && facilityTax.HasValue
            ? systemCostIndexFee.Value + facilityTax.Value + scc
            : null;

        return new JobCostComponents(systemCostIndexFee, facilityTax, scc, total);
    }

    /// <summary>
    /// Erwarteter Netto-Verkaufserlös: BestBuy × Produktmenge × (1 − Verkaufssteuer),
    /// konservativ abgerundet (Gewinn nie überschätzen).
    /// </summary>
    public static decimal? SellProceedsNet(decimal? productBuyPrice, long productQuantity, double salesTaxPercent)
    {
        ValidateQuantity(productQuantity);
        ValidatePercent(salesTaxPercent, nameof(salesTaxPercent));

        if (productBuyPrice is null || productBuyPrice <= 0)
        {
            return null;
        }

        return Math.Floor(productBuyPrice.Value * productQuantity * (1m - (decimal)(salesTaxPercent / 100.0)));
    }

    /// <summary>
    /// Buy-Kosten: BestSell × Produktmenge × (1 + Broker), konservativ aufgerundet
    /// (Kauf-Kosten nie unterschätzen).
    /// </summary>
    public static decimal? BuyCost(decimal? productSellPrice, long productQuantity, double brokerFeePercent)
    {
        ValidateQuantity(productQuantity);
        ValidatePercent(brokerFeePercent, nameof(brokerFeePercent));

        if (productSellPrice is null || productSellPrice <= 0)
        {
            return null;
        }

        return RoundUpToIsk(productSellPrice.Value * productQuantity * (1m + (decimal)(brokerFeePercent / 100.0)));
    }

    /// <summary>
    /// Build-vs-Buy-Ersparnis: Buy − Build. Positiv = Bauen ist günstiger als der
    /// Marktkauf. Nur bekannt, wenn beide Seiten bekannt sind.
    /// </summary>
    public static decimal? BuildVsBuySavings(decimal? buyCost, decimal? buildCost)
    {
        if (buyCost is null || buildCost is null)
        {
            return null;
        }

        return buyCost.Value - buildCost.Value;
    }

    /// <summary>Konservativ nach oben runden auf ganze ISK.</summary>
    public static decimal RoundUpToIsk(decimal value) => Math.Ceiling(value);

    private static void ValidateQuantity(long quantity)
    {
        if (quantity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Mengen müssen ≥ 1 sein (kein Nullkosten-Pfad).");
        }
    }

    private static void ValidateRuns(int runs)
    {
        if (runs < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(runs), "Runs müssen ≥ 1 sein.");
        }
    }

    private static void ValidatePercent(double percent, string paramName)
    {
        if (percent < 0)
        {
            throw new ArgumentOutOfRangeException(paramName, "Prozentsätze dürfen nicht negativ sein.");
        }
    }
}

/// <summary>
/// Aufgeschlüsselte Job-Gebühr (#65): jeder Anteil getrennt geführt; unbekannte
/// Anteile bleiben null. <see cref="Total"/> ist nur gesetzt, wenn alle drei
/// Anteile bekannt sind.
/// </summary>
public sealed record JobCostComponents(
    decimal? SystemCostIndexFee,
    decimal? FacilityTax,
    decimal? SccSurcharge,
    decimal? Total);