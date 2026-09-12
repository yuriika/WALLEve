using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using WALLEve.Data;
using WALLEve.Models.Holdings;
using WALLEve.Models.Portfolio;

namespace WALLEve.Tests;

/// <summary>
/// Migrationstests für Issue #51 (historische Portfolio-Punkte):
/// Die additive Migration AddPortfolioSnapshots legt die PortfolioSnapshots-Tabelle
/// an, ohne bestehende Holdings-/Wallet-Daten anzutasten; der eindeutige Index
/// auf HoldingSnapshotId verhindert doppelte Historie für dieselbe Quell-Snapshot-ID.
/// </summary>
public class PortfolioSnapshotMigrationTests
{
    /// <summary>Letzte Migration VOR der Portfolio-Migration (Holdings-Schema #35).</summary>
    private const string PreviousMigration = "20260912181108_AddHoldingsSchema";

    private static WalletDbContext CreateDb(string connectionString)
        => new(new DbContextOptionsBuilder<WalletDbContext>()
            .UseSqlite(connectionString)
            .Options);

    [Fact]
    public async Task Migrate_PreservesHoldingsAndWalletData_AndCreatesEmptyPortfolioTable()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"walleve-portfolio-{Guid.NewGuid():N}.db");
        try
        {
            var synced = new DateTime(2026, 9, 12, 10, 0, 0, DateTimeKind.Unspecified);

            // Phase 1: Schema bis VOR der Portfolio-Migration aufbauen und
            // Holdings- plus Wallet-Bestandsdaten anlegen.
            await using (var db = CreateDb($"Data Source={dbPath}"))
            {
                await db.Database.GetService<IMigrator>().MigrateAsync(PreviousMigration);

                db.Characters.Add(new WALLEve.Models.Database.WalletCharacter
                {
                    CharacterId = 90073315,
                    CharacterName = "Test Alt",
                    LastSyncedAt = synced,
                    CreatedAt = synced
                });
                db.HoldingSyncRuns.Add(new HoldingSyncRun
                {
                    OwnerType = OwnerType.Character,
                    OwnerId = 90073315,
                    StartedAt = synced,
                    CompletedAt = synced,
                    Status = "completed",
                    Snapshots =
                    {
                        new HoldingSnapshot
                        {
                            OwnerType = OwnerType.Character,
                            OwnerId = 90073315,
                            SyncedAt = synced,
                            Source = "esi/characters/90073315/assets",
                            Items =
                            {
                                new HoldingItem
                                {
                                    ItemId = 1234567890,
                                    TypeId = 44992,
                                    Quantity = 100,
                                    IsSingleton = false,
                                    LocationId = 60003760,
                                    LocationFlag = "Hangar"
                                }
                            }
                        }
                    }
                });
                await db.SaveChangesAsync();
            }

            // Phase 2: Portfolio-Migration anwenden und ALLE Bestandsdaten zurücklesen.
            await using (var db = CreateDb($"Data Source={dbPath}"))
            {
                await db.Database.MigrateAsync();

                var character = await db.Characters.SingleAsync(c => c.CharacterId == 90073315);
                Assert.Equal("Test Alt", character.CharacterName);

                var snapshot = await db.HoldingSnapshots
                    .Include(s => s.Items)
                    .SingleAsync();
                Assert.Equal(90073315, snapshot.OwnerId);
                Assert.Equal("esi/characters/90073315/assets", snapshot.Source);
                var item = Assert.Single(snapshot.Items);
                Assert.Equal(1234567890, item.ItemId);
                Assert.Equal(44992, item.TypeId);
                Assert.Equal(100, item.Quantity);

                // Portfolio-Tabelle ist additiv erzeugt und zunächst leer.
                Assert.Equal(0, await db.PortfolioSnapshots.CountAsync());
            }
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task UniqueIndex_BlocksDuplicateHistoryForSameSourceSnapshot()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"walleve-portfolio-uniq-{Guid.NewGuid():N}.db");
        try
        {
            await using (var db = CreateDb($"Data Source={dbPath}"))
            {
                await db.Database.MigrateAsync();

                var run = new HoldingSyncRun
                {
                    OwnerType = OwnerType.Character,
                    OwnerId = 90073315,
                    StartedAt = DateTime.UtcNow,
                    Status = "completed",
                    Snapshots =
                    {
                        new HoldingSnapshot
                        {
                            OwnerType = OwnerType.Character,
                            OwnerId = 90073315,
                            SyncedAt = DateTime.UtcNow,
                            Source = "esi/characters/90073315/assets"
                        }
                    }
                };
                db.HoldingSyncRuns.Add(run);
                await db.SaveChangesAsync();

                var sourceId = run.Snapshots.Single().Id;
                db.PortfolioSnapshots.Add(new PortfolioSnapshot
                {
                    HoldingSnapshotId = sourceId,
                    OwnerType = OwnerType.Character,
                    OwnerId = 90073315,
                    CapturedAt = DateTime.UtcNow,
                    TotalItems = 0
                });
                await db.SaveChangesAsync();

                // Zweiter Eintrag für dieselbe Quell-Snapshot-ID muss verworfen werden.
                db.PortfolioSnapshots.Add(new PortfolioSnapshot
                {
                    HoldingSnapshotId = sourceId,
                    OwnerType = OwnerType.Character,
                    OwnerId = 90073315,
                    CapturedAt = DateTime.UtcNow,
                    TotalItems = 0
                });
                await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            }
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }
}