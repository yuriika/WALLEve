using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WALLEve.Data;
using WALLEve.Models.Esi.Alliance;
using WALLEve.Models.Esi.Character;
using WALLEve.Models.Esi.Corporation;
using WALLEve.Models.Esi.Markets;
using WALLEve.Models.Esi.Universe;
using WALLEve.Models.Esi.Wallet;
using WALLEve.Models.Measurement;
using WALLEve.Services.Esi.Interfaces;
using WALLEve.Services.Industry;

namespace WALLEve.Tests;

/// <summary>
/// Regression-Tests für die idempotente Synchronisation der Character-Industriejobs (#40).
/// Deckt die Akzeptanzkriterien ab: Partial-Sync entfernt keine Jobs und ist bei
/// Wiederholung/Statuswechsel idempotent, Owner-/Ortskontext bleibt erhalten,
/// historische Jobs verschwinden nicht wegen des ESI-API-Fensters, M0-Partial-
/// Verhalten (null = nichts schreiben) und Cancellation.
/// </summary>
public class IndustryJobsSyncServiceTests
{
    private const int CharacterA = 90073315;
    private const int CharacterB = 90073316;

    private sealed class FakeEsi : IEsiApiService
    {
        public List<CharacterIndustryJob>? Jobs { get; set; } = new();
        public bool ThrowOnFetch { get; set; }

        public Task<List<CharacterBlueprint>?> GetCharacterBlueprintsAsync(int characterId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<CharacterIndustryJob>?> GetCharacterIndustryJobsAsync(int characterId, CancellationToken ct = default)
        {
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

    private static IndustryJobsSyncService CreateService(WalletDbContext db, FakeEsi esi)
        => new(db, esi, NullLogger<IndustryJobsSyncService>.Instance);

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

    [Fact]
    public async Task Synchronize_Success_InsertsAllJobsWithOwnerAndLocations()
    {
        var db = TestDb.Create();
        var esi = new FakeEsi { Jobs = SampleJobs() };
        var service = CreateService(db, esi);

        var result = await service.SynchronizeAsync(CharacterA);

        Assert.True(result.Success);
        Assert.Equal(2, result.Inserted);
        Assert.Equal(0, result.Updated);
        Assert.Equal(2, result.Total);
        Assert.Equal(2, await db.IndustryJobEntries.CountAsync());

        var delivered = await db.IndustryJobEntries.SingleAsync(e => e.JobId == 100);
        Assert.Equal(CharacterA, delivered.CharacterId);
        Assert.Equal("delivered", delivered.Status);
        Assert.Equal(1030, delivered.BlueprintTypeId);
        Assert.Equal(44992, delivered.ProductTypeId);
        Assert.Equal(60003760, delivered.BlueprintLocationId);
        Assert.Equal(60003760, delivered.OutputLocationId);
        Assert.Equal(new DateTime(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc), delivered.StartDate);
        Assert.Equal(new DateTime(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc), delivered.EndDate);
        Assert.Equal(1, delivered.SuccessfulRuns);
        Assert.Equal(1234.5, delivered.Cost);
    }

    [Fact]
    public async Task Synchronize_SecondRun_IsIdempotent_NoDuplicates()
    {
        var db = TestDb.Create();
        var esi = new FakeEsi { Jobs = SampleJobs() };
        var service = CreateService(db, esi);

        var first = await service.SynchronizeAsync(CharacterA);
        var second = await service.SynchronizeAsync(CharacterA);

        Assert.True(first.Success);
        Assert.True(second.Success);
        // Erfolgreiche Wiederholung mit unveränderten Daten: nichts neu, nichts korrigiert.
        Assert.Equal(0, second.Inserted);
        Assert.Equal(0, second.Updated);
        Assert.Equal(2, second.Total);
        Assert.Equal(2, await db.IndustryJobEntries.CountAsync());
        Assert.Equal("delivered", await db.IndustryJobEntries
            .Where(e => e.JobId == 100).Select(e => e.Status).SingleAsync());
    }

    [Fact]
    public async Task Synchronize_StatusChange_UpdatesMutableFieldsOnly()
    {
        var db = TestDb.Create();
        var esi = new FakeEsi { Jobs = SampleJobs(count: 1) };
        var service = CreateService(db, esi);

        await service.SynchronizeAsync(CharacterA);

        // ESI meldet Statuswechsel active → delivered inkl. Abschluss und Erfolgs-Runs.
        var job = esi.Jobs![0];
        job.Status = "delivered";
        job.CompletedDate = "2026-09-15T08:30:00Z";
        job.SuccessfulRuns = 1;
        job.Cost = 999.0;

        var second = await service.SynchronizeAsync(CharacterA);

        Assert.True(second.Success);
        Assert.Equal(0, second.Inserted);
        Assert.Equal(1, second.Updated);
        var stored = await db.IndustryJobEntries.SingleAsync();
        Assert.Equal("delivered", stored.Status);
        Assert.Equal(new DateTime(2026, 9, 15, 8, 30, 0, DateTimeKind.Utc), stored.CompletedDate);
        Assert.Equal(1, stored.SuccessfulRuns);
        Assert.Equal(999.0, stored.Cost);
        // Zeitlicher Orts-/Owner-Kontext bleibt unverändert.
        Assert.Equal(60003760, stored.BlueprintLocationId);
        Assert.Equal(CharacterA, stored.CharacterId);
    }

    [Fact]
    public async Task Synchronize_PreservesJobsOutsideEsiWindow()
    {
        var db = TestDb.Create();
        db.IndustryJobEntries.Add(new Models.Industry.IndustryJobEntry
        {
            CharacterId = CharacterA,
            JobId = 1, // historischer Job, den ESI (Fenster ~90 Tage) nicht mehr liefert
            ActivityId = 1,
            BlueprintTypeId = 1030,
            Status = "delivered",
            StartDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var esi = new FakeEsi { Jobs = SampleJobs() };
        var service = CreateService(db, esi);

        var result = await service.SynchronizeAsync(CharacterA);

        Assert.True(result.Success);
        // Alter Job überlebt, neue kommen additiv hinzu — Partial-Sync entfernt nie Jobs.
        Assert.Equal(3, await db.IndustryJobEntries.CountAsync());
        Assert.Equal("delivered", await db.IndustryJobEntries
            .Where(e => e.JobId == 1).Select(e => e.Status).SingleAsync());
    }

    [Fact]
    public async Task Synchronize_NullEsiResult_WritesNothing()
    {
        var db = TestDb.Create();
        var esi = new FakeEsi { Jobs = null };
        var service = CreateService(db, esi);

        var result = await service.SynchronizeAsync(CharacterA);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Equal(0, await db.IndustryJobEntries.CountAsync());
    }

    [Fact]
    public async Task Synchronize_OwnerIsolation_TwoCharactersNeverMix()
    {
        var db = TestDb.Create();
        var esi = new FakeEsi { Jobs = SampleJobs() };
        var service = CreateService(db, esi);

        await service.SynchronizeAsync(CharacterA);

        Assert.Equal(0, await db.IndustryJobEntries.CountAsync(e => e.CharacterId == CharacterB));
        Assert.Equal(2, await db.IndustryJobEntries.CountAsync(e => e.CharacterId == CharacterA));

        esi.Jobs = new List<CharacterIndustryJob> { SampleJobs()[0] };
        await service.SynchronizeAsync(CharacterB);

        Assert.Equal(1, await db.IndustryJobEntries.CountAsync(e => e.CharacterId == CharacterB));
        Assert.Equal(2, await db.IndustryJobEntries.CountAsync(e => e.CharacterId == CharacterA));
    }

    [Fact]
    public async Task Synchronize_CancelledBeforeStart_WritesNothing()
    {
        var db = TestDb.Create();
        var esi = new FakeEsi { Jobs = SampleJobs() };
        var service = CreateService(db, esi);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.SynchronizeAsync(CharacterA, cts.Token));

        Assert.Equal(0, await db.IndustryJobEntries.CountAsync());
    }
}