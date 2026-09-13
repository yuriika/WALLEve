using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using WALLEve.Data;
using WALLEve.Models.Database;

namespace WALLEve.Tests;

/// <summary>
/// Migrationstests für Issue #58 (Hub-/Vergleichsmarktprofile):
/// Die additive Migration AddMarketHubProfiles legt die MarketHubProfiles-Tabelle
/// an, ohne bestehende Wallet-Historie, Cost-Basis-Werte (inkl. manueller),
/// Ledger-Einträge oder App-Einstellungen anzutasten.
/// </summary>
public class HubProfileMigrationTests
{
    /// <summary>Letzte Migration VOR der Hub-Profil-Migration (Buchungsledger #52).</summary>
    private const string PreviousMigration = "20260912214317_AddCostBasisLedger";

    private static WalletDbContext CreateDb(string connectionString)
        => new(new DbContextOptionsBuilder<WalletDbContext>()
            .UseSqlite(connectionString)
            .Options);

    [Fact]
    public async Task Migrate_PreservesExistingUserData_AndCreatesProfilesTable()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"walleve-hubprofile-{Guid.NewGuid():N}.db");
        try
        {
            var txDate = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Unspecified);

            // Phase 1: Schema bis VOR der Hub-Profil-Migration und
            // Nutzerdaten anlegen: Wallet-Transaktion, Ledger-Eintrag,
            // manueller Cost-Basis-Wert, AppSetting (DefaultRegion).
            await using (var db = CreateDb($"Data Source={dbPath}"))
            {
                await db.Database.GetService<IMigrator>().MigrateAsync(PreviousMigration);

                db.WalletTransactionRecords.Add(new WalletTransactionRecord
                {
                    CharacterId = 90073315,
                    TransactionId = 2001,
                    TypeId = 44992,
                    Date = txDate,
                    IsBuy = true,
                    IsPersonal = true,
                    JournalRefId = 777,
                    LocationId = 60003760,
                    Quantity = 10,
                    UnitPrice = 100
                });

                db.CostBasisLedgerEntries.Add(new CostBasisLedgerEntry
                {
                    CharacterId = 90073315,
                    TypeId = 44992,
                    SourceTransactionId = 2001,
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

                // Neue Tabelle existiert und ist nutzbar (leer, additiv).
                Assert.Empty(await db.MarketHubProfiles.ToListAsync());

                db.MarketHubProfiles.Add(new MarketHubProfile
                {
                    Name = "Jita",
                    RegionId = 10000002,
                    SystemId = 30000142,
                    IsActiveHub = true,
                    UpdatedAt = DateTime.UtcNow
                });
                await db.SaveChangesAsync();

                var profile = await db.MarketHubProfiles.SingleAsync();
                Assert.Equal(30000142, profile.SystemId);
                Assert.True(profile.IsActiveHub);
            }
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }
}