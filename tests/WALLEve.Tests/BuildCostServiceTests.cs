using WALLEve.Data;
using WALLEve.Models.Database;
using WALLEve.Models.Industry;
using WALLEve.Models.Market;
using WALLEve.Services.Industry;
using WALLEve.Services.Industry.Interfaces;
using WALLEve.Services.Market.Interfaces;
using WALLEve.Services.Sde.Interfaces;

namespace WALLEve.Tests;

/// <summary>
/// Orchestrierungs-Tests der Baukosten-Schätzung (#65): unbekanntes Rezept
/// → Fehlschlag; Preise aus dem Vergleichsmarkt-Snapshot; Kostenkomponenten
/// getrennt (Material / Job / Facility / SCC); einmalige Kostenanrechnung;
/// unbekannte Preise/Annahmen → null mit Grund, nie 0 ISK und nie ein
/// Fantasiewert; fehlender Vergleichsmarkt bleibt sichtbar unbekannt.
/// </summary>
public class BuildCostServiceTests
{
    private const int BantamBlueprint = 683;   // Bantam-BPO
    private const int BantamProduct = 582;
    private const int ComparisonRegion = 10000002; // The Forge

    private static ManufacturingRecipe BantamRecipe() => new()
    {
        BlueprintTypeId = BantamBlueprint,
        ProductTypeId = BantamProduct,
        ProductQuantity = 1,
        BaseTimeSeconds = 6000,
        MaxProductionLimit = 30,
        Materials = new List<RecipeMaterial>
        {
            new(34, 24000),  // Tritanium
            new(35, 4500),   // Pyerite
            new(36, 1875),   // Mexallon
            new(37, 375)     // Isogen
        }
    };

    private sealed class FakeSdeIndustryRepository : ISdeIndustryRepository
    {
        public ManufacturingRecipe? RecipeToReturn { get; set; }

        public Task<ManufacturingRecipe?> GetManufacturingRecipeAsync(
            int blueprintTypeId, CancellationToken cancellationToken = default) =>
            Task.FromResult(RecipeToReturn);
    }

    private sealed class FakeHubSelectionService : IHubSelectionService
    {
        public MarketHubProfile? ComparisonMarket { get; set; }

        public Task<MarketHubProfile?> GetComparisonMarketAsync(CancellationToken ct = default) =>
            Task.FromResult(ComparisonMarket);

        public Task<List<MarketHubProfile>> GetProfilesAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task SaveProfileAsync(MarketHubProfile profile, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeleteProfileAsync(int profileId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<HubSelectionResult> SelectNearestActiveHubAsync(int fromSystemId, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private static BuildCostAssumptions Assumptions(
        int runs = 1,
        int me = 0,
        bool isCopy = false,
        int? remainingRuns = null,
        double? costIndex = 5,
        double? facilityTax = 2,
        double brokerFee = 1.0) => new()
    {
        Runs = runs,
        MaterialEfficiency = me,
        TimeEfficiency = 0,
        IsBlueprintCopy = isCopy,
        RemainingRuns = remainingRuns,
        SystemCostIndexPercent = costIndex,
        FacilityTaxPercent = facilityTax,
        BrokerFeePercent = brokerFee
    };

    private static void SeedPrices(WalletDbContext db)
    {
        db.MarketSnapshots.Add(new MarketSnapshot
        {
            RegionId = ComparisonRegion, TypeId = 34, Timestamp = new DateTime(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc),
            BestSellPrice = 4.0, BestBuyPrice = 3.5
        });
        db.MarketSnapshots.Add(new MarketSnapshot
        {
            RegionId = ComparisonRegion, TypeId = 35, Timestamp = new DateTime(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc),
            BestSellPrice = 8.0, BestBuyPrice = 7.5
        });
        db.MarketSnapshots.Add(new MarketSnapshot
        {
            RegionId = ComparisonRegion, TypeId = 36, Timestamp = new DateTime(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc),
            BestSellPrice = 16.0, BestBuyPrice = 15.0
        });
        db.MarketSnapshots.Add(new MarketSnapshot
        {
            RegionId = ComparisonRegion, TypeId = 37, Timestamp = new DateTime(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc),
            BestSellPrice = 40.0, BestBuyPrice = 38.0
        });
        db.MarketSnapshots.Add(new MarketSnapshot
        {
            RegionId = ComparisonRegion, TypeId = BantamProduct, Timestamp = new DateTime(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc),
            BestSellPrice = 200000.0, BestBuyPrice = 180000.0
        });
        db.SaveChanges();
    }

    private static (BuildCostService Service, WalletDbContext Db) Create(ManufacturingRecipe? recipe, MarketHubProfile? comparison)
    {
        var db = TestDb.Create();
        var service = new BuildCostService(
            new FakeSdeIndustryRepository { RecipeToReturn = recipe },
            new FakeHubSelectionService { ComparisonMarket = comparison },
            db);
        return (service, db);
    }

    [Fact]
    public async Task UnknownRecipe_ReturnsFailure_NeverAnEstimate()
    {
        var (service, _) = Create(recipe: null, comparison: null);

        var result = await service.CalculateAsync(BantamBlueprint, Assumptions());

        Assert.False(result.IsSuccess);
        Assert.Null(result.Estimate);
        Assert.NotNull(result.FailureReason);
        Assert.Contains("Unbekanntes Rezept", result.FailureReason);
    }

    [Fact]
    public async Task MissingComparisonMarket_AllPricesUnknown_WithVisibleReason()
    {
        var (service, _) = Create(BantamRecipe(), comparison: null);

        var result = await service.CalculateAsync(BantamBlueprint, Assumptions());

        Assert.True(result.IsSuccess);
        var estimate = result.Estimate!;
        Assert.Null(estimate.MaterialCost);
        Assert.Null(estimate.EstimatedItemValue);
        Assert.Null(estimate.JobCost);
        Assert.Null(estimate.BuildCost);
        Assert.Null(estimate.BuyCost);
        Assert.Null(estimate.SellProceedsNet);
        Assert.Contains(estimate.UnknownNotes, n => n.Contains("Kein Vergleichsmarkt konfiguriert"));
        // Zeilen existieren trotzdem mit unbekannten Kursen — nie leere Schätzung ohne Grund.
        Assert.Equal(4, estimate.Materials.Count);
        Assert.All(estimate.Materials, line => Assert.Null(line.UnitPrice));
        Assert.All(estimate.Materials, line => Assert.Null(line.LineCost));
    }

    [Fact]
    public async Task AllPricesKnown_SeparateComponentsAndOneTimeJobFee()
    {
        var (service, db) = Create(BantamRecipe(), new MarketHubProfile { Name = "The Forge", RegionId = ComparisonRegion });
        SeedPrices(db);

        var result = await service.CalculateAsync(BantamBlueprint, Assumptions(costIndex: 5, facilityTax: 2));

        Assert.True(result.IsSuccess);
        var e = result.Estimate!;
        Assert.Empty(e.UnknownNotes);

        // Materialkosten: ME0-Job-Bedarf × Kurse (194728, vgl. BuildCostMathTests).
        Assert.Equal(194728m, e.MaterialCost);
        // EIV: ME0-Materialwert × Runs = 177000.
        Assert.Equal(177000m, e.EstimatedItemValue);
        // Job-Gebühr: genau 11 % einmalig auf den Item-Wert, getrennt geführt.
        Assert.Equal(8850m, e.SystemCostIndexFee);
        Assert.Equal(3540m, e.FacilityTax);
        Assert.Equal(7080m, e.SccSurcharge);
        Assert.Equal(19470m, e.JobCost);
        // Baukosten: Material + Broker (1 % → 1948) + Job-Gebühr.
        Assert.Equal(1948m, e.BrokerFeeOnMaterials);
        Assert.Equal(216146m, e.BuildCost);
        // Marktseite: Buy 202000, Netto-Erlös 178200; Bauen teurer → negative Ersparnis.
        Assert.Equal(200000m, e.ProductSellPrice);
        Assert.Equal(180000m, e.ProductBuyPrice);
        Assert.Equal(202000m, e.BuyCost);
        Assert.Equal(178200m, e.SellProceedsNet);
        Assert.Equal(-14146m, e.BuildVsBuySavings);
        Assert.Equal("The Forge", e.ComparisonMarketName);
    }

    [Fact]
    public async Task ZeroBrokerFee_IsKnownAssumption_ZeroFeeAndCalculableBuildCost()
    {
        var (service, db) = Create(BantamRecipe(), new MarketHubProfile { Name = "The Forge", RegionId = ComparisonRegion });
        SeedPrices(db);

        var result = await service.CalculateAsync(BantamBlueprint, Assumptions(costIndex: 5, facilityTax: 2, brokerFee: 0));

        Assert.True(result.IsSuccess);
        var e = result.Estimate!;
        Assert.Empty(e.UnknownNotes);
        // 0 % Brokergebühr ist eine explizite Annahme: 0 ISK Gebühr, nie unbekannt.
        Assert.Equal(0m, e.BrokerFeeOnMaterials);
        Assert.Equal(194728m, e.MaterialCost);
        Assert.Equal(19470m, e.JobCost);
        // Baukosten = Material + 0 Brokergebühr + Job-Gebühr.
        Assert.Equal(214198m, e.BuildCost);
        // Marktseite ohne Broker: Buy = 200000; Ersparnis bleibt berechenbar.
        Assert.Equal(200000m, e.BuyCost);
        Assert.Equal(178200m, e.SellProceedsNet);
        Assert.Equal(-14198m, e.BuildVsBuySavings);
    }

    [Fact]
    public async Task UnknownFacilityTax_JobCostUnknown_WithVisibleReason_NeverZero()
    {
        var (service, db) = Create(BantamRecipe(), new MarketHubProfile { Name = "The Forge", RegionId = ComparisonRegion });
        SeedPrices(db);

        var result = await service.CalculateAsync(BantamBlueprint, Assumptions(costIndex: 5, facilityTax: null));

        Assert.True(result.IsSuccess);
        var e = result.Estimate!;
        Assert.Equal(8850m, e.SystemCostIndexFee);
        Assert.Null(e.FacilityTax);
        Assert.Equal(7080m, e.SccSurcharge);
        Assert.Null(e.JobCost);
        Assert.Null(e.BuildCost);
        Assert.Null(e.BuildVsBuySavings);
        Assert.Contains(e.UnknownNotes, n => n.Contains("Struktursteuer") && n.Contains("unbekannt"));
    }

    [Fact]
    public async Task MissingMaterialPrice_MaterialCostAndItemValueUnknown_WithNamedReason()
    {
        var recipe = BantamRecipe();
        // Isogen (37) ohne Snapshot → Materialkosten unbekannt.
        var (service, db) = Create(recipe, new MarketHubProfile { Name = "The Forge", RegionId = ComparisonRegion });
        db.MarketSnapshots.AddRange(
            new MarketSnapshot
            {
                RegionId = ComparisonRegion, TypeId = 34, Timestamp = DateTime.UtcNow,
                BestSellPrice = 4.0, BestBuyPrice = 3.5
            },
            new MarketSnapshot
            {
                RegionId = ComparisonRegion, TypeId = 35, Timestamp = DateTime.UtcNow,
                BestSellPrice = 8.0, BestBuyPrice = 7.5
            },
            new MarketSnapshot
            {
                RegionId = ComparisonRegion, TypeId = 36, Timestamp = DateTime.UtcNow,
                BestSellPrice = 16.0, BestBuyPrice = 15.0
            });
        db.SaveChanges();

        var result = await service.CalculateAsync(BantamBlueprint, Assumptions());

        Assert.True(result.IsSuccess);
        var e = result.Estimate!;
        Assert.Null(e.MaterialCost);
        Assert.Null(e.EstimatedItemValue);
        Assert.Null(e.JobCost);
        Assert.Null(e.BuildCost);
        Assert.Contains(e.UnknownNotes, n => n.Contains("Type 37"));
        // Bekannte Materialien behalten ihren Kurs — die Zeile ist sichtbar, nur die Summe nicht.
        Assert.Equal(194728m - 16520m, e.Materials.Where(m => m.UnitPrice.HasValue).Sum(m => m.LineCost!.Value));
        Assert.Null(e.Materials.Single(m => m.MaterialTypeId == 37).UnitPrice);
    }

    [Fact]
    public async Task RunsBeyondBlueprintLimit_ReturnsFailure()
    {
        var (service, _) = Create(BantamRecipe(), comparison: null);

        var result = await service.CalculateAsync(BantamBlueprint, Assumptions(runs: 50));

        Assert.False(result.IsSuccess);
        Assert.Contains("überschreiten", result.FailureReason);
    }

    [Fact]
    public async Task CopyWithoutRemainingRuns_ReturnsFailure()
    {
        var (service, _) = Create(BantamRecipe(), comparison: null);

        var result = await service.CalculateAsync(BantamBlueprint, Assumptions(isCopy: true, remainingRuns: null));

        Assert.False(result.IsSuccess);
        Assert.Contains("Verbleibende Runs", result.FailureReason);
    }
}