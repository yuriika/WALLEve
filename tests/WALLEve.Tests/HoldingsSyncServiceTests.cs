using System.Linq;
using Microsoft.EntityFrameworkCore;
using WALLEve.Data;
using WALLEve.Models.Esi.Alliance;
using WALLEve.Models.Esi.Character;
using WALLEve.Models.Esi.Corporation;
using WALLEve.Models.Esi.Markets;
using WALLEve.Models.Measurement;
using WALLEve.Models.Esi.Universe;
using WALLEve.Models.Esi.Wallet;
using WALLEve.Models.Holdings;
using WALLEve.Models.Portfolio;
using WALLEve.Services.Esi.Interfaces;
using WALLEve.Services.Holdings;

namespace WALLEve.Tests;

/// <summary>
/// Regression-Tests für die atomare Character-Snapshot-Synchronisation (#41).
/// Deckt die Akzeptanzkriterien ab: Partial/Fehler/Abbruch verändert den
/// publizierten Snapshot nicht; erfolgreich leerer Sync ersetzt den Bestand;
/// Wiederholung erzeugt keine doppelten Items; Owner-Isolation;
/// Snapshot-Reproduktion; Start/Stop (Cancellation) mit Fake-ESI.
/// </summary>
public class HoldingsSyncServiceTests
{
    private const int CharacterA = 90073315;
    private const int CharacterB = 90073316;

    private sealed class FakeEsi : IEsiApiService
    {
        public List<CharacterAsset>? Assets { get; set; } = new();
        public bool ThrowOnFetch { get; set; }
        public TaskCompletionSource? FetchGate { get; set; }

        public Task<List<CharacterAsset>?> GetCharacterAssetsAsync(int characterId)
        {
            if (ThrowOnFetch)
                return Task.FromException<List<CharacterAsset>?>(new InvalidOperationException("ESI down"));
            if (FetchGate != null)
                return FetchGate.Task.ContinueWith(_ => Assets);
            return Task.FromResult(Assets);
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
        public Task<List<WalletJournalEntry>?> GetWalletJournalAsync(int characterId, int page = 1) => throw new NotImplementedException();
        public Task<List<WalletTransaction>?> GetWalletTransactionsAsync(int characterId) => throw new NotImplementedException();
        public Task<List<WalletJournalEntry>?> GetAllWalletJournalPagesAsync(int characterId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<WalletTransaction>?> GetAllWalletTransactionsPagesAsync(int characterId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<MarketOrder>?> GetMarketOrdersAsync(int characterId) => throw new NotImplementedException();
        public Task<List<MarketOrderHistory>?> GetMarketOrderHistoryAsync(int characterId) => throw new NotImplementedException();
        public Task<List<WalletJournalEntry>?> GetCorporationWalletJournalAsync(int corporationId, int division, int page = 1) => throw new NotImplementedException();
        public Task<List<WalletTransaction>?> GetCorporationWalletTransactionsAsync(int corporationId, int division) => throw new NotImplementedException();
        public Task<List<WALLEve.Models.Esi.Universe.SystemJumps>?> GetSystemJumpsAsync() => throw new NotImplementedException();
        public Task<List<WALLEve.Models.Esi.Universe.SystemKills>?> GetSystemKillsAsync() => throw new NotImplementedException();
        public Task<List<RegionalMarketOrder>?> GetRegionalMarketOrdersAsync(int regionId, int? typeId = null, string orderType = "all", int page = 1) => throw new NotImplementedException();
        public Task<List<RegionalMarketOrder>?> GetAllRegionalMarketOrdersAsync(int regionId, int? typeId = null, string orderType = "all", CancellationToken ct = default, Action<RegionalScanPageTelemetry>? telemetrySink = null) => throw new NotImplementedException();
        public Task<List<MarketHistoryEntry>?> GetMarketHistoryAsync(int regionId, int typeId) => throw new NotImplementedException();
        public Task<List<MarketPrice>?> GetMarketPricesAsync() => throw new NotImplementedException();
    }

    private static HoldingsSyncService CreateService(WalletDbContext db, FakeEsi esi)
        => new(db, esi, new PortfolioSnapshotService(db), Microsoft.Extensions.Logging.Abstractions.NullLogger<HoldingsSyncService>.Instance);

    private static List<CharacterAsset> SampleAssets(int offset = 0, int count = 2)
        => Enumerable.Range(0, count).Select(i => new CharacterAsset
        {
            ItemId = 1000 + offset + i,
            TypeId = 34 + i,
            Quantity = 1 + i,
            IsSingleton = i % 2 == 0,
            LocationId = 60000000 + i,
            LocationType = "station",
            LocationFlag = "Hangar",
            OwnerCharacterId = CharacterA
        }).ToList();

    [Fact]
    public async Task Synchronize_Success_PublishesSnapshotWithMappedItems()
    {
        var db = TestDb.Create();
        var esi = new FakeEsi { Assets = SampleAssets() };
        var service = CreateService(db, esi);

        var run = await service.SynchronizeAsync(CharacterA);

        Assert.Equal("completed", run.Status);
        Assert.NotNull(run.CompletedAt);
        Assert.Null(run.Error);
        var snapshot = await service.GetLatestSnapshotAsync(CharacterA);
        Assert.NotNull(snapshot);
        Assert.Equal(CharacterA, snapshot!.OwnerId);
        Assert.Equal(OwnerType.Character, snapshot.OwnerType);
        Assert.Equal("esi/characters/90073315/assets", snapshot.Source);
        Assert.Equal(2, snapshot.Items.Count);
        Assert.Equal(run.Id, snapshot.SyncRunId);
        // Snapshot-Reproduktion: Rohwerte werden 1:1 und deterministisch abgebildet.
        var first = snapshot.Items.OrderBy(i => i.ItemId).First();
        Assert.Equal(1000, first.ItemId);
        Assert.Equal(34, first.TypeId);
        Assert.Equal(1, first.Quantity);
        Assert.True(first.IsSingleton);
        Assert.Equal(60000000, first.LocationId);
        Assert.Equal("Hangar", first.LocationFlag);
        Assert.Null(first.ParentItemId);
        Assert.Equal(0, db.HoldingItems.Count(i => i.SnapshotId != snapshot.Id));

        // #51: Der vollständige Sync erzeugt genau EINEN historischen Portfolio-Punkt
        // mit unveränderlichen Qualitätszählern; ohne Markt-/Cost-Basis-Daten sind
        // Bewertung und Basis explizit Unknown.
        var portfolio = await db.PortfolioSnapshots.SingleAsync(p => p.HoldingSnapshotId == snapshot.Id);
        Assert.Equal(CharacterA, portfolio.OwnerId);
        Assert.Equal(OwnerType.Character, portfolio.OwnerType);
        Assert.Equal(2, portfolio.TotalItems);
        Assert.Equal(0, portfolio.ValuedItemCount);
        Assert.Equal(2, portfolio.UnknownValuationItemCount);
        Assert.Equal(0, portfolio.CostBasisKnownItemCount);
        Assert.Equal(2, portfolio.UnknownCostBasisItemCount);
    }

    [Fact]
    public async Task Synchronize_EsiError_DoesNotChangePublishedSnapshot()
    {
        var db = TestDb.Create();
        var esi = new FakeEsi { Assets = SampleAssets() };
        var service = CreateService(db, esi);
        await service.SynchronizeAsync(CharacterA);
        var before = await service.GetLatestSnapshotAsync(CharacterA);
        Assert.NotNull(before);

        // Fehler: M0-Vertrag liefert null (keine Teildaten).
        var esiError = new FakeEsi { Assets = null, ThrowOnFetch = true };
        var serviceError = CreateService(db, esiError);

        await Assert.ThrowsAsync<InvalidOperationException>(() => serviceError.SynchronizeAsync(CharacterA));

        var after = await service.GetLatestSnapshotAsync(CharacterA);
        Assert.NotNull(after);
        Assert.Equal(before!.Id, after!.Id); // publizierter Snapshot unverändert
        var failedRun = await db.HoldingSyncRuns.OrderBy(r => r.Id).LastAsync();
        Assert.Equal("failed", failedRun.Status);
        Assert.NotNull(failedRun.Error);

        // #51: Ein fehlgeschlagener Sync erzeugt KEINEN neuen historischen Punkt.
        Assert.Equal(1, await db.PortfolioSnapshots.CountAsync());
    }

    [Fact]
    public async Task Synchronize_ValidEmptyResult_ReplacesHoldings()
    {
        var db = TestDb.Create();
        var esi = new FakeEsi { Assets = SampleAssets() };
        var service = CreateService(db, esi);
        await service.SynchronizeAsync(CharacterA);
        Assert.Equal(2, (await service.GetLatestSnapshotAsync(CharacterA))!.Items.Count);

        // Gültig leerer Sync ersetzt den Bestand korrekt (0 Items).
        var emptyEsi = new FakeEsi { Assets = new List<CharacterAsset>() };
        var serviceEmpty = CreateService(db, emptyEsi);
        var run = await serviceEmpty.SynchronizeAsync(CharacterA);

        Assert.Equal("completed", run.Status);
        var latest = await service.GetLatestSnapshotAsync(CharacterA);
        Assert.NotNull(latest);
        Assert.Equal(run.Id, latest!.SyncRunId);
        Assert.Empty(latest.Items);

        // #51: Vollständig leerer Bestand ist ein gültiger historischer Punkt (0 Items).
        var portfolio = await db.PortfolioSnapshots.SingleAsync(p => p.HoldingSnapshotId == latest.Id);
        Assert.Equal(0, portfolio.TotalItems);
        Assert.Equal(0, portfolio.ValuedItemCount);
        Assert.Equal(0, portfolio.UnknownValuationItemCount);
        Assert.Equal(0, portfolio.CostBasisKnownItemCount);
        Assert.Equal(0, portfolio.UnknownCostBasisItemCount);
    }

    [Fact]
    public async Task Synchronize_Repeat_NoDuplicateItems()
    {
        var db = TestDb.Create();
        var esi = new FakeEsi { Assets = SampleAssets() };
        var service = CreateService(db, esi);

        await service.SynchronizeAsync(CharacterA);
        await service.SynchronizeAsync(CharacterA);

        var latest = await service.GetLatestSnapshotAsync(CharacterA);
        Assert.NotNull(latest);
        Assert.Equal(2, latest!.Items.Count); // kein Duplikat durch Wiederholung
        Assert.Equal(2, latest.Items.Select(i => i.ItemId).Distinct().Count());
        Assert.Equal(2, await db.HoldingSyncRuns.CountAsync());
        // Jeder Snapshot besitzt seine eigenen Items; der publizierte Bestand ist exakt der letzte Lauf.
        var snapshots = await db.HoldingSnapshots.Include(s => s.Items).OrderBy(s => s.Id).ToListAsync();
        Assert.Equal(2, snapshots.Count);
        Assert.All(snapshots, s => Assert.Equal(2, s.Items.Count));
        // Kein Snapshot akkumuliert Items früherer Läufe (4 Rohzeilen = 2 je Snapshot, nie geteilt).
        Assert.Equal(4, await db.HoldingItems.CountAsync());

        // #51: Jeder vollständige Lauf erzeugt genau einen Portfolio-Punkt (2 Punkte,
        // keiner dupliziert — je Quell-Snapshot-ID genau einer).
        Assert.Equal(2, await db.PortfolioSnapshots.CountAsync());
        Assert.Equal(2, await db.PortfolioSnapshots.Select(p => p.HoldingSnapshotId).Distinct().CountAsync());
    }

    [Fact]
    public async Task Synchronize_OwnerIsolation_PerCharacter()
    {
        var db = TestDb.Create();
        var esiA = new FakeEsi { Assets = SampleAssets(offset: 0) };
        var esiB = new FakeEsi { Assets = SampleAssets(offset: 100) };
        var serviceA = CreateService(db, esiA);
        var serviceB = CreateService(db, esiB);

        await serviceA.SynchronizeAsync(CharacterA);
        await serviceB.SynchronizeAsync(CharacterB);

        var latestA = await serviceA.GetLatestSnapshotAsync(CharacterA);
        var latestB = await serviceB.GetLatestSnapshotAsync(CharacterB);
        Assert.NotNull(latestA);
        Assert.NotNull(latestB);
        Assert.Equal(CharacterA, latestA!.OwnerId);
        Assert.Equal(CharacterB, latestB!.OwnerId);
        Assert.All(latestA.Items, i => Assert.True(i.ItemId < 1100));
        Assert.All(latestB.Items, i => Assert.True(i.ItemId >= 1100));
        // Kein Owner sieht die Items des anderen.
        Assert.Equal(0, latestA.Items.Count(i => latestB.Items.Any(j => j.ItemId == i.ItemId)));
    }

    [Fact]
    public async Task Synchronize_CancelledBeforeFetch_PublishesNothing()
    {
        var db = TestDb.Create();
        var esi = new FakeEsi { Assets = SampleAssets() };
        var service = CreateService(db, esi);
        await service.SynchronizeAsync(CharacterA);
        var before = await service.GetLatestSnapshotAsync(CharacterA);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.SynchronizeAsync(CharacterA, cts.Token));

        var after = await service.GetLatestSnapshotAsync(CharacterA);
        Assert.Equal(before!.Id, after!.Id); // nichts publiziert
        Assert.Equal(1, await db.HoldingSyncRuns.CountAsync()); // Lauf wurde nie angelegt
        Assert.Equal(1, await db.PortfolioSnapshots.CountAsync()); // kein neuer historischer Punkt
    }

    [Fact]
    public async Task Synchronize_CancelledMidFetch_MarksRunFailedWithoutPublish()
    {
        var db = TestDb.Create();
        var beforeEsi = new FakeEsi { Assets = SampleAssets() };
        var serviceBefore = CreateService(db, beforeEsi);
        await serviceBefore.SynchronizeAsync(CharacterA);
        var before = await serviceBefore.GetLatestSnapshotAsync(CharacterA);
        Assert.NotNull(before);

        // Fake hängt den Fetch auf, bis der Test den Abbruch signalisiert.
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var esi = new FakeEsi { FetchGate = gate };
        var service = CreateService(db, esi);
        using var cts = new CancellationTokenSource();

        var syncTask = service.SynchronizeAsync(CharacterA, cts.Token);
        cts.Cancel();
        gate.SetResult();

        await Assert.ThrowsAsync<OperationCanceledException>(() => syncTask);

        var after = await serviceBefore.GetLatestSnapshotAsync(CharacterA);
        Assert.Equal(before!.Id, after!.Id); // publizierter Snapshot unverändert
        var failedRun = await db.HoldingSyncRuns.OrderBy(r => r.Id).LastAsync();
        Assert.Equal("failed", failedRun.Status);
        Assert.NotNull(failedRun.Error);
        Assert.Equal(1, await db.HoldingSnapshots.CountAsync()); // kein neuer Snapshot
        Assert.Equal(1, await db.PortfolioSnapshots.CountAsync()); // kein neuer historischer Punkt
    }

    [Fact]
    public async Task GetLatestSnapshot_NeverSynced_ReturnsNull()
    {
        var db = TestDb.Create();
        var service = CreateService(db, new FakeEsi());

        Assert.Null(await service.GetLatestSnapshotAsync(CharacterA));
    }
}