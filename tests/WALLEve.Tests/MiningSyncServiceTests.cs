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
using WALLEve.Services.Mining;

namespace WALLEve.Tests;

/// <summary>
/// Regression-Tests für die idempotente persönliche Mining-Ledger-Synchronisation (#39).
/// Deckt die Akzeptanzkriterien ab: erfolgreiche Wiederholung dupliziert keine
/// Erträge, korrigierte Tagesmenge ersetzt den alten Wert (kein Aufaddieren),
/// Historie außerhalb des ESI-Fensters bleibt erhalten, Owner-Isolation,
/// M0-Partial-Verhalten (null = nichts schreiben) und Cancellation.
/// </summary>
public class MiningSyncServiceTests
{
    private const int CharacterA = 90073315;
    private const int CharacterB = 90073316;

    private sealed class FakeEsi : IEsiApiService
    {
        public List<CharacterMiningEntry>? Ledger { get; set; } = new();
        public bool ThrowOnFetch { get; set; }

        public Task<List<CharacterMiningEntry>?> GetCharacterMiningLedgerAsync(int characterId, CancellationToken ct = default)
        {
            if (ThrowOnFetch)
                return Task.FromException<List<CharacterMiningEntry>?>(new InvalidOperationException("ESI down"));
            return Task.FromResult(Ledger);
        }

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

    private static MiningSyncService CreateService(WalletDbContext db, FakeEsi esi)
        => new(db, esi, NullLogger<MiningSyncService>.Instance);

    private static List<CharacterMiningEntry> SampleLedger(int offset = 0, int count = 2)
        => Enumerable.Range(0, count).Select(i => new CharacterMiningEntry
        {
            Date = $"2026-09-{(15 - i):00}",
            Quantity = 1000L + 500 * i,
            SolarSystemId = 30000001 + i,
            TypeId = 1230 + i
        }).ToList();

    [Fact]
    public async Task Synchronize_Success_InsertsAllEntriesDateNormalized()
    {
        var db = TestDb.Create();
        var esi = new FakeEsi { Ledger = SampleLedger() };
        var service = CreateService(db, esi);

        var result = await service.SynchronizeAsync(CharacterA);

        Assert.True(result.Success);
        Assert.Equal(2, result.Inserted);
        Assert.Equal(0, result.Updated);
        Assert.Equal(2, result.Total);
        Assert.Equal(2, await db.MiningLedgerEntries.CountAsync());

        var first = await db.MiningLedgerEntries.OrderByDescending(e => e.Date).FirstAsync();
        Assert.Equal(CharacterA, first.CharacterId);
        Assert.Equal(new DateTime(2026, 9, 15), first.Date); // Datum ohne Uhrzeit
        Assert.Equal(1230, first.TypeId);
        Assert.Equal(30000001, first.SolarSystemId);
        Assert.Equal(1000, first.Quantity);
    }

    [Fact]
    public async Task Synchronize_SecondRun_IsIdempotent_NoDuplicates()
    {
        var db = TestDb.Create();
        var esi = new FakeEsi { Ledger = SampleLedger() };
        var service = CreateService(db, esi);

        var first = await service.SynchronizeAsync(CharacterA);
        var second = await service.SynchronizeAsync(CharacterA);

        Assert.True(first.Success);
        Assert.True(second.Success);
        // Erfolgreiche Wiederholung dupliziert keine Erträge: nichts neu, nichts korrigiert.
        Assert.Equal(0, second.Inserted);
        Assert.Equal(0, second.Updated);
        Assert.Equal(2, second.Total);
        Assert.Equal(2, await db.MiningLedgerEntries.CountAsync());
        // Mengen unverändert.
        Assert.Equal(1000, await db.MiningLedgerEntries
            .Where(e => e.TypeId == 1230).Select(e => e.Quantity).SingleAsync());
    }

    [Fact]
    public async Task Synchronize_CorrectedQuantity_ReplacesOldValue()
    {
        var db = TestDb.Create();
        var esi = new FakeEsi { Ledger = SampleLedger(count: 1) };
        var service = CreateService(db, esi);

        await service.SynchronizeAsync(CharacterA);
        // ESI korrigiert die kumulierte Tagesmenge nach unten (Korrektur nach Sync-Fehler).
        esi.Ledger![0].Quantity = 700;
        var second = await service.SynchronizeAsync(CharacterA);

        Assert.True(second.Success);
        Assert.Equal(1, second.Updated);
        Assert.Equal(1, await db.MiningLedgerEntries.CountAsync());
        Assert.Equal(700, await db.MiningLedgerEntries.Select(e => e.Quantity).SingleAsync());
    }

    [Fact]
    public async Task Synchronize_PreservesEntriesOutsideEsiWindow()
    {
        var db = TestDb.Create();
        db.MiningLedgerEntries.Add(new Models.Mining.MiningLedgerEntry
        {
            CharacterId = CharacterA,
            Date = new DateTime(2026, 7, 1), // weit außerhalb des API-Fensters
            TypeId = 1230,
            SolarSystemId = 30000001,
            Quantity = 999,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var esi = new FakeEsi { Ledger = SampleLedger() };
        var service = CreateService(db, esi);

        var result = await service.SynchronizeAsync(CharacterA);

        Assert.True(result.Success);
        // Alte Zeile überlebt, neue kommen additiv hinzu.
        Assert.Equal(3, await db.MiningLedgerEntries.CountAsync());
        Assert.Equal(999, await db.MiningLedgerEntries
            .Where(e => e.Date == new DateTime(2026, 7, 1)).Select(e => e.Quantity).SingleAsync());
    }

    [Fact]
    public async Task Synchronize_NullEsiResult_WritesNothing()
    {
        var db = TestDb.Create();
        var esi = new FakeEsi { Ledger = null };
        var service = CreateService(db, esi);

        var result = await service.SynchronizeAsync(CharacterA);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Equal(0, await db.MiningLedgerEntries.CountAsync());
    }

    [Fact]
    public async Task Synchronize_OwnerIsolation_TwoCharactersNeverMix()
    {
        var db = TestDb.Create();
        var esi = new FakeEsi { Ledger = SampleLedger() };
        var service = CreateService(db, esi);

        await service.SynchronizeAsync(CharacterA);

        Assert.Equal(0, await db.MiningLedgerEntries.CountAsync(e => e.CharacterId == CharacterB));
        Assert.Equal(2, await db.MiningLedgerEntries.CountAsync(e => e.CharacterId == CharacterA));

        // B synchronisiert eigene Daten — A bleibt unverändert.
        esi.Ledger = new List<CharacterMiningEntry> { SampleLedger()[0] };
        await service.SynchronizeAsync(CharacterB);

        Assert.Equal(1, await db.MiningLedgerEntries.CountAsync(e => e.CharacterId == CharacterB));
        Assert.Equal(2, await db.MiningLedgerEntries.CountAsync(e => e.CharacterId == CharacterA));
    }

    [Fact]
    public async Task Synchronize_CancelledBeforeStart_WritesNothing()
    {
        var db = TestDb.Create();
        var esi = new FakeEsi { Ledger = SampleLedger() };
        var service = CreateService(db, esi);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.SynchronizeAsync(CharacterA, cts.Token));

        Assert.Equal(0, await db.MiningLedgerEntries.CountAsync());
    }
}