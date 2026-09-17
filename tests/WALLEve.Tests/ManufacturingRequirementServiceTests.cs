using WALLEve.Models.Industry;
using WALLEve.Services.Industry;
using WALLEve.Services.Industry.Interfaces;
using WALLEve.Services.Sde.Interfaces;

namespace WALLEve.Tests;

/// <summary>
/// Orchestrierungs-Tests der Materialbedarfs-Berechnung (#56): unbekanntes
/// Rezept oder fehlende Parameter dürfen NIE zu einem Nullbedarf oder einer
/// Produktionszusage führen; BPO/BPC-Runs-Limits werden durchgesetzt.
/// </summary>
public class ManufacturingRequirementServiceTests
{
    private const int BantamBlueprint = 683;

    private static ManufacturingRecipe BantamRecipe(int? maxProductionLimit = 30) => new()
    {
        BlueprintTypeId = BantamBlueprint,
        ProductTypeId = 582,        // Bantam
        ProductQuantity = 1,
        BaseTimeSeconds = 6000,
        MaxProductionLimit = maxProductionLimit,
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

    private static (ManufacturingRequirementService Service, FakeSdeIndustryRepository Repository) Create(
        ManufacturingRecipe? recipe)
    {
        var repository = new FakeSdeIndustryRepository { RecipeToReturn = recipe };
        return (new ManufacturingRequirementService(repository), repository);
    }

    [Fact]
    public async Task UnknownRecipe_ReturnsFailure_NeverZeroMaterials()
    {
        var (service, _) = Create(recipe: null);

        var result = await service.CalculateAsync(BantamBlueprint, 0, 0, 1, isCopy: false, remainingRuns: null);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Materials);
        Assert.Equal(0, result.ProductQuantity);
        Assert.NotNull(result.FailureReason);
    }

    [Fact]
    public async Task RecipeWithoutMaterials_ReturnsMissingParametersFailure()
    {
        var recipe = BantamRecipe() with { Materials = Array.Empty<RecipeMaterial>() };
        var (service, _) = Create(recipe);

        var result = await service.CalculateAsync(BantamBlueprint, 0, 0, 1, isCopy: false, remainingRuns: null);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.FailureReason);
        Assert.Contains("keine Materialien", result.FailureReason);
    }

    [Fact]
    public async Task RecipeWithoutProductQuantity_ReturnsMissingParametersFailure()
    {
        var recipe = BantamRecipe() with { ProductQuantity = 0 };
        var (service, _) = Create(recipe);

        var result = await service.CalculateAsync(BantamBlueprint, 0, 0, 1, isCopy: false, remainingRuns: null);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.FailureReason);
        Assert.Contains("Produktmenge", result.FailureReason);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(11)]
    public async Task OutOfRangeMe_ReturnsFailure_InsteadOfSilentCorrection(int me)
    {
        var (service, _) = Create(BantamRecipe());

        var result = await service.CalculateAsync(BantamBlueprint, me, 0, 1, isCopy: false, remainingRuns: null);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.FailureReason);
    }

    [Fact]
    public async Task RunsAboveBpoLimit_ReturnsInvalidRunsFailure()
    {
        // BPO Bantam: SDE-Limit 30 Runs je Job.
        var (service, _) = Create(BantamRecipe(maxProductionLimit: 30));

        var result = await service.CalculateAsync(BantamBlueprint, 0, 0, 31, isCopy: false, remainingRuns: null);

        Assert.False(result.IsSuccess);
        Assert.Contains("Limit", result.FailureReason);
    }

    [Fact]
    public async Task RunsExactlyAtBpoLimit_IsAllowed()
    {
        var (service, _) = Create(BantamRecipe(maxProductionLimit: 30));

        var result = await service.CalculateAsync(BantamBlueprint, 0, 0, 30, isCopy: false, remainingRuns: null);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task BpcRunsCappedByRemainingRuns()
    {
        var (service, _) = Create(BantamRecipe(maxProductionLimit: 30));

        var result = await service.CalculateAsync(
            BantamBlueprint, 0, 0, 11, isCopy: true, remainingRuns: 10);

        Assert.False(result.IsSuccess);
        Assert.Contains("Limit", result.FailureReason);
    }

    [Fact]
    public async Task BpcWithoutRemainingRuns_ReturnsMissingParametersFailure()
    {
        var (service, _) = Create(BantamRecipe());

        var result = await service.CalculateAsync(
            BantamBlueprint, 0, 0, 1, isCopy: true, remainingRuns: null);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.FailureReason);
    }

    [Fact]
    public async Task ValidSingleRunBpo_ReturnsDeterministicTotals()
    {
        var (service, _) = Create(BantamRecipe());

        var result = await service.CalculateAsync(BantamBlueprint, 0, 0, 1, isCopy: false, remainingRuns: null);

        Assert.True(result.IsSuccess);
        Assert.Equal(6000, result.TotalTimeSeconds);
        Assert.Equal(1, result.ProductQuantity);
        Assert.NotNull(result.Materials);
        Assert.Equal(4, result.Materials.Count);
        Assert.Equal((34, 26400L), (result.Materials[0].MaterialTypeId, result.Materials[0].RequiredQuantity));
        Assert.Equal((37, 413L), (result.Materials[3].MaterialTypeId, result.Materials[3].RequiredQuantity));
    }

    [Fact]
    public async Task MultiRunJob_ProductQuantityScalesWithRuns()
    {
        var (service, _) = Create(BantamRecipe());

        var result = await service.CalculateAsync(BantamBlueprint, 10, 20, 5, isCopy: false, remainingRuns: null);

        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.ProductQuantity);
        // 6000 s × (1 - 0,2) je Run, dann × 5.
        Assert.Equal(24000, result.TotalTimeSeconds);
        // Tritanium: ceil(24000 × 5 × (1 + 0,1/11)) = ceil(121090,9…) = 121091.
        Assert.Equal(121091, result.Materials![0].RequiredQuantity);
    }
}