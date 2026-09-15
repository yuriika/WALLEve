using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using WALLEve.Data;
using WALLEve.Models.Database;

namespace WALLEve.Tests;

/// <summary>
/// Migrationstest für Issue #46 (Gebühren-Herkunft): Die additive Migration
/// AddFeeOriginToTradingOpportunity ergänzt die Spalten BrokerFeeRate,
/// SalesTaxRate, BrokerFeeOrigin, SalesTaxOrigin, StandingsOrigin und
/// FeeEvaluatedAtUtc auf TradingOpportunities, ohne bestehende
/// Empfehlungen, Wallet-Historie oder Cost-Basis-Daten anzutasten.
/// </summary>
public class FeeOriginMigrationTests
{
    /// <summary>Letzte Migration VOR der Gebühren-Herkunfts-Migration (Status-Historie #45).</summary>
    private const string PreviousMigration = "20260915064611_AddTradeStatusChanges";

    private static WalletDbContext CreateDb(string connectionString)
        => new(new DbContextOptionsBuilder<WalletDbContext>()
            .UseSqlite(connectionString)
            .Options);

    [Fact]
    public async Task Migrate_PreservesExistingOpportunity_AndAddsFeeOriginColumns()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"walleve-feeorigin-{Guid.NewGuid():N}.db");
        try
        {
            // Phase 1: Schema bis VOR der Gebühren-Herkunfts-Migration und
            // eine bestehende Opportunity als Nutzerdaten anlegen.
            await using (var db = CreateDb($"Data Source={dbPath}"))
            {
                await db.Database.GetService<IMigrator>().MigrateAsync(PreviousMigration);

                // Insert per Roh-SQL: Das aktuelle Modell kennt die Issue-#46-Spalten,
                // die im Schema VOR der Migration noch nicht existieren (der EF-INSERT
                // mit dem neuen Modell würde hier fehlschlagen — genau das Additiv-Problem,
                // das diese Migration testet). Parameterisiert statt String-Konkatenation.
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"INSERT INTO TradingOpportunities (CharacterId, TypeId, OpportunityType, EstimatedProfit, RequiredCapital, Score, Provenance, Evidence, DetectedAt, ExpiresAt, Status) VALUES ({90073315}, {44992}, 'inventory_sell', {8450}, {90000}, {69}, 'heuristic', 'bestehende Empfehlung', {new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc)}, {new DateTime(2026, 9, 1, 11, 0, 0, DateTimeKind.Utc)}, 'planned')");
            }

            // Phase 2: Upgrade auf die neueste Migration (inkl. Issue #46).
            await using (var db = CreateDb($"Data Source={dbPath}"))
            {
                await db.Database.GetService<IMigrator>().MigrateAsync();

                var opp = await db.TradingOpportunities.SingleAsync();

                // Bestehende Nutzerdaten bleiben vollständig erhalten.
                Assert.Equal(44992, opp.TypeId);
                Assert.Equal(8450, opp.EstimatedProfit, 2);
                Assert.Equal("bestehende Empfehlung", opp.Evidence);

                // Neue Spalten sind vorhanden und initial leer (nullable, additiv).
                Assert.Null(opp.BrokerFeeRate);
                Assert.Null(opp.SalesTaxRate);
                Assert.Null(opp.BrokerFeeOrigin);
                Assert.Null(opp.SalesTaxOrigin);
                Assert.Null(opp.StandingsOrigin);
                Assert.Null(opp.FeeEvaluatedAtUtc);

                // Spalten sind beschreibbar: frisch persistierte Herkunftswerte
                // werden unverändert gelesen (Rundreise).
                opp.BrokerFeeRate = 0.03;
                opp.SalesTaxRate = 0.075;
                opp.BrokerFeeOrigin = "automatic";
                opp.SalesTaxOrigin = "automatic";
                opp.StandingsOrigin = "estimated";
                opp.FeeEvaluatedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync();

                var reloaded = await db.TradingOpportunities.SingleAsync();
                Assert.Equal(0.03, reloaded.BrokerFeeRate!.Value, 6);
                Assert.Equal(0.075, reloaded.SalesTaxRate!.Value, 6);
                Assert.Equal("automatic", reloaded.BrokerFeeOrigin);
                Assert.Equal("estimated", reloaded.StandingsOrigin);
                Assert.NotNull(reloaded.FeeEvaluatedAtUtc);
            }
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }
}