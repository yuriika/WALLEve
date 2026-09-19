using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WALLEve.Data;
using WALLEve.Models.Database;
using WALLEve.Models.Esi.Alliance;
using WALLEve.Models.Esi.Character;
using WALLEve.Models.Esi.Corporation;
using WALLEve.Models.Esi.Markets;
using WALLEve.Models.Esi.Universe;
using WALLEve.Models.Esi.Wallet;
using WALLEve.Models.Industry;
using WALLEve.Models.Measurement;
using WALLEve.Services.Esi.Interfaces;
using WALLEve.Services.Industry;
using WALLEve.Services.Market;
using WALLEve.Services.Market.Interfaces;

namespace WALLEve.Tests;

/// <summary>
/// Deterministische Tests für die Collector/Job-Registry-Anbindung des
/// Industrie-Job-Syncs (#40): Ausführung als persistierter BackgroundJob,
/// Resume unterbrochener Läufe (idempotent, keine Duplikate), Cancel pausierter
/// Läufe, 24h-Intervall mit Force-Override und Fehlerpfad (Failed-Job ohne
/// Datenverlust). Die Tests rufen den Executor direkt auf — kein Collector-Loop,
/// keine Live-ESI.
/// </summary>
public class IndustryJobsSyncExecutorTests
{
    private const int CharacterA = 90073315;

    private sealed class FakeEsi : IEsiApiService
    {
        public List<CharacterIndustryJob>? Jobs { get; set; } = new();
        public bool ThrowOnFetch { get; set; }
        public int SyncCalls { get; private set; }

        public Task<List<CharacterBlueprint>?> GetCharacterBlueprintsAsync(int characterId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<CharacterIndustryJob>?> GetCharacterIndustryJobsAsync(int characterId, CancellationToken ct = default)
        {
            SyncCalls++;
            if (ThrowOnFetch)
                return Task.FromException<List<CharacterIndustryJob>?>(new InvalidOperationException("ESI down"));
            return Task.FromResult(Jobs);
        }

        public Task<List<CharacterMiningEntry>?> GetCharacterMiningLedgerAsync(int characterId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<CharacterOverview?> GetCharacterOverviewAsync() => throw new NotImplementedException();
        public Task<EveCharacter?> GetCharacterAsync(int characterId) => throw new NotImplementedException();
        public Task<EveCorporation?> GetCorporationAsync(int corporationId) => throw new NotImplementedException();
        public Task<EveAlliance?> GetAllianceAsync(int allianceId) => throw new NotImplementedException();
        public Task<double?> GetWalletBalanceAsync(int characterId) => throw new NotImplementedException();
        public Task<CharacterLocation?> GetLocationAsync(int characterId) => throw new NotImplementedException();
        public Task<CharacterShip?> GetCurrentShipAsync(int characterId) => throw new NotImplementedException();
        public Task<CharacterOnlineStatus?> GetOnlineStatusAsync(int characterId) => throw new NotImplementedException();
        public Task<SolarSystem?> GetSolarSystemAsync(int systemId) => throw new NotImplementedException();
        public Task<EveType?> GetTypeAsync(int typeId) => throw new NotImplementedException();
        public Task<StructureLookupResult> GetStructureAsync(long structureId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<CharacterSkills?> GetCharacterSkillsAsync() => throw new NotImplementedException();
        public Task<List<CharacterAsset>?> GetCharacterAssetsAsync(int characterId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<WalletJournalEntry>?> GetWalletJournalAsync(int characterId, int page = 1) => throw new NotImplementedException();
        public Task<List<WalletTransaction>?> GetWalletTransactionsAsync(int characterId) => throw new NotImplementedException();
        public Task<List<WalletJournalEntry>?> GetAllWalletJournalPagesAsync(int characterId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<WalletTransaction>?> GetAllWalletTransactionsPagesAsync(int characterId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<MarketOrder>?> GetMarketOrdersAsync(int characterId) => throw new NotImplementedException();
        public Task<List<MarketOrderHistory>?> GetMarketOrderHistoryAsync(int characterId) => throw new NotImplementedException();
        public Task<List<WalletJournalEntry>?> GetCorporationWalletJournalAsync(int corporationId, int division, int page = 1) => throw new NotImplementedException();
        public Task<List<WalletTransaction>?> GetCorporationWalletTransactionsAsync(int corporationId, int division) => throw new NotImplementedException();
        public Task<List<SystemJumps>?> GetSystemJumpsAsync() => throw new NotImplementedException();
        public Task<List<SystemKills>?> GetSystemKillsAsync() => throw new NotImplementedException();
        public Task<List<RegionalMarketOrder>?> GetRegionalMarketOrdersAsync(int regionId, int? typeId = null, string orderType = "all", int page = 1) => throw new NotImplementedException();
        public Task<List<RegionalMarketOrder>?> GetAllRegionalMarketOrdersAsync(int regionId, int? typeId = null, string orderType = "all", CancellationToken ct = default, Action<RegionalScanPageTelemetry>? telemetrySink = null) => throw new NotImplementedException();
        public Task<List<MarketHistoryEntry>?> GetMarketHistoryAsync(int regionId, int typeId) => throw new NotImplementedException();
        public Task<List<MarketPrice>?> GetMarketPricesAsync() => throw new NotImplementedException();
    }

    private sealed class FakeTrigger : ISyncTriggerService
    {
        public bool Forced { get; set; }

        public Task<bool> TriggerNowAsync(int characterId, string jobType)
        {
            Forced = true;
            return Task.FromResult(true);
        }

        public Task<bool> ConsumeForceAsync(int characterId, string jobType)
        {
            var forced = Forced;
            Forced = false;
            return Task.FromResult(forced);
        }
    }

    private static IndustryJobsSyncExecutor CreateExecutor(WalletDbContext db, FakeEsi esi, FakeTrigger? trigger = null)
        => new(
            db,
            new BackgroundJobManager(db),
            trigger ?? new FakeTrigger(),
            new IndustryJobsSyncService(db, esi, NullLogger<IndustryJobsSyncService>.Instance),
            NullLogger<IndustryJobsSyncExecutor>.Instance);

    private static List<CharacterIndustryJob> SampleJobs(int count = 2)
        => Enumerable.Range(0, count).Select(i => new CharacterIndustryJob
        {
            ActivityId = 1,
            BlueprintId = 1014567891234L + i,
            BlueprintLocationId = 60003760L + i,
            BlueprintTypeId = 1030 + i,
            Duration = 3600,
            EndDate = $"2026-09-{(14 - i):00}T12:00:00Z",
            FacilityId = 60003760L + i,
            InstallerId = CharacterA,
            JobId = 100 + i,
            LicensedRuns = 1,
            OutputLocationId = 60003760L + i,
            ProductTypeId = 44992 + i,
            Runs = 1,
            StartDate = $"2026-09-{(13 - i):00}T12:00:00Z",
            StationId = 60003760,
            Status = i == 0 ? "delivered" : "active",
            SuccessfulRuns = i == 0 ? 1 : null,
            Cost = i == 0 ? 1234.5 : null
        }).ToList();

    private static BackgroundJob SeedJob(WalletDbContext db,
        BackgroundJobStatus status, DateTime? completedAt = null)
    {
        var job = new BackgroundJob
        {
            JobType = IndustryJobsSyncExecutor.JobType,
            DisplayName = "Industrie-Jobs synchronisieren",
            CharacterId = CharacterA,
            Status = status,
            Current = 0,
            Total = 0,
            StartedAt = DateTime.UtcNow.AddHours(-1),
            CompletedAt = completedAt,
            UpdatedAt = status == BackgroundJobStatus.Completed ? completedAt ?? DateTime.UtcNow : DateTime.UtcNow
        };
        db.BackgroundJobs.Add(job);
        return job;
    }

    private static IndustryJobEntry SeedEntry(int jobId, string status)
        => new()
        {
            CharacterId = CharacterA,
            JobId = jobId,
            ActivityId = 1,
            BlueprintId = 1014567891234L,
            BlueprintLocationId = 60003760,
            BlueprintTypeId = 1030,
            OutputLocationId = 60003760,
            FacilityId = 60003760,
            StationId = 60003760,
            ProductTypeId = 44992,
            Status = status,
            StartDate = new DateTime(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc),
            Runs = 1,
            LicensedRuns = 1,
            SuccessfulRuns = 1,
            Cost = 1234.5,
            Duration = 3600,
            InstallerId = CharacterA,
            UpdatedAt = DateTime.UtcNow
        };

    [Fact]
    public async Task FirstRun_CreatesCompletesJob_AndPersistsEntries()
    {
        var db = TestDb.Create();
        var esi = new FakeEsi { Jobs = SampleJobs() };
        var executor = CreateExecutor(db, esi);

        await executor.RunAsync(CharacterA, CancellationToken.None);

        var job = await db.BackgroundJobs.SingleAsync();
        Assert.Equal(IndustryJobsSyncExecutor.JobType, job.JobType);
        Assert.Equal(BackgroundJobStatus.Completed, job.Status);
        Assert.Equal(2, job.Total);
        Assert.Equal(2, await db.IndustryJobEntries.CountAsync());
        Assert.Equal(1, esi.SyncCalls);
    }

    [Fact]
    public async Task RecentCompletion_SkipsAutomaticRun_ForceTriggerOverrides()
    {
        var db = TestDb.Create();
        SeedJob(db, BackgroundJobStatus.Completed, completedAt: DateTime.UtcNow.AddHours(-1));
        await db.SaveChangesAsync();

        var esi = new FakeEsi { Jobs = SampleJobs() };
        var trigger = new FakeTrigger();
        var executor = CreateExecutor(db, esi, trigger);

        // Innerhalb des 24h-Intervalls und ohne Force-Flag: kein neuer Lauf.
        await executor.RunAsync(CharacterA, CancellationToken.None);
        Assert.Equal(1, await db.BackgroundJobs.CountAsync());
        Assert.Equal(0, esi.SyncCalls);

        // Manueller Trigger („Jetzt ausführen“) überwindet das Intervall einmalig.
        await trigger.TriggerNowAsync(CharacterA, IndustryJobsSyncExecutor.JobType);
        await executor.RunAsync(CharacterA, CancellationToken.None);

        Assert.Equal(2, await db.BackgroundJobs.CountAsync());
        var forcedJob = await db.BackgroundJobs
            .OrderByDescending(j => j.StartedAt).FirstAsync();
        Assert.Equal(BackgroundJobStatus.Completed, forcedJob.Status);
        // Genau ein Sync: der übersprungene erste Lauf ruft ESI nicht auf.
        Assert.Equal(1, esi.SyncCalls);
    }

    [Fact]
    public async Task InterruptedJob_IsResumed_WithoutDuplicates_AndUpdatesStatus()
    {
        var db = TestDb.Create();
        var interrupted = SeedJob(db, BackgroundJobStatus.Interrupted);
        // Ein bereits gespiegelter Job mit ALTEM Status; ESI meldet ihn als „delivered“.
        db.IndustryJobEntries.Add(SeedEntry(100, "active"));
        await db.SaveChangesAsync();

        var esi = new FakeEsi { Jobs = SampleJobs() };
        var executor = CreateExecutor(db, esi);

        await executor.RunAsync(CharacterA, CancellationToken.None);

        var job = await db.BackgroundJobs.FindAsync(interrupted.Id);
        Assert.Equal(BackgroundJobStatus.Completed, job!.Status);
        // Resume ohne Duplikate: 2 eindeutige Jobs, der vorhandene wurde aktualisiert.
        Assert.Equal(2, await db.IndustryJobEntries.CountAsync());
        Assert.Equal("delivered", await db.IndustryJobEntries
            .Where(e => e.JobId == 100).Select(e => e.Status).SingleAsync());
        Assert.Equal(1, esi.SyncCalls);
    }

    [Fact]
    public async Task PausedJob_IsNotExecuted_CancelSemantics()
    {
        var db = TestDb.Create();
        var paused = SeedJob(db, BackgroundJobStatus.Paused);
        await db.SaveChangesAsync();

        var esi = new FakeEsi { Jobs = SampleJobs() };
        var executor = CreateExecutor(db, esi);

        await executor.RunAsync(CharacterA, CancellationToken.None);

        var job = await db.BackgroundJobs.FindAsync(paused.Id);
        Assert.Equal(BackgroundJobStatus.Paused, job!.Status);
        Assert.Equal(0, esi.SyncCalls);
        Assert.Equal(0, await db.IndustryJobEntries.CountAsync());
    }

    [Fact]
    public async Task EsiFailure_MarksJobFailed_AndKeepsExistingEntries()
    {
        var db = TestDb.Create();
        var running = SeedJob(db, BackgroundJobStatus.Running);
        db.IndustryJobEntries.Add(SeedEntry(100, "delivered"));
        await db.SaveChangesAsync();

        var esi = new FakeEsi { Jobs = SampleJobs(), ThrowOnFetch = true };
        var executor = CreateExecutor(db, esi);

        await executor.RunAsync(CharacterA, CancellationToken.None);

        var job = await db.BackgroundJobs.FindAsync(running.Id);
        Assert.Equal(BackgroundJobStatus.Failed, job!.Status);
        Assert.False(string.IsNullOrEmpty(job.LastError));
        // Keine Teildaten: der gespiegelte Stand bleibt vollständig erhalten.
        Assert.Equal(1, await db.IndustryJobEntries.CountAsync());
        Assert.Equal("delivered", await db.IndustryJobEntries
            .Where(e => e.JobId == 100).Select(e => e.Status).SingleAsync());
    }
}