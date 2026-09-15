using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using WALLEve.Data;
using WALLEve.Models.Database;
using WALLEve.Models.Trading;

namespace WALLEve.Tests;

/// <summary>
/// Migrationstest für Issue #60 (Zuordnung von Wallet-Ergebnissen): Die additive
/// Migration AddRecommendationAttributions legt die Tabellen für Zuordnungen,
/// Transaktions-Links und Netto-Korrekturen an, ohne bestehende Wallet-Historie,
/// Cost-Basis-Werte, Einstellungen, Empfehlungen, Status-Historie, Verträge oder
/// Profile anzutasten — Nutzerdaten bleiben beim Upgrade vollständig erhalten.
/// </summary>
public class RecommendationAttributionMigrationTests
{
    /// <summary>Letzte Migration VOR der Zuordnungs-Migration (Gebühren-Herkunft #46).</summary>
    private const string PreviousMigration = "20260915081539_AddFeeOriginToTradingOpportunity";

    private static WalletDbContext CreateDb(string connectionString)
        => new(new DbContextOptionsBuilder<WalletDbContext>()
            .UseSqlite(connectionString)
            .Options);

    [Fact]
    public async Task Migrate_PreservesExistingUserData_AndCreatesAttributionTables()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"walleve-attribution-{Guid.NewGuid():N}.db");
        try
        {
            var txDate = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Unspecified);

            // Phase 1: Schema bis VOR der Zuordnungs-Migration plus Nutzerdaten.
            await using (var db = CreateDb($"Data Source={dbPath}"))
            {
                await db.Database.GetService<IMigrator>().MigrateAsync(PreviousMigration);

                db.WalletTransactionRecords.Add(new WalletTransactionRecord
                {
                    CharacterId = 90073315,
                    TransactionId = 4001,
                    TypeId = 44992,
                    Date = txDate,
                    IsBuy = false,
                    IsPersonal = true,
                    JournalRefId = 999,
                    LocationId = 60003760,
                    Quantity = 10,
                    UnitPrice = 150
                });

                db.CostBasisLedgerEntries.Add(new CostBasisLedgerEntry
                {
                    CharacterId = 90073315,
                    TypeId = 44992,
                    SourceTransactionId = 4001,
                    Date = txDate,
                    IsBuy = true,
                    Quantity = 10,
                    UnitPrice = 100,
                    ImportedAt = DateTime.UtcNow
                });

                db.AppSettings.Add(new AppSetting
                {
                    Key = "CostBasis.DefaultRegionId",
                    Value = "10000002"
                });

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

                var opportunity = new TradingOpportunity
                {
                    TypeId = 44992,
                    CharacterId = 90073315,
                    OpportunityType = "inventory_sell",
                    EstimatedProfit = 48_000,
                    RequiredCapital = 1_000_000,
                    Score = 60,
                    Provenance = TradingOpportunity.ProvenanceHeuristic,
                    AlgorithmVersion = "inventory-sell-v1",
                    DataQuality = "complete",
                    Evidence = "Test: 10 Einheiten, Verkauf 150 ISK",
                    DetectedAt = DateTime.UtcNow.AddHours(-1),
                    ExpiresAt = DateTime.UtcNow.AddHours(23),
                    Status = RecommendationStatus.Active,
                    BrokerFeeRate = 0.015,
                    SalesTaxRate = 0.0337,
                    BrokerFeeOrigin = "automatic",
                    SalesTaxOrigin = "automatic",
                    StandingsOrigin = "estimated",
                    FeeEvaluatedAtUtc = DateTime.UtcNow
                };
                db.TradingOpportunities.Add(opportunity);
                await db.SaveChangesAsync();

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
            }

            // Phase 2: auf die neueste Migration aktualisieren.
            await using (var db = CreateDb($"Data Source={dbPath}"))
            {
                await db.Database.GetService<IMigrator>().MigrateAsync();

                // Bestehende Nutzerdaten unangetastet.
                Assert.Equal(1, await db.WalletTransactionRecords.CountAsync());
                Assert.Equal(1, await db.CostBasisLedgerEntries.CountAsync());
                Assert.Equal("10000002", (await db.AppSettings.FindAsync("CostBasis.DefaultRegionId"))!.Value);
                Assert.Equal(1, await db.TradeProfiles.CountAsync());
                Assert.Equal(1, await db.TradeStatusChanges.CountAsync());

                var opportunity = await db.TradingOpportunities.SingleAsync();
                Assert.Equal(0.015, opportunity.BrokerFeeRate!.Value, 6);
                Assert.Equal("automatic", opportunity.BrokerFeeOrigin);
                Assert.Equal(RecommendationStatus.Active, opportunity.Status);

                // Neue Tabellen existieren und sind additiv nutzbar.
                Assert.Empty(await db.RecommendationAttributions.ToListAsync());
                Assert.Empty(await db.AttributionTransactionLinks.ToListAsync());
                Assert.Empty(await db.ActualNetCorrections.ToListAsync());

                var attribution = new RecommendationAttribution
                {
                    TradingOpportunityId = opportunity.Id,
                    CharacterId = 90073315,
                    TypeId = 44992,
                    Side = TradeSide.Sell,
                    MatchState = AttributionMatchState.Unique,
                    ExpectedQuantity = 10,
                    AttributedQuantity = 10,
                    ExpectedNetMin = 400m,
                    ExpectedNetMax = 600m,
                    ActualNet = 1426.95m,
                    FeeKnowledge = AttributionFeeKnowledge.Known,
                    Source = AttributionSource.System,
                    WindowStartUtc = txDate,
                    WindowEndUtc = txDate.AddDays(1),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                db.RecommendationAttributions.Add(attribution);
                await db.SaveChangesAsync();

                db.AttributionTransactionLinks.Add(new AttributionTransactionLink
                {
                    AttributionId = attribution.Id,
                    TradingOpportunityId = opportunity.Id,
                    CharacterId = 90073315,
                    TransactionId = 4001,
                    TypeId = 44992,
                    TransactionDate = txDate,
                    Side = TradeSide.Sell,
                    Quantity = 10,
                    UnitPrice = 150,
                    CreatedAt = DateTime.UtcNow
                });

                db.ActualNetCorrections.Add(new ActualNetCorrection
                {
                    AttributionId = attribution.Id,
                    TradingOpportunityId = opportunity.Id,
                    CharacterId = 90073315,
                    PreviousActualNet = 1426.95m,
                    NewActualNet = 1400m,
                    Reason = "Gebühr manuell geprüft",
                    Source = AttributionSource.User,
                    CorrectedAt = DateTime.UtcNow
                });
                await db.SaveChangesAsync();

                Assert.Equal(1, await db.AttributionTransactionLinks.CountAsync());
                Assert.Equal(1400m, (await db.ActualNetCorrections.SingleAsync()).NewActualNet!.Value);
            }
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }
}