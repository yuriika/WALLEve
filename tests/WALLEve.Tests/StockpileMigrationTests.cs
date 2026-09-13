using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using WALLEve.Data;
using WALLEve.Models.Database;
using WALLEve.Models.Holdings;
using WALLEve.Models.Stockpiles;

namespace WALLEve.Tests;

/// <summary>
/// Migrationstests für Issue #36 (Stockpile-Ziele): Die additive Migration
/// AddStockpileTargets legt die StockpileTargets-Tabelle an, ohne bestehende
/// Wallet-Historie, Cost-Basis-Werte, Ledger-Einträge oder App-Einstellungen
/// anzutasten; Nutzerdaten bleiben beim Upgrade vollständig erhalten.
/// </summary>
public class StockpileMigrationTests
{
    /// <summary>Letzte Migration VOR der Stockpile-Migration (Hub-Profile #58).</summary>
    private const string PreviousMigration = "20260913114217_AddMarketHubProfiles";

    private static WalletDbContext CreateDb(string connectionString)
        => new(new DbContextOptionsBuilder<WalletDbContext>()
            .UseSqlite(connectionString)
            .Options);

    [Fact]
    public async Task Migrate_PreservesExistingUserData_AndCreatesStockpileTable()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"walleve-stockpile-{Guid.NewGuid():N}.db");
        try
        {
            var txDate = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Unspecified);

            // Phase 1: Schema bis VOR der Stockpile-Migration und Nutzerdaten
            // anlegen: Wallet-Transaktion, Ledger-Eintrag, manueller
            // Cost-Basis-Wert, AppSetting (DefaultRegion).
            await using (var db = CreateDb($"Data Source={dbPath}"))
            {
                await db.Database.GetService<IMigrator>().MigrateAsync(PreviousMigration);

                db.WalletTransactionRecords.Add(new WalletTransactionRecord
                {
                    CharacterId = 90073315,
                    TransactionId = 3001,
                    TypeId = 44992,
                    Date = txDate,
                    IsBuy = true,
                    IsPersonal = true,
                    JournalRefId = 888,
                    LocationId = 60003760,
                    Quantity = 10,
                    UnitPrice = 100
                });

                db.CostBasisLedgerEntries.Add(new CostBasisLedgerEntry
                {
                    CharacterId = 90073315,
                    TypeId = 44992,
                    SourceTransactionId = 3001,
                    Date = txDate,
                    IsBuy = true,
                    Quantity = 10,
                    UnitPrice = 100,
                    ImportedAt = DateTime.UtcNow
                });

                db.CostBasisEntries.Add(new CostBasisEntry
                {
                    CharacterId = 90073315,
                    TypeId = 44992,
                    Value = 98.5,
                    Source = CostBasisSource.Manual, // alter manueller Wert
                    PurchaseDate = txDate,
                    UpdatedAt = DateTime.UtcNow
                });

                db.AppSettings.Add(new AppSetting
                {
                    Key = "CostBasis.DefaultRegionId",
                    Value = "10000002"
                });

                await db.SaveChangesAsync();
            }

            // Phase 2: auf die neueste Migration aktualisieren.
            await using (var db = CreateDb($"Data Source={dbPath}"))
            {
                await db.Database.GetService<IMigrator>().MigrateAsync();

                // Nutzerdaten unangetastet.
                Assert.Equal(1, await db.WalletTransactionRecords.CountAsync());
                Assert.Equal(1, await db.CostBasisLedgerEntries.CountAsync());
                Assert.Equal(CostBasisSource.Manual, (await db.CostBasisEntries.SingleAsync()).Source);
                Assert.Equal("10000002",
                    (await db.AppSettings.FindAsync("CostBasis.DefaultRegionId"))!.Value);

                // Neue Tabelle existiert und ist additiv nutzbar.
                Assert.Empty(await db.StockpileTargets.ToListAsync());

                var now = DateTime.UtcNow;
                db.StockpileTargets.Add(new StockpileTarget
                {
                    OwnerType = OwnerType.Character,
                    OwnerId = 90073315,
                    TypeId = 34,
                    Quantity = 1000,
                    LocationId = 60003760,
                    Note = "Vorratsziel",
                    IsArchived = false,
                    CreatedAt = now,
                    UpdatedAt = now
                });
                await db.SaveChangesAsync();

                var target = await db.StockpileTargets.SingleAsync();
                Assert.Equal(34, target.TypeId);
                Assert.Equal(1000, target.Quantity);
                Assert.Equal(60003760, target.LocationId);
            }
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task UniqueIndex_BlocksDuplicateActiveTargetForSameOwnerType()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"walleve-stockpile-uniq-{Guid.NewGuid():N}.db");
        try
        {
            await using (var db = CreateDb($"Data Source={dbPath}"))
            {
                await db.Database.MigrateAsync();

                var now = DateTime.UtcNow;
                db.StockpileTargets.Add(new StockpileTarget
                {
                    OwnerType = OwnerType.Character,
                    OwnerId = 90073315,
                    TypeId = 34,
                    Quantity = 100,
                    IsArchived = false,
                    CreatedAt = now,
                    UpdatedAt = now
                });
                await db.SaveChangesAsync();

                // Zweites aktives Ziel für dasselbe Owner/Type muss auf
                // Datenbankebene verworfen werden (partieller Unique-Index).
                db.StockpileTargets.Add(new StockpileTarget
                {
                    OwnerType = OwnerType.Character,
                    OwnerId = 90073315,
                    TypeId = 34,
                    Quantity = 200,
                    IsArchived = false,
                    CreatedAt = now,
                    UpdatedAt = now
                });
                await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            }

            // Archivierte Ziele dürfen denselben Slot nutzen (partieller Filter).
            // Frische Datei, damit Phase 1 keine aktiven Zeilen hinterlässt.
            var dbPath2 = Path.Combine(Path.GetTempPath(), $"walleve-stockpile-uniq2-{Guid.NewGuid():N}.db");
            try
            {
                await using (var db = CreateDb($"Data Source={dbPath2}"))
                {
                    await db.Database.MigrateAsync();

                    var now = DateTime.UtcNow;
                    db.StockpileTargets.Add(new StockpileTarget
                    {
                        OwnerType = OwnerType.Character,
                        OwnerId = 90073315,
                        TypeId = 34,
                        Quantity = 100,
                        IsArchived = true,
                        CreatedAt = now,
                        UpdatedAt = now
                    });
                    db.StockpileTargets.Add(new StockpileTarget
                    {
                        OwnerType = OwnerType.Character,
                        OwnerId = 90073315,
                        TypeId = 34,
                        Quantity = 100,
                        IsArchived = false,
                        CreatedAt = now,
                        UpdatedAt = now
                    });
                    await db.SaveChangesAsync();
                }
            }
            finally
            {
                if (File.Exists(dbPath2)) File.Delete(dbPath2);
            }
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }
}