using Microsoft.EntityFrameworkCore;
using WALLEve.Data;
using WALLEve.Models.Database;
using WALLEve.Models.Holdings;
using WALLEve.Models.Portfolio;
using WALLEve.Services.Holdings;

namespace WALLEve.Tests;

/// <summary>
/// Regression-Tests für Issue #51 (historische Portfolio-Punkte):
/// Qualitätszähler (bewertet / Cost-Basis bekannt) werden zum Erfassungszeitpunkt
/// unveränderlich gespeichert; fehlende Daten sind explizit Unknown; dieselbe
/// Quell-Snapshot-ID erzeugt keine doppelte Historie; spätere Preise oder
/// Cost-Basis-Buchungen überschreiben die historische Provenienz nicht.
/// </summary>
public class PortfolioSnapshotServiceTests
{
    private const int CharacterA = 90073315;
    private const int CharacterB = 90073316;

    /// <summary>
    /// Legt einen vollständigen Holdings-Snapshot mit Items je TypeId an und
    /// liefert seine Id zurück.
    /// </summary>
    private static async Task<long> CreateHoldingSnapshotAsync(WalletDbContext db, int ownerId, params (int TypeId, int Quantity)[] items)
    {
        var run = new HoldingSyncRun
        {
            OwnerType = OwnerType.Character,
            OwnerId = ownerId,
            StartedAt = DateTime.UtcNow,
            Status = "completed",
            Snapshots =
            {
                new HoldingSnapshot
                {
                    OwnerType = OwnerType.Character,
                    OwnerId = ownerId,
                    SyncedAt = DateTime.UtcNow,
                    Source = $"esi/characters/{ownerId}/assets",
                    Items = items.Select((it, i) => new HoldingItem
                    {
                        ItemId = 1000 + i,
                        TypeId = it.TypeId,
                        Quantity = it.Quantity,
                        IsSingleton = false,
                        LocationId = 60000000,
                        LocationFlag = "Hangar"
                    }).ToList()
                }
            }
        };
        db.HoldingSyncRuns.Add(run);
        await db.SaveChangesAsync();
        return run.Snapshots.Single().Id;
    }

    [Fact]
    public async Task Capture_ComputesImmutableQualityCountersFromCurrentData()
    {
        var db = TestDb.Create();
        var service = new PortfolioSnapshotService(db);

        // Typ 34: Markt-Snapshot + Cost-Basis vorhanden → bewertet und Basis bekannt.
        // Typ 35: beides fehlt → explizit Unknown.
        db.MarketSnapshots.Add(new MarketSnapshot
        {
            RegionId = 10000002,
            TypeId = 34,
            Timestamp = DateTime.UtcNow,
            BestSellPrice = 100.0
        });
        db.CostBasisEntries.Add(new CostBasisEntry
        {
            CharacterId = CharacterA,
            TypeId = 34,
            Value = 50.0,
            Source = CostBasisSource.Transaction,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var sourceId = await CreateHoldingSnapshotAsync(db, CharacterA, (34, 5), (35, 3));

        var portfolio = await service.CaptureAsync(sourceId);

        Assert.Equal(sourceId, portfolio.HoldingSnapshotId);
        Assert.Equal(CharacterA, portfolio.OwnerId);
        Assert.Equal(OwnerType.Character, portfolio.OwnerType);
        Assert.Equal(2, portfolio.TotalItems);
        Assert.Equal(1, portfolio.ValuedItemCount);          // Typ 34
        Assert.Equal(1, portfolio.UnknownValuationItemCount); // Typ 35
        Assert.Equal(1, portfolio.CostBasisKnownItemCount);   // Typ 34
        Assert.Equal(1, portfolio.UnknownCostBasisItemCount); // Typ 35
    }

    [Fact]
    public async Task Capture_SameSourceSnapshotId_DoesNotDuplicateHistory()
    {
        var db = TestDb.Create();
        var service = new PortfolioSnapshotService(db);

        var sourceId = await CreateHoldingSnapshotAsync(db, CharacterA, (34, 1), (35, 1));

        var first = await service.CaptureAsync(sourceId);
        var second = await service.CaptureAsync(sourceId);

        Assert.Equal(first.Id, second.Id); // idempotent: gleicher Eintrag
        Assert.Equal(1, await db.PortfolioSnapshots.CountAsync());
        var stored = await db.PortfolioSnapshots.SingleAsync();
        Assert.Equal(2, stored.TotalItems);
    }

    [Fact]
    public async Task Capture_LaterPricesDoNotOverwriteHistoricalProvenance()
    {
        var db = TestDb.Create();
        var service = new PortfolioSnapshotService(db);

        // Erfassen OHNE Markt-/Cost-Basis-Daten: alles Unknown.
        var sourceId = await CreateHoldingSnapshotAsync(db, CharacterA, (34, 1), (35, 1));
        var captured = await service.CaptureAsync(sourceId);
        var capturedAt = captured.CapturedAt;
        Assert.Equal(0, captured.ValuedItemCount);
        Assert.Equal(2, captured.UnknownValuationItemCount);
        Assert.Equal(0, captured.CostBasisKnownItemCount);
        Assert.Equal(2, captured.UnknownCostBasisItemCount);

        // Später kommen Preise und Cost-Basis dazu.
        db.MarketSnapshots.Add(new MarketSnapshot
        {
            RegionId = 10000002,
            TypeId = 34,
            Timestamp = DateTime.UtcNow,
            BestSellPrice = 200.0
        });
        db.CostBasisEntries.Add(new CostBasisEntry
        {
            CharacterId = CharacterA,
            TypeId = 34,
            Value = 50.0,
            Source = CostBasisSource.Manual,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        // Erneute Verarbeitung derselben Quell-Snapshot-ID: kein Überschreiben.
        var again = await service.CaptureAsync(sourceId);
        Assert.Equal(captured.Id, again.Id);
        Assert.Equal(capturedAt, captured.CapturedAt);
        var stored = await db.PortfolioSnapshots.SingleAsync();
        Assert.Equal(0, stored.ValuedItemCount);            // Provenienz bleibt historisch
        Assert.Equal(2, stored.UnknownValuationItemCount);  // nicht überschrieben
        Assert.Equal(0, stored.CostBasisKnownItemCount);
        Assert.Equal(2, stored.UnknownCostBasisItemCount);
    }

    [Fact]
    public async Task Capture_OwnerIsolation_CostBasisCountedPerOwner()
    {
        var db = TestDb.Create();
        var service = new PortfolioSnapshotService(db);

        db.CostBasisEntries.Add(new CostBasisEntry
        {
            CharacterId = CharacterA,
            TypeId = 34,
            Value = 50.0,
            Source = CostBasisSource.Transaction,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var sourceA = await CreateHoldingSnapshotAsync(db, CharacterA, (34, 1));
        var sourceB = await CreateHoldingSnapshotAsync(db, CharacterB, (34, 1));

        var a = await service.CaptureAsync(sourceA);
        var b = await service.CaptureAsync(sourceB);

        Assert.Equal(1, a.CostBasisKnownItemCount);   // A hat Basis für Typ 34
        Assert.Equal(0, b.CostBasisKnownItemCount);   // B hat KEINE Basis für Typ 34
        Assert.Equal(1, b.UnknownCostBasisItemCount);
    }

    [Fact]
    public async Task Capture_CorporationOwner_AllCostBasisUnknown()
    {
        var db = TestDb.Create();
        var service = new PortfolioSnapshotService(db);

        // Corporation-Bestand: Cost-Basis-Konzept existiert nur für Character-Owner.
        var run = new HoldingSyncRun
        {
            OwnerType = OwnerType.Corporation,
            OwnerId = 98000001,
            StartedAt = DateTime.UtcNow,
            Status = "completed",
            Snapshots =
            {
                new HoldingSnapshot
                {
                    OwnerType = OwnerType.Corporation,
                    OwnerId = 98000001,
                    SyncedAt = DateTime.UtcNow,
                    Source = "esi/corporations/98000001/assets",
                    Items =
                    {
                        new HoldingItem
                        {
                            ItemId = 2000,
                            TypeId = 34,
                            Quantity = 5,
                            IsSingleton = false,
                            LocationId = 60000001,
                            LocationFlag = "CorpSAG2"
                        }
                    }
                }
            }
        };
        db.HoldingSyncRuns.Add(run);
        await db.SaveChangesAsync();

        var portfolio = await service.CaptureAsync(run.Snapshots.Single().Id);

        Assert.Equal(1, portfolio.TotalItems);
        Assert.Equal(0, portfolio.CostBasisKnownItemCount);
        Assert.Equal(1, portfolio.UnknownCostBasisItemCount);
    }

    [Fact]
    public async Task Capture_MissingSourceSnapshot_Throws()
    {
        var db = TestDb.Create();
        var service = new PortfolioSnapshotService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CaptureAsync(999999));
        Assert.Equal(0, await db.PortfolioSnapshots.CountAsync());
    }
}