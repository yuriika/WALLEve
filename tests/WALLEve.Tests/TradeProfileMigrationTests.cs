using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using WALLEve.Data;
using WALLEve.Models.Database;
using WALLEve.Models.Trading;

namespace WALLEve.Tests;

/// <summary>
/// Migrationstests für Issue #44 (Handelsprofile): Die additive Migration
/// AddTradeProfiles legt die TradeProfiles-Tabelle an, ohne bestehende
/// Wallet-Historie, Cost-Basis-Werte, Ledger-Einträge, Trade-Verträge oder
/// App-Einstellungen anzutasten; Nutzerdaten bleiben beim Upgrade erhalten.
/// </summary>
public class TradeProfileMigrationTests
{
    /// <summary>Letzte Migration VOR der Trade-Profile-Migration (Trade-Verträge #37).</summary>
    private const string PreviousMigration = "20260913161711_AddTradeContracts";

    private static WalletDbContext CreateDb(string connectionString)
        => new(new DbContextOptionsBuilder<WalletDbContext>()
            .UseSqlite(connectionString)
            .Options);

    [Fact]
    public async Task Migrate_PreservesExistingUserData_AndCreatesTradeProfileTable()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"walleve-tradeprofile-{Guid.NewGuid():N}.db");
        try
        {
            var txDate = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Unspecified);

            // Phase 1: Schema bis VOR der Trade-Profile-Migration und
            // Nutzerdaten anlegen: Wallet-Transaktion, Ledger-Eintrag,
            // manueller Cost-Basis-Wert, AppSetting und ein Trade-Vertrag.
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

                db.TradeContracts.Add(new TradeContract
                {
                    TradingOpportunityId = 7,
                    Kind = TradeKind.InventorySell,
                    CharacterId = 90073315,
                    TypeId = 44992,
                    AlgorithmVersion = "inventory-sell-v1",
                    CreatedAt = DateTime.UtcNow,
                    IsActionable = true,
                    Evidence = "Testvertrag",
                    EstimatedProfit = 12_345m,
                    RequiredCapital = 1_000m,
                    NetRoiPercent = 12.3m
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
                Assert.Equal(1, await db.TradeContracts.CountAsync());

                // Neue Tabelle existiert und ist additiv nutzbar.
                Assert.Empty(await db.TradeProfiles.ToListAsync());

                db.TradeProfiles.Add(new TradeProfile
                {
                    CharacterId = 90073315,
                    Name = "Mein Profil",
                    UpdatedAt = DateTime.UtcNow,
                    MaxCapital = 1_000_000m,
                    MaxCargoVolume = 10_000m,
                    MaxJumps = 20,
                    AllowHighSec = true,
                    AllowLowSec = true,
                    AllowNullSec = false,
                    MinVolumeM3 = 1m,
                    MinProfit = 5_000m,
                    MinQualityScore = 40
                });
                await db.SaveChangesAsync();

                var profile = await db.TradeProfiles.SingleAsync();
                Assert.Equal(90073315, profile.CharacterId);
                Assert.Equal(1_000_000m, profile.MaxCapital);
                Assert.Equal(40, profile.MinQualityScore);
            }
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }
}