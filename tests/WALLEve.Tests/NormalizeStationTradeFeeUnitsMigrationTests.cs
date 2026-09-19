using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using WALLEve.Data;
using WALLEve.Models.Database;

namespace WALLEve.Tests;

/// <summary>
/// Migrationstest für Issue #180: Normalisierung der BrokerFeeRate/SalesTaxRate
/// bestehender station_trading-Opportunities von Prozent (1.5 = 1,5 %) auf
/// dimensionslose Rate (0.015 = 1,5 %). Nur station_trading-Zeilen werden
/// angefasst — inventory_sell-Zeilen haben bereits die korrekte Rate.
/// </summary>
public class NormalizeStationTradeFeeUnitsMigrationTests
{
    private const string PreviousMigration = "20260917121654_AddBlueprintEntries";

    private static WalletDbContext CreateDb(string connectionString)
        => new(new DbContextOptionsBuilder<WalletDbContext>()
            .UseSqlite(connectionString)
            .Options);

    [Fact]
    public async Task NormalizeSql_DividesStationTradeFeeRatesBy100()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"walleve-stationtrade-feenorm-{Guid.NewGuid():N}.db");
        try
        {
            // Einfache DB ohne Migrations: direkt SQL ausführen
            using (var db = CreateDb($"Data Source={dbPath}"))
            {
                await db.Database.EnsureCreatedAsync();
            }

            // Phase 1: Daten einfügen (BrokerFeeRate = 1.5 as percentage)
            await using (var db = CreateDb($"Data Source={dbPath}"))
            {
                var opp = new TradingOpportunity
                {
                    CharacterId = 90073315,
                    TypeId = 34,
                    OpportunityType = "station_trading",
                    BuyRegionId = 10000002,
                    SellRegionId = 10000002,
                    EstimatedProfit = 858,
                    RequiredCapital = 4800,
                    Score = 70,
                    Provenance = "heuristic",
                    Evidence = "Station-Trade Test-Evidenz",
                    DetectedAt = new DateTime(2026, 9, 18, 10, 0, 0, DateTimeKind.Utc),
                    ExpiresAt = new DateTime(2026, 9, 18, 12, 0, 0, DateTimeKind.Utc),
                    Status = "planned",
                    BrokerFeeRate = 1.5,
                    SalesTaxRate = 3.0,
                    BrokerFeeOrigin = "automatic",
                    SalesTaxOrigin = "automatic",
                    StandingsOrigin = "estimated"
                };
                db.TradingOpportunities.Add(opp);
                await db.SaveChangesAsync();
            }

            // Phase 2: Normalisierungs-SQL direkt ausführen
            await using (var db = CreateDb($"Data Source={dbPath}"))
            {
                // Zuerst prüfen: Wert ist 1.5
                var before = await db.TradingOpportunities.AsNoTracking().FirstAsync();
                Assert.Equal(1.5, before.BrokerFeeRate!.Value, 6);

                // SQL direkt ausführen (wie die Migration)
                await db.Database.ExecuteSqlRawAsync("""
                    UPDATE "TradingOpportunities"
                    SET "BrokerFeeRate" = "BrokerFeeRate" / 100.0,
                        "SalesTaxRate" = "SalesTaxRate" / 100.0
                    WHERE "OpportunityType" = 'station_trading'
                      AND "BrokerFeeRate" IS NOT NULL
                      AND "SalesTaxRate" IS NOT NULL
                      AND "BrokerFeeRate" > 1.0;
                    """);

                // Nach der Normalisierung
                var after = await db.TradingOpportunities.AsNoTracking().FirstAsync();
                Assert.NotNull(after.BrokerFeeRate);
                Assert.Equal(0.015, after.BrokerFeeRate!.Value, 6);
                Assert.NotNull(after.SalesTaxRate);
                Assert.Equal(0.03, after.SalesTaxRate!.Value, 6);
            }
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task NormalizeSql_DoesNotTouchInventorySellRows()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"walleve-invsell-untouched-{Guid.NewGuid():N}.db");
        try
        {
            using (var db = CreateDb($"Data Source={dbPath}"))
            {
                await db.Database.EnsureCreatedAsync();
            }

            await using (var db = CreateDb($"Data Source={dbPath}"))
            {
                var opp = new TradingOpportunity
                {
                    CharacterId = 90073315,
                    TypeId = 34,
                    OpportunityType = "inventory_sell",
                    EstimatedProfit = 5000,
                    RequiredCapital = 100000,
                    Score = 75,
                    Provenance = "heuristic",
                    Evidence = "Inventory-Sell Test",
                    DetectedAt = new DateTime(2026, 9, 18, 10, 0, 0, DateTimeKind.Utc),
                    ExpiresAt = new DateTime(2026, 9, 18, 12, 0, 0, DateTimeKind.Utc),
                    Status = "planned",
                    BrokerFeeRate = 0.015,
                    SalesTaxRate = 0.025,
                    BrokerFeeOrigin = "automatic",
                    SalesTaxOrigin = "automatic",
                    StandingsOrigin = "estimated"
                };
                db.TradingOpportunities.Add(opp);
                await db.SaveChangesAsync();
            }

            await using (var db = CreateDb($"Data Source={dbPath}"))
            {
                await db.Database.ExecuteSqlRawAsync("""
                    UPDATE "TradingOpportunities"
                    SET "BrokerFeeRate" = "BrokerFeeRate" / 100.0,
                        "SalesTaxRate" = "SalesTaxRate" / 100.0
                    WHERE "OpportunityType" = 'station_trading'
                      AND "BrokerFeeRate" IS NOT NULL
                      AND "SalesTaxRate" IS NOT NULL
                      AND "BrokerFeeRate" > 1.0;
                    """);

                var opp = await db.TradingOpportunities.AsNoTracking().SingleAsync();
                Assert.NotNull(opp.BrokerFeeRate);
                Assert.Equal(0.015, opp.BrokerFeeRate!.Value, 6);
                Assert.Equal(0.025, opp.SalesTaxRate!.Value, 6);
            }
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }
}