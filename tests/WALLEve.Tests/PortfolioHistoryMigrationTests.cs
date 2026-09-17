using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using WALLEve.Data;
using WALLEve.Models.Database;
using WALLEve.Models.Holdings;

namespace WALLEve.Tests;

/// <summary>
/// Migrationstests für Issue #38 (Historien-Read-Models):
/// Die additive Migration AddPortfolioHistoryPoints legt die
/// PortfolioHistoryPoints-Tabelle an, ohne bestehende Holdings-/Wallet-Daten
/// anzutasten; der eindeutige Index auf HoldingSnapshotId verhindert doppelte
/// Punkte für dieselbe Quell-Snapshot-ID. Ein Rollback löscht ausschließlich
/// die neue Tabelle.
/// </summary>
public class PortfolioHistoryMigrationTests
{
    /// <summary>Letzte Migration VOR der Portfolio-History-Migration.</summary>
    private const string PreviousMigration = "20260916081308_AddTradeProfileSignalState";

    private static WalletDbContext CreateDb(string connectionString)
        => new(new DbContextOptionsBuilder<WalletDbContext>()
            .UseSqlite(connectionString)
            .Options);

    [Fact]
    public async Task Migrate_PreservesHoldingsAndWalletData_AndCreatesEmptyHistoryTable()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"walleve-history-{Guid.NewGuid():N}.db");
        try
        {
            var synced = new DateTime(2026, 9, 16, 10, 0, 0, DateTimeKind.Unspecified);

            // Phase 1: Schema bis VOR der History-Migration aufbauen und
            // Holdings- plus Wallet-Bestandsdaten anlegen.
            await using (var db = CreateDb($"Data Source={dbPath}"))
            {
                await db.Database.GetService<IMigrator>().MigrateAsync(PreviousMigration);

                db.Characters.Add(new WalletCharacter
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
                                new HoldingItem { ItemId = 1, TypeId = 34, Quantity = 5, IsSingleton = false, LocationId = 60000000, LocationFlag = "Hangar" }
                            }
                        }
                    }
                });
                db.MarketSnapshots.Add(new MarketSnapshot
                {
                    RegionId = 10000002,
                    TypeId = 34,
                    Timestamp = synced,
                    BestSellPrice = 100.0
                });
                db.WalletTransactionRecords.Add(new WalletTransactionRecord
                {
                    CharacterId = 90073315,
                    TypeId = 34,
                    TransactionId = 7001,
                    Date = synced,
                    IsBuy = true,
                    IsPersonal = true,
                    JournalRefId = 500001,
                    LocationId = 60000000,
                    Quantity = 5,
                    UnitPrice = 90.0
                });
                await db.SaveChangesAsync();
            }

            // Phase 2: History-Migration additiv anwenden und die Daten prüfen.
            await using (var db = CreateDb($"Data Source={dbPath}"))
            {
                await db.Database.GetService<IMigrator>().MigrateAsync("AddPortfolioHistoryPoints");

                // Bestehende Daten sind unangetastet.
                Assert.Equal(1, await db.HoldingSnapshots.CountAsync());
                Assert.Equal(1, await db.HoldingItems.CountAsync());
                Assert.Equal(1, await db.WalletTransactionRecords.CountAsync());
                Assert.Equal(1, await db.MarketSnapshots.CountAsync());

                // Neue Tabelle ist leer und über den Kontext erreichbar.
                Assert.Equal(0, await db.PortfolioHistoryPoints.CountAsync());

                // Eindeutiger Index: dieselbe Quell-Snapshot-ID nur einmal.
                var snapshotId = (await db.HoldingSnapshots.SingleAsync()).Id;
                db.PortfolioHistoryPoints.Add(new WALLEve.Models.Portfolio.PortfolioHistoryPoint
                {
                    HoldingSnapshotId = snapshotId,
                    OwnerType = OwnerType.Character,
                    OwnerId = 90073315,
                    CapturedAt = synced,
                    Source = "esi/characters/90073315/assets"
                });
                await db.SaveChangesAsync();
                await Assert.ThrowsAsync<DbUpdateException>(async () =>
                {
                    db.PortfolioHistoryPoints.Add(new WALLEve.Models.Portfolio.PortfolioHistoryPoint
                    {
                        HoldingSnapshotId = snapshotId,
                        OwnerType = OwnerType.Character,
                        OwnerId = 90073315,
                        CapturedAt = synced,
                        Source = "esi/characters/90073315/assets"
                    });
                    await db.SaveChangesAsync();
                });
            }

            // Phase 3: Rollback entfernt ausschließlich die neue Tabelle.
            await using (var db = CreateDb($"Data Source={dbPath}"))
            {
                await db.Database.GetService<IMigrator>().MigrateAsync(PreviousMigration);
                Assert.Equal(1, await db.HoldingSnapshots.CountAsync());
                Assert.Equal(1, await db.HoldingItems.CountAsync());
            }
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }
}