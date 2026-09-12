using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using WALLEve.Data;
using WALLEve.Models.Database;
using WALLEve.Models.Holdings;

namespace WALLEve.Tests;

/// <summary>
/// Migrationstests für Issue #35 (Holdings-Rohschema, M1):
/// Die additive Migration AddHoldingsSchema stellt Owner-, SyncRun-, Snapshot-
/// und HoldingItem-Tabellen bereit, ohne bestehende Nutzerdaten anzutasten.
/// </summary>
public class HoldingsSchemaMigrationTests
{
    /// <summary>Letzte Migration VOR der Holdings-Migration (bestehendes Wallet-Schema).</summary>
    private const string PreviousMigration = "20260912174826_AddProvenanceToTradingOpportunity";

    private static WalletDbContext CreateDb(string connectionString)
        => new(new DbContextOptionsBuilder<WalletDbContext>()
            .UseSqlite(connectionString)
            .Options);

    [Fact]
    public async Task Migrate_PreservesWalletJobsFavoritesTransactionsAndManualBasis()
    {
        // Echte Datei-DB, damit alle Migrationen (inkl. Index/DATA-Änderungen) real durchlaufen.
        var dbPath = Path.Combine(Path.GetTempPath(), $"walleve-holdings-{Guid.NewGuid():N}.db");
        try
        {
            var walletDate = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Unspecified);
            var runDate = new DateTime(2026, 9, 12, 8, 0, 0, DateTimeKind.Unspecified);

            // Phase 1: Schema bis VOR der Holdings-Migration aufbauen und Bestandsdaten
            // jedes betroffenen Bereichs anlegen (Wallet, Jobs, Favoriten,
            // Transaktionen, manuelle Basiswerte).
            await using (var db = CreateDb($"Data Source={dbPath}"))
            {
                await db.Database.GetService<IMigrator>().MigrateAsync(PreviousMigration);

                db.Characters.Add(new WalletCharacter
                {
                    CharacterId = 90073315,
                    CharacterName = "Test Alt",
                    LastSyncedAt = walletDate,
                    CreatedAt = walletDate
                });
                db.BackgroundJobs.Add(new BackgroundJob
                {
                    JobType = "HoldingsSync",
                    DisplayName = "Test-Job",
                    Status = BackgroundJobStatus.Completed,
                    Current = 7,
                    Total = 7,
                    ParametersJson = "{\"owner\":\"character\"}",
                    StartedAt = walletDate,
                    CompletedAt = walletDate,
                    UpdatedAt = walletDate
                });
                db.MarketFavorits.Add(new MarketFavorit
                {
                    CharacterId = 90073315,
                    TypeId = 44992,
                    Name = "Test Favorite"
                });
                db.WalletTransactionRecords.Add(new WalletTransactionRecord
                {
                    CharacterId = 90073315,
                    TransactionId = 5555,
                    TypeId = 44992,
                    Date = walletDate,
                    IsBuy = true,
                    IsPersonal = true,
                    JournalRefId = 9999,
                    LocationId = 60003760,
                    Quantity = 10,
                    UnitPrice = 1234.5
                });
                db.CostBasisEntries.Add(new CostBasisEntry
                {
                    CharacterId = 90073315,
                    TypeId = 44992,
                    Value = 1234.5,
                    Source = CostBasisSource.Manual,
                    PurchaseDate = walletDate,
                    UpdatedAt = runDate
                });
                await db.SaveChangesAsync();
            }

            // Phase 2: Holdings-Migration anwenden und ALLE Bestandsdaten zurücklesen.
            await using (var db = CreateDb($"Data Source={dbPath}"))
            {
                await db.Database.MigrateAsync();

                var character = await db.Characters.SingleAsync(c => c.CharacterId == 90073315);
                Assert.Equal("Test Alt", character.CharacterName);
                Assert.Equal(walletDate, character.LastSyncedAt);

                var job = await db.BackgroundJobs.SingleAsync(j => j.JobType == "HoldingsSync");
                Assert.Equal(BackgroundJobStatus.Completed, job.Status);
                Assert.Equal(7, job.Total);
                Assert.Equal("{\"owner\":\"character\"}", job.ParametersJson);

                var favorit = await db.MarketFavorits.SingleAsync(f => f.CharacterId == 90073315 && f.TypeId == 44992);
                Assert.Equal("Test Favorite", favorit.Name);

                var transaction = await db.WalletTransactionRecords.SingleAsync(t => t.TransactionId == 5555);
                Assert.True(transaction.IsBuy);
                Assert.Equal(10, transaction.Quantity);
                Assert.Equal(1234.5, transaction.UnitPrice);

                var basis = await db.CostBasisEntries.SingleAsync(b => b.CharacterId == 90073315 && b.TypeId == 44992);
                Assert.Equal(CostBasisSource.Manual, basis.Source);
                Assert.Equal(1234.5, basis.Value);
                Assert.Equal(walletDate, basis.PurchaseDate);

                // Holdings-Tabellen sind additiv erzeugt und zunächst leer.
                Assert.Equal(0, await db.HoldingSyncRuns.CountAsync());
                Assert.Equal(0, await db.HoldingSnapshots.CountAsync());
                Assert.Equal(0, await db.HoldingItems.CountAsync());
            }
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task SameRawItemId_DifferentOwners_DoesNotCollide_AndRawDimensionsRoundTrip()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"walleve-holdings-owner-{Guid.NewGuid():N}.db");
        try
        {
            // Vollständig migriertes Schema (inkl. Holdings) auf frischer Datei-DB.
            await using (var db = CreateDb($"Data Source={dbPath}"))
            {
                await db.Database.MigrateAsync();

                var synced = new DateTime(2026, 9, 12, 9, 0, 0, DateTimeKind.Unspecified);

                // Owner A: Character. Besitzt ein Item mit roher ItemId 1234567890.
                var characterRun = new HoldingSyncRun
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
                                new HoldingItem
                                {
                                    ItemId = 1234567890,
                                    TypeId = 44992,
                                    Quantity = 100,
                                    IsSingleton = false,
                                    LocationId = 60003760,
                                    LocationFlag = "Hangar",
                                    ParentItemId = null
                                }
                            }
                        }
                    }
                };

                // Owner B: Corporation. Besitzt ein Item mit DERSELBEN rohen ItemId —
                // darf mit Owner A nicht kollidieren (kein Unique-Index auf ItemId).
                var corporationRun = new HoldingSyncRun
                {
                    OwnerType = OwnerType.Corporation,
                    OwnerId = 98000001,
                    StartedAt = synced,
                    Status = "completed",
                    Snapshots =
                    {
                        new HoldingSnapshot
                        {
                            OwnerType = OwnerType.Corporation,
                            OwnerId = 98000001,
                            SyncedAt = synced,
                            Source = "esi/corporations/98000001/assets",
                            Items =
                            {
                                new HoldingItem
                                {
                                    ItemId = 1234567890,
                                    TypeId = 34,
                                    Quantity = 5,
                                    IsSingleton = true,
                                    LocationId = 1021468205422,
                                    LocationFlag = "CorpSAG2",
                                    ParentItemId = 99000001
                                }
                            }
                        }
                    }
                };

                db.HoldingSyncRuns.AddRange(characterRun, corporationRun);
                await db.SaveChangesAsync();
            }

            // Phase 2: Rohdaten über den Owner-Schlüssel je Owner zurücklesen.
            await using (var db = CreateDb($"Data Source={dbPath}"))
            {
                var allWithSameItemId = await db.HoldingItems
                    .Where(i => i.ItemId == 1234567890)
                    .ToListAsync();
                Assert.Equal(2, allWithSameItemId.Count);

                var characterItem = await db.HoldingItems
                    .Include(i => i.Snapshot)
                    .SingleAsync(i => i.Snapshot!.OwnerType == OwnerType.Character && i.ItemId == 1234567890);
                Assert.Equal(90073315, characterItem.Snapshot!.OwnerId);
                Assert.Equal(100, characterItem.Quantity);
                Assert.False(characterItem.IsSingleton);
                Assert.Equal(60003760, characterItem.LocationId);
                Assert.Equal("Hangar", characterItem.LocationFlag);
                Assert.Null(characterItem.ParentItemId);

                var corporationItem = await db.HoldingItems
                    .Include(i => i.Snapshot)
                    .SingleAsync(i => i.Snapshot!.OwnerType == OwnerType.Corporation && i.ItemId == 1234567890);
                Assert.Equal(98000001, corporationItem.Snapshot!.OwnerId);
                Assert.Equal(34, corporationItem.TypeId);
                Assert.Equal(5, corporationItem.Quantity);
                Assert.True(corporationItem.IsSingleton);
                Assert.Equal(1021468205422, corporationItem.LocationId);
                Assert.Equal("CorpSAG2", corporationItem.LocationFlag);
                Assert.Equal(99000001, corporationItem.ParentItemId);

                // Fremdschlüssel bleiben erhalten: jede Rohzeile hängt an genau
                // ihrem Snapshot, jeder Snapshot an genau seinem SyncRun.
                var snapshots = await db.HoldingSnapshots
                    .Include(s => s.SyncRun)
                    .ToListAsync();
                Assert.Equal(2, snapshots.Count);
                Assert.All(snapshots, s => Assert.NotNull(s.SyncRun));
                Assert.Equal(2, await db.HoldingSyncRuns.CountAsync());
            }
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }
}