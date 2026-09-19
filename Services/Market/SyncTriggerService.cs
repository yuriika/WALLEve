using Microsoft.EntityFrameworkCore;
using WALLEve.Data;
using WALLEve.Models.Database;
using WALLEve.Services.Market.Interfaces;

namespace WALLEve.Services.Market;

public class SyncTriggerService : ISyncTriggerService
{
    private readonly WalletDbContext _db;
    private readonly ICostBasisService _costBasis;
    private readonly ISyncWakeService _wake;
    private readonly IBackgroundJobStatusNotifier? _statusNotifier;

    internal const string ForcePrefix = "ForceRun.";

    public SyncTriggerService(WalletDbContext db, ICostBasisService costBasis, ISyncWakeService wake,
        IBackgroundJobStatusNotifier? statusNotifier = null)
    {
        _db = db;
        _costBasis = costBasis;
        _wake = wake;
        _statusNotifier = statusNotifier;
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
                _wake.Signal(); // Collector sofort wecken, statt bis zum 60s-Takt zu warten
                return true;

            default: // Automatischer Sync → Force-Flag + sofort wecken
                var key = ForceKey(characterId, jobType);
                var forceFlag = await _db.AppSettings.FindAsync(key);
                if (forceFlag == null)
                {
                    _db.AppSettings.Add(new AppSetting { Key = key, Value = DateTime.UtcNow.ToString("o") });
                }
                else
                {
                    forceFlag.Value = DateTime.UtcNow.ToString("o");
                }
                await _db.SaveChangesAsync();
                _wake.Signal();
                _statusNotifier?.NotifyStatusChanged();
                return true;
        }
    }

    public async Task<int> TriggerScheduledNowAsync(int characterId)
    {
        var queued = 0;
        foreach (var jobType in SyncOverviewService.ScheduledJobTypes)
        {
            if (await TriggerNowAsync(characterId, jobType)) queued++;
        }
        return queued;
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

    internal static string ForceKey(int characterId, string jobType) => $"{ForcePrefix}{jobType}.{characterId}";
}