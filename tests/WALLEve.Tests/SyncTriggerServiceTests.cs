using Microsoft.EntityFrameworkCore;
using WALLEve.Data;
using WALLEve.Models.Database;
using WALLEve.Services.Market;
using WALLEve.Services.Market.Interfaces;

namespace WALLEve.Tests;

/// <summary>
/// Tests für die manuelle Sync-Auslösung (Force-Flag + Scan-Start).
/// </summary>
public class SyncTriggerServiceTests
{
    private const int CharacterId = 90073315;

    private sealed class FakeCostBasis : ICostBasisService
    {
        public long LastScanJobId { get; set; }
        public int ScanRegion { get; set; }
        public string EstimateJobType => "CostBasisEstimate";
        public string InventoryScanJobType => "InventoryScan";
        public IReadOnlyDictionary<int, string> KnownRegions => new Dictionary<int, string>();
        public Task<List<CostBasisItemView>> GetItemsAsync(int characterId) => Task.FromResult(new List<CostBasisItemView>());
        public Task<double?> GetCostBasisPerUnitAsync(int characterId, int typeId) => Task.FromResult<double?>(null);
        public Task<long> StartEstimateJobAsync(int characterId, IEnumerable<int> typeIds, int regionId) => Task.FromResult(1L);
        public Task<long> StartInventoryScanAsync(int characterId, int regionId)
        {
            ScanRegion = regionId;
            LastScanJobId = 99;
            return Task.FromResult(99L);
        }
        public Task SetManualValueAsync(int characterId, int typeId, double value, DateTime? purchaseDate = null) => Task.CompletedTask;
        public Task ResetEntryAsync(int characterId, int typeId) => Task.CompletedTask;
        public Task<BackgroundJob?> GetActiveJobAsync(string jobType, int? characterId = null) => Task.FromResult<BackgroundJob?>(null);
        public Task<int> GetDefaultEstimateRegionAsync() => Task.FromResult(10000002);
        public Task SetDefaultEstimateRegionAsync(int regionId) => Task.CompletedTask;
    }

    private static SyncTriggerService CreateService(WalletDbContext db, FakeCostBasis? fake = null)
        => new(db, fake ?? new FakeCostBasis(), new SyncWakeService());

    [Fact]
    public async Task TriggerSink_SetsForceFlag()
    {
        var db = TestDb.Create();
        var service = CreateService(db);

        var ok = await service.TriggerNowAsync(CharacterId, "CostBasisSink");

        Assert.True(ok);
        var setting = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == $"ForceRun.CostBasisSink.{CharacterId}");
        Assert.NotNull(setting);
    }

    [Fact]
    public async Task TriggerSink_NotifiesSubscribersThatExecutionIsQueued()
    {
        var db = TestDb.Create();
        var notifier = new BackgroundJobStatusNotifier();
        var notifications = 0;
        notifier.StatusChanged += () => notifications++;
        var service = new SyncTriggerService(db, new FakeCostBasis(), new SyncWakeService(), notifier);

        await service.TriggerNowAsync(CharacterId, "CostBasisSink");

        Assert.Equal(1, notifications);
    }

    [Fact]
    public async Task ConsumeForce_OnceOnly_RemovesFlag()
    {
        var db = TestDb.Create();
        var service = CreateService(db);
        await service.TriggerNowAsync(CharacterId, "CostBasisSink");

        Assert.True(await service.ConsumeForceAsync(CharacterId, "CostBasisSink"));
        Assert.False(await service.ConsumeForceAsync(CharacterId, "CostBasisSink")); // weg
    }

    [Fact]
    public async Task TriggerEstimate_IsNotDirectlyTriggerable()
    {
        var db = TestDb.Create();
        var service = CreateService(db);

        var ok = await service.TriggerNowAsync(CharacterId, "CostBasisEstimate");

        Assert.False(ok);
    }

    [Fact]
    public async Task TriggerScan_StartsJobDirectly()
    {
        var db = TestDb.Create();
        var fake = new FakeCostBasis();
        var service = CreateService(db, fake);

        var ok = await service.TriggerNowAsync(CharacterId, "InventoryScan");

        Assert.True(ok);
        Assert.Equal(99, fake.LastScanJobId);
        Assert.Equal(10000002, fake.ScanRegion); // Default-Region Jita
    }
}