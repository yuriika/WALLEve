using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using WALLEve.Data;
using WALLEve.Models.Industry;

namespace WALLEve.Tests;

/// <summary>
/// Migrationstests für Issue #49 (Blueprint-Register, M6):
/// Die additive Migration AddBlueprintEntries schafft die Blueprint-Tabelle mit
/// Idempotenz-Unique-Index (CharacterId, ItemId), ohne bestehende Nutzerdaten
/// (inkl. Industrie-Job-Register aus #40) anzutasten; der Unique-Index weist
/// Duplikate ab, gleiche ItemIds verschiedener Owner kollidieren nicht.
/// </summary>
public class BlueprintSchemaMigrationTests
{
    /// <summary>Letzte Migration VOR der Blueprint-Migration (bestehendes Wallet-Schema inkl. Industrie-Jobs).</summary>
    private const string PreviousMigration = "20260917111216_AddIndustryJobEntries";

    private static WalletDbContext CreateDb(string connectionString)
        => new(new DbContextOptionsBuilder<WalletDbContext>()
            .UseSqlite(connectionString)
            .Options);

    [Fact]
    public async Task Migrate_PreservesWalletDataAndCreatesBlueprintTable()
    {
        // Echte Datei-DB, damit alle Migrationen (inkl. Index-/DATA-Änderungen) real durchlaufen.
        var dbPath = Path.Combine(Path.GetTempPath(), $"walleve-blueprint-{Guid.NewGuid():N}.db");
        try
        {
            var walletDate = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Unspecified);

            // Phase 1: Schema bis VOR der Blueprint-Migration aufbauen und Bestandsdaten
            // des betroffenen Bereichs anlegen (Character + Industrie-Job aus #40).
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
                db.IndustryJobEntries.Add(new IndustryJobEntry
                {
                    CharacterId = 90073315,
                    JobId = 100,
                    ActivityId = 1,
                    BlueprintTypeId = 1030,
                    Status = "delivered",
                    StartDate = walletDate,
                    EndDate = walletDate.AddHours(1),
                    UpdatedAt = walletDate
                });
                await db.SaveChangesAsync();
            }

            // Phase 2: Blueprint-Migration anwenden und ALLE Bestandsdaten zurücklesen.
            await using (var db = CreateDb($"Data Source={dbPath}"))
            {
                await db.Database.MigrateAsync();

                var character = await db.Characters.SingleAsync(c => c.CharacterId == 90073315);
                Assert.Equal("Test Alt", character.CharacterName);

                var job = await db.IndustryJobEntries.SingleAsync(e => e.JobId == 100);
                Assert.Equal("delivered", job.Status);
                Assert.Equal(1030, job.BlueprintTypeId);

                // Blueprint-Tabelle ist additiv erzeugt und zunächst leer.
                Assert.Equal(0, await db.BlueprintEntries.CountAsync());
            }
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task UniqueIndex_RejectsDuplicateItemPerCharacter()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"walleve-blueprint-dup-{Guid.NewGuid():N}.db");
        try
        {
            // Vollständig migriertes Schema (inkl. Blueprints) auf frischer Datei-DB.
            await using (var db = CreateDb($"Data Source={dbPath}"))
            {
                await db.Database.MigrateAsync();

                var now = DateTime.UtcNow;
                db.BlueprintEntries.Add(new BlueprintEntry
                {
                    CharacterId = 90073315,
                    ItemId = 1000,
                    TypeId = 1030,
                    Runs = -1,
                    IsBlueprintCopy = false,
                    UpdatedAt = now
                });
                db.BlueprintEntries.Add(new BlueprintEntry
                {
                    CharacterId = 90073315,
                    ItemId = 1000,
                    TypeId = 1030,
                    Runs = 50,
                    IsBlueprintCopy = true,
                    UpdatedAt = now
                });

                await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
                Assert.Equal(0, await db.BlueprintEntries.CountAsync());
            }
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task SameItemId_DifferentCharacters_DoesNotCollide()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"walleve-blueprint-owner-{Guid.NewGuid():N}.db");
        try
        {
            await using (var db = CreateDb($"Data Source={dbPath}"))
            {
                await db.Database.MigrateAsync();

                var now = DateTime.UtcNow;
                db.BlueprintEntries.Add(new BlueprintEntry
                {
                    CharacterId = 90073315,
                    ItemId = 1000,
                    TypeId = 1030,
                    Runs = -1,
                    IsBlueprintCopy = false,
                    UpdatedAt = now
                });
                db.BlueprintEntries.Add(new BlueprintEntry
                {
                    CharacterId = 90073316,
                    ItemId = 1000,
                    TypeId = 1031,
                    Runs = 25,
                    IsBlueprintCopy = true,
                    UpdatedAt = now
                });

                await db.SaveChangesAsync();
                Assert.Equal(2, await db.BlueprintEntries.CountAsync());
            }
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }
}