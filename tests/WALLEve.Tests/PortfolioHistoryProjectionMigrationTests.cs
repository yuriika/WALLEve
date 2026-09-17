using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using WALLEve.Data;
using WALLEve.Models.Holdings;
using WALLEve.Models.Portfolio;

namespace WALLEve.Tests;

/// <summary>
/// Migrationstests für Review #157 (eingefrorene Orts-/Kategorie-Projektionen):
/// Die additive Migration AddPortfolioHistoryProjections stellt die
/// Projektions-Tabellen bereit, ohne bestehende Historien-Punkte anzutasten.
/// </summary>
public class PortfolioHistoryProjectionMigrationTests
{
    /// <summary>Letzte Migration VOR der Projektions-Migration (Portfolio-Historienpunkte).</summary>
    private const string PreviousMigration = "20260917063600_AddPortfolioHistoryPoints";

    private static WalletDbContext CreateDb(string connectionString)
        => new(new DbContextOptionsBuilder<WalletDbContext>()
            .UseSqlite(connectionString)
            .Options);

    [Fact]
    public async Task Migrate_PreservesExistingHistoryPoints_AndAddsEmptyProjectionTables()
    {
        // Echte Datei-DB, damit alle Migrationen real durchlaufen.
        var dbPath = Path.Combine(Path.GetTempPath(), $"walleve-portfolio-projections-{Guid.NewGuid():N}.db");
        try
        {
            var syncedAt = new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);

            // Phase 1: Schema bis VOR der Projektions-Migration aufbauen und
            // einen bestehenden Historien-Punkt anlegen (Bestandssituation).
            await using (var db = CreateDb($"Data Source={dbPath}"))
            {
                await db.Database.GetService<IMigrator>().MigrateAsync(PreviousMigration);

                var run = new HoldingSyncRun
                {
                    OwnerType = OwnerType.Character,
                    OwnerId = 90073315,
                    StartedAt = syncedAt,
                    Status = "completed",
                    Snapshots =
                    {
                        new HoldingSnapshot
                        {
                            OwnerType = OwnerType.Character,
                            OwnerId = 90073315,
                            SyncedAt = syncedAt,
                            Source = "esi/characters/90073315/assets",
                            Items =
                            {
                                new HoldingItem
                                {
                                    ItemId = 1, TypeId = 34, Quantity = 5,
                                    IsSingleton = false, LocationId = 60000000, LocationFlag = "Hangar"
                                }
                            }
                        }
                    }
                };
                db.HoldingSyncRuns.Add(run);
                await db.SaveChangesAsync(); // erzeugt Snapshot-Id für den FK
                db.PortfolioHistoryPoints.Add(new PortfolioHistoryPoint
                {
                    HoldingSnapshotId = run.Snapshots.Single().Id,
                    OwnerType = OwnerType.Character,
                    OwnerId = 90073315,
                    CapturedAt = syncedAt,
                    Source = "esi/characters/90073315/assets",
                    ValuationRegionId = 10000002,
                    ValuationHubName = "Jita",
                    ValuatedTypeCount = 1,
                    AssetsItemCount = 1,
                    AssetsQuantity = 5,
                    AssetsValue = 500.0
                });
                await db.SaveChangesAsync();
            }

            // Phase 2: additive Migration auf den neuesten Stand — der
            // bestehende Punkt bleibt unverändert, die neuen Tabellen entstehen leer.
            await using (var db = CreateDb($"Data Source={dbPath}"))
            {
                await db.Database.MigrateAsync();

                var point = await db.PortfolioHistoryPoints.SingleAsync();
                Assert.Equal(5, point.AssetsQuantity);
                Assert.Equal(500.0, point.AssetsValue);
                Assert.Equal("Jita", point.ValuationHubName);

                Assert.Equal(0, await db.PortfolioHistoryLocations.CountAsync());
                Assert.Equal(0, await db.PortfolioHistoryCategories.CountAsync());
            }
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }
}