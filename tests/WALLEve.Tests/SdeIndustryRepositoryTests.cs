using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WALLEve.Configuration;
using WALLEve.Services.Industry;
using WALLEve.Services.Sde;

namespace WALLEve.Tests;

/// <summary>
/// Repository/Service-Grenztest für Issue #56: Ein Blueprint, der in
/// industryBlueprints existiert, aber keinen Produkt- oder Fertigungszeit-Eintrag
/// hat, muss einen expliziten Fehlerpfad (kein Rezept → IsSuccess=false) liefern —
/// niemals eine NullReferenceException und nie eine Produktionszusage mit Zeit 0.
/// Läuft gegen eine echte, temporäre SDE-SQLite-Datei (read-only geöffnet, wie in Produktion).
/// </summary>
public class SdeIndustryRepositoryTests
{
    private const int BantamBlueprint = 683;
    private const string SdeFileName = "sde.sqlite";

    private sealed class SdeFixture : IAsyncDisposable
    {
        public required string DbPath { get; init; }
        public required string DataFolder { get; init; }

        public async ValueTask DisposeAsync()
        {
            if (Directory.Exists(DataFolder))
            {
                Directory.Delete(DataFolder, recursive: true);
            }
            await ValueTask.CompletedTask;
        }
    }

    private static async Task<SdeFixture> CreateSdeFileAsync()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dataFolder = Path.Combine(baseDir, "WALLEve.Tests", $"issue56-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dataFolder);
        var dbPath = Path.Combine(dataFolder, SdeFileName);

        await using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = """
                    CREATE TABLE industryBlueprints (typeID INTEGER PRIMARY KEY, maxProductionLimit INTEGER);
                    CREATE TABLE industryActivityProducts (typeID INTEGER, activityID INTEGER, productTypeID INTEGER, quantity INTEGER);
                    CREATE TABLE industryActivityMaterials (typeID INTEGER, activityID INTEGER, materialTypeID INTEGER, quantity INTEGER);
                    CREATE TABLE industryActivity (typeID INTEGER, activityID INTEGER, time INTEGER);

                    -- Vollständiges Bantam-Rezept (683 → 582, 1 je Run, 6000 s, Limit 30).
                    INSERT INTO industryBlueprints VALUES (683, 30);
                    INSERT INTO industryActivityProducts VALUES (683, 1, 582, 1);
                    INSERT INTO industryActivityMaterials VALUES
                        (683, 1, 34, 24000), (683, 1, 35, 4500), (683, 1, 36, 1875), (683, 1, 37, 375);
                    INSERT INTO industryActivity VALUES (683, 1, 6000);

                    -- Blueprint OHNE Produkt-Eintrag (industryActivityProducts leer).
                    INSERT INTO industryBlueprints VALUES (684, 30);
                    INSERT INTO industryActivityMaterials VALUES (684, 1, 34, 24000);
                    INSERT INTO industryActivity VALUES (684, 1, 6000);

                    -- Blueprint mit Produkt- und Materialzeilen, aber OHNE Fertigungszeit (industryActivity leer).
                    INSERT INTO industryBlueprints VALUES (685, 30);
                    INSERT INTO industryActivityProducts VALUES (685, 1, 585, 1);
                    INSERT INTO industryActivityMaterials VALUES (685, 1, 34, 24000);
                    """;
                await cmd.ExecuteNonQueryAsync();
            }
        }

        return new SdeFixture { DbPath = dbPath, DataFolder = dataFolder };
    }

    private static SdeDbContext CreateContext(SdeFixture fixture)
    {
        var eveSettings = Options.Create(new EveOnlineSettings
        {
            Sde = new SdeSettings { LocalFileName = SdeFileName }
        });
        var appSettings = Options.Create(new ApplicationSettings
        {
            AppDataFolder = "WALLEve.Tests",
            DataFolder = Path.GetFileName(fixture.DataFolder)!
        });
        return new SdeDbContext(eveSettings, appSettings, NullLogger<SdeDbContext>.Instance);
    }

    [Fact]
    public async Task BlueprintWithoutProductRow_RepositoryReturnsNull_ServiceReturnsExplicitFailure()
    {
        await using var fixture = await CreateSdeFileAsync();
        using var context = CreateContext(fixture);
        var repository = new SdeIndustryRepository(context, NullLogger<SdeIndustryRepository>.Instance);

        // Repository-Grenze: kein Rezept statt NullReferenceException.
        var recipe = await repository.GetManufacturingRecipeAsync(684);
        Assert.Null(recipe);

        // Service-Grenze: expliziter Fehlschlag, keine Nullkosten, keine Produktionszusage.
        var service = new ManufacturingRequirementService(repository);
        var result = await service.CalculateAsync(684, 0, 0, 1, isCopy: false, remainingRuns: null);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Materials);
        Assert.Equal(0, result.ProductQuantity);
        Assert.NotNull(result.FailureReason);
    }

    [Fact]
    public async Task BlueprintWithoutBaseTimeRow_RepositoryReturnsNull_ServiceReturnsExplicitFailure()
    {
        await using var fixture = await CreateSdeFileAsync();
        using var context = CreateContext(fixture);
        var repository = new SdeIndustryRepository(context, NullLogger<SdeIndustryRepository>.Instance);

        // Repository-Grenze: kein Rezept statt Produktionszeit 0.
        var recipe = await repository.GetManufacturingRecipeAsync(685);
        Assert.Null(recipe);

        // Service-Grenze: kein Job mit Zeit 0 (keine falsche Produktionszusage).
        var service = new ManufacturingRequirementService(repository);
        var result = await service.CalculateAsync(685, 0, 0, 1, isCopy: false, remainingRuns: null);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Materials);
        Assert.Equal(0, result.TotalTimeSeconds);
        Assert.NotNull(result.FailureReason);
    }

    [Fact]
    public async Task CompleteRecipe_RepositoryLoadsAllFields_ServiceSucceeds()
    {
        await using var fixture = await CreateSdeFileAsync();
        using var context = CreateContext(fixture);
        var repository = new SdeIndustryRepository(context, NullLogger<SdeIndustryRepository>.Instance);

        // Repository-Grenze: alle Rezeptfelder aus allen vier SDE-Tabellen geladen.
        var recipe = await repository.GetManufacturingRecipeAsync(BantamBlueprint);
        Assert.NotNull(recipe);
        Assert.Equal(582, recipe.ProductTypeId);
        Assert.Equal(1, recipe.ProductQuantity);
        Assert.Equal(6000, recipe.BaseTimeSeconds);
        Assert.Equal(30, recipe.MaxProductionLimit);
        Assert.Equal(4, recipe.Materials.Count);

        // Service-Grenze: identisches Ergebnis wie der Fake-basierte Service-Test (26400 Tritanium).
        var service = new ManufacturingRequirementService(repository);
        var result = await service.CalculateAsync(BantamBlueprint, 0, 0, 1, isCopy: false, remainingRuns: null);

        Assert.True(result.IsSuccess);
        Assert.Equal(6000, result.TotalTimeSeconds);
        Assert.Equal(1, result.ProductQuantity);
        Assert.Equal((34, 26400L), (result.Materials![0].MaterialTypeId, result.Materials[0].RequiredQuantity));
    }
}