using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WALLEve.Data;
using WALLEve.Models.Database;
using WALLEve.Services.Market;
using WALLEve.Services.Market.Interfaces;

namespace WALLEve.Tests;

/// <summary>
/// Tests für die Cost-Basis-Fachlogik: manuelle Werte gewinnen immer,
/// Reset, Schätz-Job-Anlage (nur offene/geschätzte Items, gesperrte
/// ausgenommen) und die persistierte Standard-Schätzregion.
/// </summary>
public class CostBasisServiceTests
{
    private const int CharacterId = 90073315;

    private sealed class FakeInventoryService : IInventoryService
    {
        public List<InventoryItem> Items { get; } = new();

        public Task<List<InventoryItem>> GetInventoryAsync(int characterId) => Task.FromResult(Items);

        public Task<PortfolioOverview> GetPortfolioOverviewAsync(int characterId)
            => Task.FromResult(new PortfolioOverview());

        public Task<List<InventoryItem>> GetPrioritizedItemsAsync(int characterId,
            InventorySortMode sortMode = InventorySortMode.Opportunity)
            => Task.FromResult(Items);
    }

    private static (CostBasisService Service, WalletDbContext Db) CreateSut()
    {
        var db = TestDb.Create();
        var inventory = new FakeInventoryService
        {
            Items =
            {
                new InventoryItem { TypeId = 1, TypeName = "Tritanium", TotalQuantity = 1000, BestSellPrice = 4.0 },
                new InventoryItem { TypeId = 2, TypeName = "Pyerite", TotalQuantity = 500, BestSellPrice = 18.0 },
                new InventoryItem { TypeId = 3, TypeName = "Megacyte", TotalQuantity = 10, BestSellPrice = 5000.0 }
            }
        };
        var jobManager = new BackgroundJobManager(db);
        var service = new CostBasisService(db, inventory, jobManager);
        return (service, db);
    }

    // ------------------------------------------------------------------
    // Manuelle Werte
    // ------------------------------------------------------------------

    [Fact]
    public async Task SetManualValue_CreatesEntryWithManualSource()
    {
        var (service, db) = CreateSut();

        await service.SetManualValueAsync(CharacterId, 1, 3.5, new DateTime(2026, 8, 1));

        var entry = await db.CostBasisEntries.FirstAsync(e => e.TypeId == 1);
        Assert.Equal(CostBasisSource.Manual, entry.Source);
        Assert.Equal(3.5, entry.Value);
        Assert.Equal(new DateTime(2026, 8, 1), entry.PurchaseDate);
    }

    [Fact]
    public async Task SetManualValue_OverwritesExistingEntry_RegardlessOfSource()
    {
        var (service, db) = CreateSut();

        // Erst ein "echter" (Transaktions-)Eintrag...
        db.CostBasisEntries.Add(new CostBasisEntry
        {
            CharacterId = CharacterId, TypeId = 1,
            Value = 5.0, Source = CostBasisSource.Transaction,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        // ...dann manuell überschreiben — manuell gewinnt immer.
        await service.SetManualValueAsync(CharacterId, 1, 2.5);

        var entry = await db.CostBasisEntries.FirstAsync(e => e.TypeId == 1);
        Assert.Equal(CostBasisSource.Manual, entry.Source);
        Assert.Equal(2.5, entry.Value);
    }

    [Fact]
    public async Task ResetEntry_RemovesEntry()
    {
        var (service, db) = CreateSut();
        await service.SetManualValueAsync(CharacterId, 2, 10.0);

        await service.ResetEntryAsync(CharacterId, 2);

        var count = await db.CostBasisEntries.CountAsync(e => e.TypeId == 2);
        Assert.Equal(0, count);
    }

    // ------------------------------------------------------------------
    // Schätz-Jobs
    // ------------------------------------------------------------------

    [Fact]
    public async Task StartEstimateJob_FiltersLockedItems_AndStoresParameters()
    {
        var (service, db) = CreateSut();
        // Type 1 hat bereits einen finalen (manuellen) Wert → ausgenommen
        await service.SetManualValueAsync(CharacterId, 1, 3.0);

        var jobId = await service.StartEstimateJobAsync(CharacterId, new[] { 1, 2, 3 }, 10000002);

        var job = await db.BackgroundJobs.FindAsync(jobId);
        Assert.NotNull(job);
        Assert.Equal(2, job!.Total);

        // Parameter-JSON enthält nur die offenen Items + Region
        using var doc = JsonDocument.Parse(job.ParametersJson!);
        var typeIds = doc.RootElement.GetProperty("typeIds")
            .EnumerateArray().Select(e => e.GetInt32()).ToList();
        Assert.Equal(new[] { 2, 3 }, typeIds);
        Assert.Equal(10000002, doc.RootElement.GetProperty("regionId").GetInt32());
    }

    [Fact]
    public async Task StartEstimateJob_WithoutSelectedItems_Throws()
    {
        var (service, _) = CreateSut();

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.StartEstimateJobAsync(CharacterId, Array.Empty<int>(), 10000002));
    }

    [Fact]
    public async Task StartEstimateJob_AllLocked_Throws()
    {
        var (service, _) = CreateSut();
        await service.SetManualValueAsync(CharacterId, 1, 3.0);
        await service.SetManualValueAsync(CharacterId, 2, 15.0);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.StartEstimateJobAsync(CharacterId, new[] { 1, 2 }, 10000002));
    }

    // ------------------------------------------------------------------
    // Komplett-Scan (Initial Sync)
    // ------------------------------------------------------------------

    [Fact]
    public async Task StartInventoryScan_CreatesJobWithRegionParameter()
    {
        var (service, db) = CreateSut();

        var jobId = await service.StartInventoryScanAsync(CharacterId, 10000002);

        var job = await db.BackgroundJobs.FindAsync(jobId);
        Assert.NotNull(job);
        Assert.Equal("InventoryScan", job!.JobType);
        Assert.Equal("Komplett-Scan (alle Items)", job.DisplayName);
        Assert.Equal(BackgroundJobStatus.Running, job.Status);

        // Parameter-JSON enthält nur die Region (alle Items werden im Collector bestimmt)
        using var doc = JsonDocument.Parse(job.ParametersJson!);
        Assert.Equal(10000002, doc.RootElement.GetProperty("regionId").GetInt32());
    }

    [Fact]
    public async Task StartInventoryScan_SecondScanWhileActive_Throws()
    {
        var (service, _) = CreateSut();
        await service.StartInventoryScanAsync(CharacterId, 10000002);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.StartInventoryScanAsync(CharacterId, 10000043));
    }

    // ------------------------------------------------------------------
    // Standard-Schätzregion
    // ------------------------------------------------------------------

    [Fact]
    public async Task DefaultRegion_WithoutSetting_IsJita()
    {
        var (service, _) = CreateSut();

        Assert.Equal(10000002, await service.GetDefaultEstimateRegionAsync());
    }

    [Fact]
    public async Task DefaultRegion_AfterSetting_PersistsAndIsReturned()
    {
        var (service, _) = CreateSut();

        await service.SetDefaultEstimateRegionAsync(10000043); // Domain (Amarr)

        Assert.Equal(10000043, await service.GetDefaultEstimateRegionAsync());
    }

    [Fact]
    public async Task DefaultRegion_UnknownValue_IgnoresAndFallsBack()
    {
        var (service, db) = CreateSut();
        db.AppSettings.Add(new AppSetting { Key = "CostBasis.DefaultRegionId", Value = "99999999" });
        await db.SaveChangesAsync();

        Assert.Equal(10000002, await service.GetDefaultEstimateRegionAsync());
    }
}