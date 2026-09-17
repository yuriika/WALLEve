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
/// Regression-Tests für die idempotente Blueprint-Synchronisation (#49).
/// Deckt die Akzeptanzkriterien ab: BPO-Sentinel (Runs = -1) und BPC-Runs
/// werden nicht verwechselt, ME/TE und Ort bleiben als Rohwerte erhalten,
/// Owner-Isolation über (CharacterId, ItemId), Missing/Partial-Sync (null)
/// schreibt nichts, und Blueprints außerhalb der ESI-Antwort werden nicht
/// gelöscht (Historie bleibt erhalten).
/// </summary>
public class BlueprintsSyncServiceTests
{
    private const int CharacterA = 90073315;
    private const int CharacterB = 90073316;

    private const int BpoTypeId = 1030;
    private const int BpcTypeId = 1031;

    private sealed class FakeEsi : IEsiApiService
    {
        public List<CharacterBlueprint>? Blueprints { get; set; } = new();
        public bool ThrowOnFetch { get; set; }

        public Task<List<CharacterBlueprint>?> GetCharacterBlueprintsAsync(int characterId, CancellationToken ct = default)
        {
            if (ThrowOnFetch)
                return Task.FromException<List<CharacterBlueprint>?>(new InvalidOperationException("ESI down"));
            return Task.FromResult(Blueprints);
        }

        public Task<List<CharacterIndustryJob>?> GetCharacterIndustryJobsAsync(int characterId, CancellationToken ct = default) => throw new NotImplementedException();
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
        public Task<List<CharacterAsset>?> GetCharacterAssetsAsync(int characterId) => throw new NotImplementedException();
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

    private static BlueprintsSyncService CreateService(WalletDbContext db, FakeEsi esi)
        => new(db, esi, NullLogger<BlueprintsSyncService>.Instance);

    /// <summary>Typisches Paar: ein BPO (Runs = -1, kein Copy) und ein BPC (Runs &gt; 0, Copy).</summary>
    private static List<CharacterBlueprint> SampleBlueprints() => new()
    {
        new CharacterBlueprint
        {
            ItemId = 1000,
            TypeId = BpoTypeId,
            LocationId = 60003760,
            LocationFlag = "Hangar",
            Quantity = -1,
            MaterialEfficiency = 10,
            TimeEfficiency = 20,
            Runs = -1,
            IsBlueprintCopy = false
        },
        new CharacterBlueprint
        {
            ItemId = 2000,
            TypeId = BpcTypeId,
            LocationId = 10439550937895,
            LocationFlag = "CorpDeliveries",
            Quantity = 25,
            MaterialEfficiency = 5,
            TimeEfficiency = 18,
            Runs = 50,
            IsBlueprintCopy = true
        }
    };

    [Fact]
    public async Task Synchronize_Success_PreservesBpoSentinelBpcRunsMeTeAndLocation()
    {
        var db = TestDb.Create();
        var esi = new FakeEsi { Blueprints = SampleBlueprints() };

        var result = await CreateService(db, esi).SynchronizeAsync(CharacterA);

        Assert.True(result.Success);
        Assert.Equal(2, result.Inserted);
        Assert.Equal(0, result.Updated);
        Assert.Equal(2, result.Total);

        var rows = await db.BlueprintEntries.OrderBy(e => e.ItemId).ToListAsync();
        Assert.Equal(2, rows.Count);

        // BPO: Runs = -1 ist der Sentinel und bleibt als Rohwert erhalten.
        var bpo = rows[0];
        Assert.Equal(1000, bpo.ItemId);
        Assert.Equal(BpoTypeId, bpo.TypeId);
        Assert.Equal(-1, bpo.Runs);
        Assert.False(bpo.IsBlueprintCopy);
        Assert.Equal(10, bpo.MaterialEfficiency);
        Assert.Equal(20, bpo.TimeEfficiency);
        Assert.Equal(60003760, bpo.LocationId);
        Assert.Equal("Hangar", bpo.LocationFlag);
        Assert.Equal(CharacterA, bpo.CharacterId);

        // BPC: Runs und Kopierstatus bleiben als Rohwerte erhalten.
        var bpc = rows[1];
        Assert.Equal(2000, bpc.ItemId);
        Assert.Equal(BpcTypeId, bpc.TypeId);
        Assert.Equal(50, bpc.Runs);
        Assert.True(bpc.IsBlueprintCopy);
        Assert.Equal(5, bpc.MaterialEfficiency);
        Assert.Equal(18, bpc.TimeEfficiency);
        Assert.Equal(10439550937895, bpc.LocationId);
        Assert.Equal("CorpDeliveries", bpc.LocationFlag);
    }

    [Fact]
    public async Task Synchronize_ExhaustedBpcWithZeroRuns_IsNotConfusedWithBpo()
    {
        var db = TestDb.Create();
        var esi = new FakeEsi
        {
            Blueprints = new List<CharacterBlueprint>
            {
                new() { ItemId = 1000, TypeId = BpoTypeId, LocationId = 1, LocationFlag = "Hangar", Runs = -1, IsBlueprintCopy = false },
                new() { ItemId = 2000, TypeId = BpcTypeId, LocationId = 1, LocationFlag = "Hangar", Runs = 0, IsBlueprintCopy = true }
            }
        };

        await CreateService(db, esi).SynchronizeAsync(CharacterA);

        var rows = await db.BlueprintEntries.OrderBy(e => e.ItemId).ToListAsync();
        // Runs 0 (verbrauchter BPC) darf nie wie der BPO-Sentinel -1 behandelt werden.
        Assert.Equal(new[] { -1, 0 }, rows.Select(r => r.Runs));
        Assert.Equal(new[] { false, true }, rows.Select(r => r.IsBlueprintCopy));
    }

    [Fact]
    public async Task Synchronize_SecondRunWithUnchangedData_UpdatesNothing()
    {
        var db = TestDb.Create();
        var esi = new FakeEsi { Blueprints = SampleBlueprints() };
        var service = CreateService(db, esi);

        await service.SynchronizeAsync(CharacterA);
        var second = await service.SynchronizeAsync(CharacterA);

        Assert.Equal(0, second.Inserted);
        Assert.Equal(0, second.Updated);
        Assert.Equal(2, await db.BlueprintEntries.CountAsync());
    }

    [Fact]
    public async Task Synchronize_MutatedMeRunsAndLocation_UpdateInPlaceWithoutDuplicates()
    {
        var db = TestDb.Create();
        var esi = new FakeEsi
        {
            Blueprints = new List<CharacterBlueprint>
            {
                new() { ItemId = 1000, TypeId = BpoTypeId, LocationId = 60003760, LocationFlag = "Hangar", MaterialEfficiency = 1, TimeEfficiency = 2, Runs = -1, IsBlueprintCopy = false }
            }
        };
        var service = CreateService(db, esi);

        await service.SynchronizeAsync(CharacterA);

        // Research + Umzug in einen anderen Container.
        esi.Blueprints = new List<CharacterBlueprint>
        {
            new() { ItemId = 1000, TypeId = BpoTypeId, LocationId = 60003761, LocationFlag = "Hangar", MaterialEfficiency = 10, TimeEfficiency = 20, Runs = -1, IsBlueprintCopy = false }
        };

        var second = await service.SynchronizeAsync(CharacterA);

        Assert.Equal(1, second.Updated);
        Assert.Equal(0, second.Inserted);
        var row = await db.BlueprintEntries.SingleAsync(e => e.ItemId == 1000);
        Assert.Equal(10, row.MaterialEfficiency);
        Assert.Equal(20, row.TimeEfficiency);
        Assert.Equal(60003761, row.LocationId);
        Assert.Equal(-1, row.Runs);
        Assert.False(row.IsBlueprintCopy);
    }

    [Fact]
    public async Task Synchronize_SameItemIdOnDifferentCharacters_DoesNotCollide()
    {
        var db = TestDb.Create();
        var esi = new FakeEsi { Blueprints = SampleBlueprints() };
        var service = CreateService(db, esi);

        await service.SynchronizeAsync(CharacterA);
        await service.SynchronizeAsync(CharacterB);

        Assert.Equal(4, await db.BlueprintEntries.CountAsync());
        Assert.Equal(2, await db.BlueprintEntries.CountAsync(e => e.CharacterId == CharacterA));
        Assert.Equal(2, await db.BlueprintEntries.CountAsync(e => e.CharacterId == CharacterB));
        // Dieselbe ItemId ist je Character getrennt gespeichert.
        Assert.Equal(2, await db.BlueprintEntries.CountAsync(e => e.ItemId == 1000));
    }

    [Fact]
    public async Task Synchronize_EsiFailure_NothingIsWritten()
    {
        var db = TestDb.Create();
        var esi = new FakeEsi { Blueprints = null };

        var result = await CreateService(db, esi).SynchronizeAsync(CharacterA);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Equal(0, await db.BlueprintEntries.CountAsync());
    }

    [Fact]
    public async Task Synchronize_MissingBlueprintInLaterResponse_KeepsHistoricRow()
    {
        var db = TestDb.Create();
        var esi = new FakeEsi { Blueprints = SampleBlueprints() };
        var service = CreateService(db, esi);

        await service.SynchronizeAsync(CharacterA);

        // ESI liefert den zweiten Blueprint nicht mehr (verkauft/zerstört) —
        // die historische Zeile darf nicht gelöscht werden.
        esi.Blueprints = SampleBlueprints().Where(b => b.ItemId == 1000).ToList();

        var second = await service.SynchronizeAsync(CharacterA);

        Assert.True(second.Success);
        Assert.Equal(0, second.Inserted);
        Assert.Equal(0, second.Updated);
        Assert.Equal(2, await db.BlueprintEntries.CountAsync());
    }

    [Fact]
    public async Task Synchronize_Cancellation_ThrowsWithoutWrites()
    {
        var db = TestDb.Create();
        var esi = new FakeEsi { Blueprints = SampleBlueprints() };
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateService(db, esi).SynchronizeAsync(CharacterA, cts.Token));

        Assert.Equal(0, await db.BlueprintEntries.CountAsync());
    }
}