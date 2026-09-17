using Microsoft.EntityFrameworkCore;
using WALLEve.Data;
using WALLEve.Models.Industry;
using WALLEve.Services.Industry.Interfaces;
using WALLEve.Services.Market.Interfaces;
using WALLEve.Services.Sde.Interfaces;

namespace WALLEve.Services.Industry;

/// <summary>
/// Orchestriert die Baukosten-/Build-vs-Buy-Berechnung (#65): Rezept aus der
/// SDE, Marktpreise aus den persistierten Snapshots des Vergleichsmarkts
/// (eine Abfrage für alle beteiligten Typen, kein N+1), deterministische
/// Formeln aus <see cref="BuildCostMath"/>. Fehlende Preise/Annahmen bleiben
/// sichtbar unbekannt (null mit Grund) — nie 0 ISK, nie ein Fantasiewert.
/// Keine Live-ESI-Aufrufe: Preise kommen aus dem letzten Snapshot.
/// </summary>
public class BuildCostService : IBuildCostService
{
    private readonly ISdeIndustryRepository _sdeIndustryRepository;
    private readonly IHubSelectionService _hubSelection;
    private readonly WalletDbContext _db;

    public BuildCostService(
        ISdeIndustryRepository sdeIndustryRepository,
        IHubSelectionService hubSelection,
        WalletDbContext db)
    {
        _sdeIndustryRepository = sdeIndustryRepository;
        _hubSelection = hubSelection;
        _db = db;
    }

    public async Task<BuildCostResult> CalculateAsync(
        int blueprintTypeId,
        BuildCostAssumptions assumptions,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var recipe = await _sdeIndustryRepository.GetManufacturingRecipeAsync(blueprintTypeId, cancellationToken);
        if (recipe is null)
        {
            return BuildCostResult.Failed($"Unbekanntes Rezept für Blueprint {blueprintTypeId}: keine Baukosten ableitbar.");
        }

        // Parameter-Validierung wie in ManufacturingRequirementService: nie still
        // korrigieren, nie eine Kostenaussage ohne gültige Eingaben erzeugen.
        if (assumptions.MaterialEfficiency is < ManufacturingMath.MinMaterialEfficiency or > ManufacturingMath.MaxMaterialEfficiency)
        {
            return BuildCostResult.Failed(
                $"Materialeffizienz {assumptions.MaterialEfficiency} liegt außerhalb des Bereichs " +
                $"{ManufacturingMath.MinMaterialEfficiency}..{ManufacturingMath.MaxMaterialEfficiency}.");
        }

        if (assumptions.TimeEfficiency is < 0 or > ManufacturingMath.MaxTimeEfficiency)
        {
            return BuildCostResult.Failed(
                $"Zeiteffizienz {assumptions.TimeEfficiency} liegt außerhalb des Bereichs 0..{ManufacturingMath.MaxTimeEfficiency}.");
        }

        if (assumptions.Runs < 1)
        {
            return BuildCostResult.Failed("Die Anzahl der Runs muss ≥ 1 sein.");
        }

        int? maxRuns;
        try
        {
            maxRuns = ManufacturingMath.MaxAllowedRuns(recipe.MaxProductionLimit, assumptions.IsBlueprintCopy, assumptions.RemainingRuns);
        }
        catch (ArgumentException)
        {
            return BuildCostResult.Failed("Verbleibende Runs einer Blueprint-Kopie fehlen: Runs-Limit nicht prüfbar.");
        }

        if (maxRuns is not null && assumptions.Runs > maxRuns.Value)
        {
            return BuildCostResult.Failed(
                $"Angeforderte Runs ({assumptions.Runs}) überschreiten das Limit des Blueprints ({maxRuns.Value}).");
        }

        if (recipe.Materials.Count == 0 || recipe.ProductQuantity < 1)
        {
            return BuildCostResult.Failed(
                $"Rezept {blueprintTypeId} ist unvollständig (keine Materialien oder keine Produktmenge): keine Baukosten ableitbar.");
        }

        // Preise je Typ aus den Snapshots des Vergleichsmarkts — eine Abfrage für
        // alle Materialien + das Produkt (kein N+1). Kein Vergleichsmarkt → alle
        // Preise unbekannt, sichtbar mit Grund.
        var comparison = await _hubSelection.GetComparisonMarketAsync(cancellationToken);
        var prices = comparison is null
            ? new Dictionary<int, (double? Sell, double? Buy)>()
            : await LoadPricesAsync(comparison.RegionId, CollectTypeIds(recipe), cancellationToken);

        var unknownNotes = new List<string>();
        if (comparison is null)
        {
            unknownNotes.Add("Kein Vergleichsmarkt konfiguriert — Marktpreise unbekannt (nie 0 ISK).");
        }
        else if (prices.Count == 0)
        {
            unknownNotes.Add($"Vergleichsmarkt „{comparison.Name}“ hat keine Snapshots für die Rezept-Typen — Preise unbekannt.");
        }

        // Materialbedarf: ME-abhängig über ManufacturingMath (identisch zur Bedarfs-Ansicht).
        var materialLines = new List<MaterialCostLine>(recipe.Materials.Count);
        foreach (var material in recipe.Materials)
        {
            var required = ManufacturingMath.MaterialRequirementForJob(material.BaseQuantity, assumptions.MaterialEfficiency, assumptions.Runs);
            var sell = Price(prices, material.MaterialTypeId).Sell;
            materialLines.Add(new MaterialCostLine
            {
                MaterialTypeId = material.MaterialTypeId,
                RequiredQuantity = required,
                UnitPrice = sell.HasValue ? (decimal)sell.Value : null
            });

            if (!sell.HasValue && comparison is not null)
            {
                unknownNotes.Add($"Marktpreis für Material Type {material.MaterialTypeId} fehlt im Vergleichsmarkt.");
            }
        }

        var materialLinesWithPrice = materialLines.ToList();
        var materialCost = BuildCostMath.MaterialCost(
            materialLinesWithPrice.Select(l => (l.RequiredQuantity, l.UnitPrice)).ToList());

        // Job-Basis: ME0-Materialwert × Runs (EVE-Formel, unabhängig vom Blueprint-ME).
        var estimatedItemValue = BuildCostMath.EstimatedItemValue(
            recipe.Materials.Select(m => ((long)m.BaseQuantity, Price(prices, m.MaterialTypeId).Sell is { } p ? (decimal?)p : null)).ToList(),
            assumptions.Runs);

        var components = estimatedItemValue.HasValue
            ? BuildCostMath.JobCostComponents(estimatedItemValue.Value, assumptions.SystemCostIndexPercent, assumptions.FacilityTaxPercent)
            : new JobCostComponents(null, null, null, null);

        if (assumptions.SystemCostIndexPercent is null)
        {
            unknownNotes.Add("System-Cost-Index nicht angenommen — der Installationsanteil bleibt unbekannt.");
        }

        if (assumptions.FacilityTaxPercent is null)
        {
            unknownNotes.Add("Struktursteuer nicht angenommen — Facility-Kosten unbekannt (nie 0 ISK).");
        }

        // Brokergebühr auf Marktkäufe der Materialien (Annahme).
        decimal? brokerFeeOnMaterials = null;
        if (materialCost.HasValue && assumptions.BrokerFeePercent > 0)
        {
            brokerFeeOnMaterials = BuildCostMath.RoundUpToIsk(
                materialCost.Value * (decimal)(assumptions.BrokerFeePercent / 100.0));
        }

        // Baukosten gesamt nur, wenn alle Komponenten bekannt sind.
        decimal? buildCost = null;
        if (materialCost.HasValue && brokerFeeOnMaterials.HasValue && components.Total.HasValue)
        {
            buildCost = materialCost.Value + brokerFeeOnMaterials.Value + components.Total.Value;
        }

        var productSell = Price(prices, recipe.ProductTypeId).Sell;
        var productBuy = Price(prices, recipe.ProductTypeId).Buy;
        var productQuantity = recipe.ProductQuantity * assumptions.Runs;

        if (productSell is null && comparison is not null)
        {
            unknownNotes.Add($"Marktpreis (Sell) für Produkt Type {recipe.ProductTypeId} fehlt — Buy-Kosten unbekannt.");
        }

        if (productBuy is null && comparison is not null)
        {
            unknownNotes.Add($"Marktpreis (Buy) für Produkt Type {recipe.ProductTypeId} fehlt — Verkaufserlös unbekannt.");
        }

        var sellProceedsNet = BuildCostMath.SellProceedsNet(
            productBuy.HasValue ? (decimal)productBuy.Value : null, productQuantity, assumptions.SalesTaxPercent);
        var buyCost = BuildCostMath.BuyCost(
            productSell.HasValue ? (decimal)productSell.Value : null, productQuantity, assumptions.BrokerFeePercent);

        var estimate = new BuildCostEstimate
        {
            BlueprintTypeId = blueprintTypeId,
            ProductTypeId = recipe.ProductTypeId,
            ProductQuantityPerRun = recipe.ProductQuantity,
            Runs = assumptions.Runs,
            MaterialEfficiency = assumptions.MaterialEfficiency,
            TimeEfficiency = assumptions.TimeEfficiency,
            IsBlueprintCopy = assumptions.IsBlueprintCopy,
            RemainingRuns = assumptions.RemainingRuns,
            ComparisonMarketName = comparison?.Name,
            SystemCostIndexPercent = assumptions.SystemCostIndexPercent,
            FacilityTaxPercent = assumptions.FacilityTaxPercent,
            SalesTaxPercent = assumptions.SalesTaxPercent,
            BrokerFeePercent = assumptions.BrokerFeePercent,
            JobTimeSeconds = ManufacturingMath.JobTimeSeconds(recipe.BaseTimeSeconds, assumptions.TimeEfficiency, assumptions.Runs),
            Materials = materialLinesWithPrice,
            MaterialCost = materialCost,
            EstimatedItemValue = estimatedItemValue,
            SystemCostIndexFee = components.SystemCostIndexFee,
            FacilityTax = components.FacilityTax,
            SccSurcharge = components.SccSurcharge,
            JobCost = components.Total,
            BrokerFeeOnMaterials = brokerFeeOnMaterials,
            BuildCost = buildCost,
            ProductSellPrice = productSell.HasValue ? (decimal)productSell.Value : null,
            ProductBuyPrice = productBuy.HasValue ? (decimal)productBuy.Value : null,
            SellProceedsNet = sellProceedsNet,
            BuyCost = buyCost,
            BuildVsBuySavings = BuildCostMath.BuildVsBuySavings(buyCost, buildCost),
            UnknownNotes = unknownNotes
        };

        return BuildCostResult.Success(estimate);
    }

    private static HashSet<int> CollectTypeIds(ManufacturingRecipe recipe)
    {
        var typeIds = new HashSet<int> { recipe.ProductTypeId };
        foreach (var material in recipe.Materials)
        {
            typeIds.Add(material.MaterialTypeId);
        }

        return typeIds;
    }

    private static (double? Sell, double? Buy) Price(IReadOnlyDictionary<int, (double? Sell, double? Buy)> prices, int typeId)
        => prices.TryGetValue(typeId, out var price) ? price : (null, null);

    /// <summary>
    /// Neuester Snapshot je Type im Vergleichsmarkt (eine gruppierte Abfrage,
    /// kein N+1). Analog zur Fehlmengen-Bewertung in StockpileOverviewService.
    /// </summary>
    private async Task<IReadOnlyDictionary<int, (double? Sell, double? Buy)>> LoadPricesAsync(
        int regionId,
        IReadOnlyCollection<int> typeIds,
        CancellationToken cancellationToken)
    {
        var snapshots = await _db.MarketSnapshots
            .AsNoTracking()
            .Where(s => s.RegionId == regionId && typeIds.Contains(s.TypeId))
            .GroupBy(s => s.TypeId)
            .Select(g => g.OrderByDescending(s => s.Timestamp).First())
            .ToListAsync(cancellationToken);

        return snapshots.ToDictionary(
            s => s.TypeId,
            s => (Sell: s.BestSellPrice, Buy: s.BestBuyPrice));
    }
}