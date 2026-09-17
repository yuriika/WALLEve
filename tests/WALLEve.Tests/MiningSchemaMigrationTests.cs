using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using WALLEve.Data;
using WALLEve.Models.Database;
using WALLEve.Models.Holdings;
using WALLEve.Models.Mining;

namespace WALLEve.Tests;

/// <summary>
/// Migrationstests für Issue #39 (Mining-Ledger, M5):
/// Die additive Migration AddMiningLedgerEntries schafft die Ledger-Tabelle mit
/// Idempotenz-Unique-Index (CharacterId, Date, TypeId, SolarSystemId), ohne
/// bestehende Nutzerdaten anzutasten; der Unique-Index weist Duplikate ab.
/// </summary>
public class MiningSchemaMigrationTests
{
    /// <summary>Letzte Migration VOR der Mining-Migration (bestehendes Wallet-Schema).</summary>
    private const string PreviousMigration = "20260917084431_AddPortfolioHistoryProjections";

    private static WalletDbContext CreateDb(string connectionString)
        => new(new DbContextOptionsBuilder<WalletDbContext>()
            .UseSqlite(connectionString)
            .Options);

    [Fact]
    public async Task Migrate_PreservesWalletDataAndCreatesLedgerTable()
    {
        // Echte Datei-DB, damit alle Migrationen (inkl. Index-/DATA-Änderungen) real durchlaufen.
        var dbPath = Path.Combine(Path.GetTempPath(), $"walleve-mining-{Guid.NewGuid():N}.db");
        try
        {
            var walletDate = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Unspecified);
            var runDate = new DateTime(2026, 9, 12, 8, 0, 0, DateTimeKind.Unspecified);

            // Phase 1: Schema bis VOR der Mining-Migration aufbauen und Bestandsdaten
            // jedes betroffenen Bereichs anlegen (Character, Job, Favorit, Transaktion, Basis).
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
                    JobType = "MiningSync",
                    DisplayName = "Test-Job",
                    Status = BackgroundJobStatus.Completed,
                    Current = 7,
                    Total = 7,
                    ParametersJson = "{\"character\":90073315}",
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

            // Phase 2: Mining-Migration anwenden und ALLE Bestandsdaten zurücklesen.
            await using (var db = CreateDb($"Data Source={dbPath}"))
            {
                await db.Database.MigrateAsync();

                var character = await db.Characters.SingleAsync(c => c.CharacterId == 90073315);
                Assert.Equal("Test Alt", character.CharacterName);
                Assert.Equal(walletDate, character.LastSyncedAt);

                var job = await db.BackgroundJobs.SingleAsync(j => j.JobType == "MiningSync");
                Assert.Equal(BackgroundJobStatus.Completed, job.Status);
                Assert.Equal("{\"character\":90073315}", job.ParametersJson);

                var favorit = await db.MarketFavorits.SingleAsync(f => f.CharacterId == 90073315 && f.TypeId == 44992);
                Assert.Equal("Test Favorite", favorit.Name);

                var transaction = await db.WalletTransactionRecords.SingleAsync(t => t.TransactionId == 5555);
                Assert.True(transaction.IsBuy);
                Assert.Equal(10, transaction.Quantity);
                Assert.Equal(1234.5, transaction.UnitPrice);

                var basis = await db.CostBasisEntries.SingleAsync(b => b.CharacterId == 90073315 && b.TypeId == 44992);
                Assert.Equal(CostBasisSource.Manual, basis.Source);
                Assert.Equal(1234.5, basis.Value);

                // Mining-Tabelle ist additiv erzeugt und zunächst leer.
                Assert.Equal(0, await db.MiningLedgerEntries.CountAsync());
            }
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task UniqueIndex_RejectsDuplicateOwnerDateTypeSystem()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"walleve-mining-dup-{Guid.NewGuid():N}.db");
        try
        {
            // Vollständig migriertes Schema (inkl. Mining) auf frischer Datei-DB.
            await using (var db = CreateDb($"Data Source={dbPath}"))
            {
                await db.Database.MigrateAsync();

                var date = new DateTime(2026, 9, 15);
                db.MiningLedgerEntries.Add(new MiningLedgerEntry
                {
                    CharacterId = 90073315,
                    Date = date,
                    TypeId = 1230,
                    SolarSystemId = 30000001,
                    Quantity = 500,
                    UpdatedAt = DateTime.UtcNow
                });
                db.MiningLedgerEntries.Add(new MiningLedgerEntry
                {
                    CharacterId = 90073315,
                    Date = date,
                    TypeId = 1230,
                    SolarSystemId = 30000001,
                    Quantity = 501,
                    UpdatedAt = DateTime.UtcNow
                });

                await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
                Assert.Equal(0, await db.MiningLedgerEntries.CountAsync());
            }
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task SameKey_DifferentCharacters_DoesNotCollide()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"walleve-mining-owner-{Guid.NewGuid():N}.db");
        try
        {
            await using (var db = CreateDb($"Data Source={dbPath}"))
            {
                await db.Database.MigrateAsync();

                var date = new DateTime(2026, 9, 15);
                db.MiningLedgerEntries.Add(new MiningLedgerEntry
                {
                    CharacterId = 90073315,
                    Date = date,
                    TypeId = 1230,
                    SolarSystemId = 30000001,
                    Quantity = 500,
                    UpdatedAt = DateTime.UtcNow
                });
                db.MiningLedgerEntries.Add(new MiningLedgerEntry
                {
                    CharacterId = 90073316,
                    Date = date,
                    TypeId = 1230,
                    SolarSystemId = 30000001,
                    Quantity = 900,
                    UpdatedAt = DateTime.UtcNow
                });
                await db.SaveChangesAsync();

                Assert.Equal(2, await db.MiningLedgerEntries.CountAsync());
                Assert.Equal(900, await db.MiningLedgerEntries
                    .Where(e => e.CharacterId == 90073316).Select(e => e.Quantity).SingleAsync());
            }
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }
}