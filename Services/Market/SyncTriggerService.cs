using Microsoft.EntityFrameworkCore;
using WALLEve.Data;
using WALLEve.Models.Database;
using WALLEve.Services.Market.Interfaces;

namespace WALLEve.Services.Market;

public class SyncTriggerService : ISyncTriggerService
{
    private readonly WalletDbContext _db;
    private readonly ICostBasisService _costBasis;

    private const string ForcePrefix = "ForceRun.";

    public SyncTriggerService(WalletDbContext db, ICostBasisService costBasis)
    {
        _db = db;
        _costBasis = costBasis;
    }

    public async Task<bool> TriggerNowAsync(int characterId, string jobType)
    {
        switch (jobType)
        {
            case "CostBasisEstimate":
                // Braucht explizite Item-Auswahl → nicht direkt auslösbar
                return false;

            case "InventoryScan":
                var regionId = await _costBasis.GetDefaultEstimateRegionAsync();
                await _costBasis.StartInventoryScanAsync(characterId, regionId);
                return true;

            default: // CostBasisSink → Force-Flag für den Collector
                _db.AppSettings.Add(new AppSetting
                {
                    Key = ForceKey(characterId, jobType),
                    Value = DateTime.UtcNow.ToString("o")
                });
                await _db.SaveChangesAsync();
                return true;
        }
    }

    public async Task<bool> ConsumeForceAsync(int characterId, string jobType)
    {
        var key = ForceKey(characterId, jobType);
        var setting = await _db.AppSettings.FindAsync(key);
        if (setting == null) return false;

        _db.AppSettings.Remove(setting);
        await _db.SaveChangesAsync();
        return true;
    }

    private static string ForceKey(int characterId, string jobType) => $"{ForcePrefix}{jobType}.{characterId}";
}