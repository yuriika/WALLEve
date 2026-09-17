using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using WALLEve.Data;
using WALLEve.Models.Industry;

namespace WALLEve.Tests;

/// <summary>
/// Migrationstests für Issue #40 (Industrie-Job-Register, M6):
/// Die additive Migration AddIndustryJobEntries schafft die Job-Tabelle mit
/// Idempotenz-Unique-Index (CharacterId, JobId), ohne bestehende Nutzerdaten
/// anzutasten; der Unique-Index weist Duplikate ab.
/// </summary>
public class IndustrySchemaMigrationTests
{
    /// <summary>Letzte Migration VOR der Industrie-Migration (bestehendes Wallet-Schema).</summary>
    private const string PreviousMigration = "20260917101426_AddMiningLedgerEntries";

    private static WalletDbContext CreateDb(string connectionString)
        => new(new DbContextOptionsBuilder<WalletDbContext>()
            .UseSqlite(connectionString)
            .Options);

    [Fact]
    public async Task Migrate_PreservesWalletDataAndCreatesJobTable()
    {
        // Echte Datei-DB, damit alle Migrationen (inkl. Index-/DATA-Änderungen) real durchlaufen.
        var dbPath = Path.Combine(Path.GetTempPath(), $"walleve-industry-{Guid.NewGuid():N}.db");
        try
        {
            var walletDate = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Unspecified);

            // Phase 1: Schema bis VOR der Industrie-Migration aufbauen und Bestandsdaten
            // des betroffenen Bereichs anlegen (Character + Mining-Ledger-Zeile).
            await using (var db = CreateDb($"Data Source={dbPath}"))
            {
                await db.Database.GetService<IMigrator>().MigrateAsync(PreviousMigration);

                db.Characters.Add(new Models.Database.WalletCharacter
                {
                    CharacterId = 90073315,
                    CharacterName = "Test Alt",
                    LastSyncedAt = walletDate,
                    CreatedAt = walletDate
                });
                db.MiningLedgerEntries.Add(new Models.Mining.MiningLedgerEntry
                {
                    CharacterId = 90073315,
                    Date = new DateTime(2026, 9, 15),
                    TypeId = 1230,
                    SolarSystemId = 30000001,
                    Quantity = 500,
                    UpdatedAt = walletDate
                });
                await db.SaveChangesAsync();
            }

            // Phase 2: Industrie-Migration anwenden und ALLE Bestandsdaten zurücklesen.
            await using (var db = CreateDb($"Data Source={dbPath}"))
            {
                await db.Database.MigrateAsync();

                var character = await db.Characters.SingleAsync(c => c.CharacterId == 90073315);
                Assert.Equal("Test Alt", character.CharacterName);

                var ledger = await db.MiningLedgerEntries.SingleAsync(e => e.TypeId == 1230);
                Assert.Equal(500, ledger.Quantity);

                // Industrie-Tabelle ist additiv erzeugt und zunächst leer.
                Assert.Equal(0, await db.IndustryJobEntries.CountAsync());
            }
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task UniqueIndex_RejectsDuplicateJobPerCharacter()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"walleve-industry-dup-{Guid.NewGuid():N}.db");
        try
        {
            // Vollständig migriertes Schema (inkl. Industrie) auf frischer Datei-DB.
            await using (var db = CreateDb($"Data Source={dbPath}"))
            {
                await db.Database.MigrateAsync();

                var start = new DateTime(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc);
                db.IndustryJobEntries.Add(new IndustryJobEntry
                {
                    CharacterId = 90073315,
                    JobId = 100,
                    ActivityId = 1,
                    BlueprintTypeId = 1030,
                    Status = "active",
                    StartDate = start,
                    EndDate = start.AddHours(1),
                    UpdatedAt = DateTime.UtcNow
                });
                db.IndustryJobEntries.Add(new IndustryJobEntry
                {
                    CharacterId = 90073315,
                    JobId = 100,
                    ActivityId = 1,
                    BlueprintTypeId = 1030,
                    Status = "delivered",
                    StartDate = start,
                    EndDate = start.AddHours(1),
                    UpdatedAt = DateTime.UtcNow
                });

                await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
                Assert.Equal(0, await db.IndustryJobEntries.CountAsync());
            }
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task SameJobId_DifferentCharacters_DoesNotCollide()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"walleve-industry-owner-{Guid.NewGuid():N}.db");
        try
        {
            await using (var db = CreateDb($"Data Source={dbPath}"))
            {
                await db.Database.MigrateAsync();

                var start = new DateTime(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc);
                db.IndustryJobEntries.Add(new IndustryJobEntry
                {
                    CharacterId = 90073315,
                    JobId = 100,
                    ActivityId = 1,
                    BlueprintTypeId = 1030,
                    Status = "active",
                    StartDate = start,
                    EndDate = start.AddHours(1),
                    UpdatedAt = DateTime.UtcNow
                });
                db.IndustryJobEntries.Add(new IndustryJobEntry
                {
                    CharacterId = 90073316,
                    JobId = 100,
                    ActivityId = 1,
                    BlueprintTypeId = 1031,
                    Status = "active",
                    StartDate = start,
                    EndDate = start.AddHours(1),
                    UpdatedAt = DateTime.UtcNow
                });

                await db.SaveChangesAsync();
                Assert.Equal(2, await db.IndustryJobEntries.CountAsync());
            }
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }
}