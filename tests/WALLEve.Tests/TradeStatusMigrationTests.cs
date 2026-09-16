using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using WALLEve.Data;
using WALLEve.Models.Database;
using WALLEve.Models.Trading;

namespace WALLEve.Tests;

/// <summary>
/// Migrationstests für Issue #45 (Status-Historie): Die additive Migration
/// AddTradeStatusChanges legt die TradeStatusChanges-Tabelle an, ohne
/// bestehende Wallet-Historie, Cost-Basis-Werte, Ledger-Einträge,
/// App-Einstellungen, Trade-Verträge oder Trade-Profile anzutasten;
/// Nutzerdaten bleiben beim Upgrade vollständig erhalten.
/// </summary>
public class TradeStatusMigrationTests
{
    /// <summary>Letzte Migration VOR der Status-Historie-Migration (Trade-Profile #44).</summary>
    private const string PreviousMigration = "20260913164303_AddTradeProfiles";

    private static WalletDbContext CreateDb(string connectionString)
        => new(new DbContextOptionsBuilder<WalletDbContext>()
            .UseSqlite(connectionString)
            .Options);

    [Fact]
    public async Task Migrate_PreservesExistingUserData_AndCreatesStatusHistoryTable()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"walleve-tradestatus-{Guid.NewGuid():N}.db");
        try
        {
            var txDate = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Unspecified);

            // Phase 1: Schema bis VOR der Status-Historie-Migration und
            // Nutzerdaten anlegen: Wallet-Transaktion, Ledger-Eintrag,
            // manueller Cost-Basis-Wert, AppSetting, Trade-Vertrag und
            // eine aktive TradingOpportunity (Empfehlung, die erhalten bleibt).
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

                // Roh-SQL-Insert für das Bestandsprofil: Das aktuelle Modell kennt
                // die Issue-#68-Spalten (Cooldown/Deaktivierung/Fingerprint), die im
                // Schema VOR der Status-Historie-Migration noch nicht existieren — der
                // EF-INSERT mit dem neuen Modell würde hier fehlschlagen (additives
                // Schema-Problem, das diese Tests absichern).
                var profileName = "Mein Profil";
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"INSERT INTO TradeProfiles (CharacterId, Name, UpdatedAt, AllowHighSec, AllowLowSec, AllowNullSec, MaxCapital, MaxCargoVolume, MaxJumps, MinVolumeM3, MinProfit, MinQualityScore) VALUES ({90073315}, {profileName}, {DateTime.UtcNow}, {true}, {true}, {false}, {1000000m}, {10000m}, {20}, {1m}, {5000m}, {40})");

                // Roh-SQL-Insert: Das aktuelle Modell kennt die Issue-#46-Spalten
                // (Gebühren-Herkunft), die im Schema VOR der Status-Historie-Migration
                // noch nicht existieren — der EF-INSERT mit dem neuen Modell würde hier
                // fehlschlagen, genau das Additiv-Problem, das dieser Test absichert.
                // Parameterisiert (ExecuteSqlInterpolatedAsync) statt String-Konkatenation.
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"INSERT INTO TradingOpportunities (TypeId, CharacterId, OpportunityType, EstimatedProfit, RequiredCapital, Score, Provenance, AlgorithmVersion, DataQuality, Evidence, DetectedAt, ExpiresAt, Status) VALUES ({44992}, {90073315}, 'inventory_sell', {48000}, {1000000}, {60}, 'heuristic', 'inventory-sell-v1', 'complete', 'Test: 10 Einheiten, Cost Basis 100 ISK', {DateTime.UtcNow.AddHours(-1)}, {DateTime.UtcNow.AddHours(23)}, {RecommendationStatus.Active})");

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
                Assert.Equal(1, await db.TradeProfiles.CountAsync());

                // Empfehlung und ihre Eingaben bleiben erhalten.
                var opportunity = await db.TradingOpportunities.SingleAsync();
                Assert.Equal(RecommendationStatus.Active, opportunity.Status);
                Assert.Equal(1_000_000, opportunity.RequiredCapital);

                // Neue Tabelle existiert und ist additiv nutzbar.
                Assert.Empty(await db.TradeStatusChanges.ToListAsync());

                db.TradeStatusChanges.Add(new TradeStatusChange
                {
                    TradingOpportunityId = opportunity.Id,
                    CharacterId = 90073315,
                    FromStatus = RecommendationStatus.Active,
                    ToStatus = RecommendationStatus.Planned,
                    Source = TradeStatusSource.User,
                    ChangedAt = DateTime.UtcNow,
                    Note = "Beobachten"
                });
                await db.SaveChangesAsync();

                var change = await db.TradeStatusChanges.SingleAsync();
                Assert.Equal(RecommendationStatus.Planned, change.ToStatus);
                Assert.Equal(TradeStatusSource.User, change.Source);
                Assert.Equal(90073315, change.CharacterId);
            }
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }
}