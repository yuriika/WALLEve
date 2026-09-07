using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WALLEve.Data;
using WALLEve.Models.Database;
using WALLEve.Services.Market.Interfaces;

namespace WALLEve.Services.Market;

public class CostBasisService : ICostBasisService
{
    private readonly WalletDbContext _db;
    private readonly IInventoryService _inventoryService;
    private readonly IBackgroundJobManager _jobManager;

    public const string EstimateJobTypeConst = "CostBasisEstimate";
    public const string DefaultRegionSettingKey = "CostBasis.DefaultRegionId";
    public const int DefaultRegionId = 10000002; // The Forge (Jita)

    public string EstimateJobType => EstimateJobTypeConst;

    // Feste Regionen-Liste für die Hub-Auswahl (Id → Name)
    public IReadOnlyDictionary<int, string> KnownRegions { get; } =
        new Dictionary<int, string>
        {
            { 10000002, "The Forge (Jita)" },
            { 10000043, "Domain (Amarr)" },
            { 10000032, "Sinq Laison (Dodixie)" },
            { 10000030, "Heimatar (Rens)" },
            { 10000042, "Metropolis (Hek)" }
        };

    public CostBasisService(
        WalletDbContext db,
        IInventoryService inventoryService,
        IBackgroundJobManager jobManager)
    {
        _db = db;
        _inventoryService = inventoryService;
        _jobManager = jobManager;
    }

    public async Task<double?> GetCostBasisPerUnitAsync(int characterId, int typeId)
    {
        var entry = await _db.CostBasisEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.CharacterId == characterId && e.TypeId == typeId);

        return entry?.Value;
    }

    public async Task<List<CostBasisItemView>> GetItemsAsync(int characterId)
    {
        var items = await _inventoryService.GetInventoryAsync(characterId);
        var entries = await _db.CostBasisEntries
            .Where(e => e.CharacterId == characterId)
            .ToDictionaryAsync(e => e.TypeId);

        return items.Select(i =>
        {
            entries.TryGetValue(i.TypeId, out var entry);
            return new CostBasisItemView
            {
                TypeId = i.TypeId,
                TypeName = i.TypeName,
                TotalQuantity = i.TotalQuantity,
                MarketValue = i.CurrentMarketValue,
                SellPrice = i.BestSellPrice,
                Source = entry?.Source ?? CostBasisSource.None,
                Value = entry?.Value,
                PurchaseDate = entry?.PurchaseDate,
                EstimateRegionId = entry?.EstimateRegionId,
                UpdatedAt = entry?.UpdatedAt
            };
        }).ToList();
    }

    public async Task<long> StartEstimateJobAsync(int characterId, IEnumerable<int> typeIds, int regionId)
    {
        var ids = typeIds.Distinct().ToList();
        if (ids.Count == 0) throw new ArgumentException("Keine Items ausgewählt.");

        // Nur Items ohne endgültigen Wert (None oder Estimate) schätzen;
        // manuelle/echte Werte bleiben unangetastet.
        var skipIds = await _db.CostBasisEntries
            .Where(e => e.CharacterId == characterId
                     && (e.Source == CostBasisSource.Manual
                      || e.Source == CostBasisSource.Transaction))
            .Select(e => e.TypeId)
            .ToListAsync();

        var targets = ids.Where(id => !skipIds.Contains(id)).ToList();
        if (targets.Count == 0) throw new InvalidOperationException(
            "Alle ausgewählten Items haben bereits einen echten oder manuellen Wert.");

        var parameters = JsonSerializer.Serialize(new
        {
            typeIds = targets,
            regionId
        });

        var job = await _jobManager.CreateJobAsync(EstimateJobType, "Einkaufspreise schätzen",
            characterId, total: targets.Count, parametersJson: parameters);
        return job.Id;
    }

    public async Task SetManualValueAsync(int characterId, int typeId, double value, DateTime? purchaseDate = null)
    {
        var entry = await _db.CostBasisEntries
            .FirstOrDefaultAsync(e => e.CharacterId == characterId && e.TypeId == typeId);

        if (entry == null)
        {
            entry = new CostBasisEntry
            {
                CharacterId = characterId,
                TypeId = typeId,
                Value = value,
                Source = CostBasisSource.Manual,
                PurchaseDate = purchaseDate,
                EstimateRegionId = null,
                UpdatedAt = DateTime.UtcNow
            };
            _db.CostBasisEntries.Add(entry);
        }
        else
        {
            // Manueller Wert gewinnt immer — egal welche Quelle vorher stand.
            entry.Value = value;
            entry.Source = CostBasisSource.Manual;
            entry.PurchaseDate = purchaseDate;
            entry.EstimateRegionId = null;
            entry.UpdatedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync();
    }

    public async Task ResetEntryAsync(int characterId, int typeId)
    {
        var entry = await _db.CostBasisEntries
            .FirstOrDefaultAsync(e => e.CharacterId == characterId && e.TypeId == typeId);
        if (entry == null) return;
        _db.CostBasisEntries.Remove(entry);
        await _db.SaveChangesAsync();
    }

    public async Task<BackgroundJob?> GetActiveJobAsync(string jobType, int? characterId = null)
    {
        var query = _db.BackgroundJobs.Where(j => j.JobType == jobType);
        if (characterId.HasValue)
        {
            query = query.Where(j => j.CharacterId == characterId.Value);
        }
        return await query
            .Where(j => j.Status == BackgroundJobStatus.Running
                     || j.Status == BackgroundJobStatus.Paused
                     || j.Status == BackgroundJobStatus.Interrupted)
            .OrderByDescending(j => j.UpdatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<int> GetDefaultEstimateRegionAsync()
    {
        var setting = await _db.AppSettings.FindAsync(DefaultRegionSettingKey);
        if (setting != null && int.TryParse(setting.Value, out var regionId)
            && KnownRegions.ContainsKey(regionId))
        {
            return regionId;
        }
        return DefaultRegionId;
    }

    public async Task SetDefaultEstimateRegionAsync(int regionId)
    {
        if (!KnownRegions.ContainsKey(regionId)) return;
        var setting = await _db.AppSettings.FindAsync(DefaultRegionSettingKey);
        if (setting == null)
        {
            _db.AppSettings.Add(new AppSetting
            {
                Key = DefaultRegionSettingKey,
                Value = regionId.ToString()
            });
        }
        else
        {
            setting.Value = regionId.ToString();
        }
        await _db.SaveChangesAsync();
    }
}