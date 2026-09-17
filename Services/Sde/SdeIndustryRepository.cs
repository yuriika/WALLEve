using System.Data;
using Microsoft.Data.Sqlite;
using WALLEve.Models.Industry;
using WALLEve.Services.Sde.Interfaces;

namespace WALLEve.Services.Sde;

/// <summary>
/// SDE-Zugriff auf Fertigungsdaten (#56), gelesen aus dem Fuzzwork-SQLite-Dump:
/// industryActivityMaterials (Basismenge je Run), industryActivityProducts
/// (Produktmenge), industryActivity (Basiszeit, Activity 1 = Manufacturing) und
/// industryBlueprints (maxProductionLimit). Kein Cache — Rezepte sind klein und
/// der Aufruf erfolgt je Berechnung; die SDE selbst ist statisch.
/// </summary>
public class SdeIndustryRepository : ISdeIndustryRepository
{
    private const int ManufacturingActivityId = 1;

    private readonly SdeDbContext _context;
    private readonly ILogger<SdeIndustryRepository> _logger;

    public SdeIndustryRepository(SdeDbContext context, ILogger<SdeIndustryRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<ManufacturingRecipe?> GetManufacturingRecipeAsync(
        int blueprintTypeId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _context.EnsureConnectionAsync();

            // Unterscheidet „kein industryBlueprints-Eintrag“ von „Eintrag existiert mit
            // SQL-NULL-Limit“: NULL maxProductionLimit ist laut Modell-Vertrag ein
            // UNBEGRENZTES Produktionslimit (siehe ManufacturingMath.MaxAllowedRuns) und
            // darf das Rezept nicht zu UnknownRecipe machen.
            var (blueprintFound, limit) = await QueryMaxProductionLimitAsync(blueprintTypeId, cancellationToken);
            if (!blueprintFound)
            {
                // Kein industryBlueprints-Eintrag → kein Rezept (auch wenn Materialzeilen existieren).
                return null;
            }

            var product = await QueryProductAsync(blueprintTypeId, cancellationToken);
            if (product is null)
            {
                // Blueprint existiert in industryBlueprints, aber industryActivityProducts
                // hat keinen Produkt-Eintrag für Activity 1: kein Rezept ableitbar.
                // Expliziter Fehlerpfad (null → Service liefert IsSuccess=false),
                // niemals eine NullReferenceException auf product!.Value.
                _logger.LogWarning(
                    "SDE recipe for blueprint {BlueprintTypeId} has no manufacturing product row; treated as unknown recipe",
                    blueprintTypeId);
                return null;
            }

            var materials = await QueryMaterialsAsync(blueprintTypeId, cancellationToken);
            var baseTime = await QueryBaseTimeAsync(blueprintTypeId, cancellationToken);
            if (baseTime is null)
            {
                // Keine Fertigungszeit (industryActivity, Activity 1): Produktionszeit
                // wäre 0 → keine gültige Produktionszusage. Expliziter Fehlerpfad.
                _logger.LogWarning(
                    "SDE recipe for blueprint {BlueprintTypeId} has no manufacturing activity time row; treated as unknown recipe",
                    blueprintTypeId);
                return null;
            }

            return new ManufacturingRecipe
            {
                BlueprintTypeId = blueprintTypeId,
                ProductTypeId = product.Value.ProductTypeId,
                ProductQuantity = product.Value.Quantity,
                BaseTimeSeconds = baseTime.Value,
                MaxProductionLimit = limit,
                Materials = materials.ToList()
            };
        }
        catch (SqliteException ex)
        {
            _logger.LogError(ex, "SDE error loading manufacturing recipe for blueprint {BlueprintTypeId}", blueprintTypeId);
            return null;
        }
    }

    private async Task<(bool Found, int? Limit)> QueryMaxProductionLimitAsync(int blueprintTypeId, CancellationToken ct)
    {
        using var cmd = _context.Connection.CreateCommand();
        cmd.CommandText = "SELECT maxProductionLimit FROM industryBlueprints WHERE typeID = @blueprintTypeId";
        cmd.Parameters.AddWithValue("@blueprintTypeId", blueprintTypeId);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            // Kein industryBlueprints-Eintrag.
            return (Found: false, Limit: null);
        }

        // Zeile existiert; NULL maxProductionLimit = unbegrenztes Produktionslimit.
        return (Found: true, Limit: reader.IsDBNull(0) ? null : reader.GetInt32(0));
    }

    private async Task<(int ProductTypeId, long Quantity)?> QueryProductAsync(int blueprintTypeId, CancellationToken ct)
    {
        using var cmd = _context.Connection.CreateCommand();
        cmd.CommandText = @"
            SELECT productTypeID, quantity
            FROM industryActivityProducts
            WHERE typeID = @blueprintTypeId AND activityID = @activityId
            ORDER BY productTypeID
            LIMIT 1";
        cmd.Parameters.AddWithValue("@blueprintTypeId", blueprintTypeId);
        cmd.Parameters.AddWithValue("@activityId", ManufacturingActivityId);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        var productTypeId = reader.GetInt32(0);
        var quantity = reader.IsDBNull(1) ? 0 : reader.GetInt64(1);
        return (productTypeId, quantity);
    }

    private async Task<List<RecipeMaterial>> QueryMaterialsAsync(int blueprintTypeId, CancellationToken ct)
    {
        var materials = new List<RecipeMaterial>();
        using var cmd = _context.Connection.CreateCommand();
        cmd.CommandText = @"
            SELECT materialTypeID, quantity
            FROM industryActivityMaterials
            WHERE typeID = @blueprintTypeId AND activityID = @activityId
            ORDER BY quantity DESC, materialTypeID";
        cmd.Parameters.AddWithValue("@blueprintTypeId", blueprintTypeId);
        cmd.Parameters.AddWithValue("@activityId", ManufacturingActivityId);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var materialTypeId = reader.GetInt32(0);
            var quantity = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
            if (quantity > 0)
            {
                materials.Add(new RecipeMaterial(materialTypeId, quantity));
            }
        }

        return materials;
    }

    private async Task<int?> QueryBaseTimeAsync(int blueprintTypeId, CancellationToken ct)
    {
        using var cmd = _context.Connection.CreateCommand();
        cmd.CommandText = "SELECT time FROM industryActivity WHERE typeID = @blueprintTypeId AND activityID = @activityId";
        cmd.Parameters.AddWithValue("@blueprintTypeId", blueprintTypeId);
        cmd.Parameters.AddWithValue("@activityId", ManufacturingActivityId);

        var result = await cmd.ExecuteScalarAsync(ct);
        return result is null or DBNull ? null : Convert.ToInt32(result);
    }
}