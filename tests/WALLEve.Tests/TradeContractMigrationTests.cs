using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using WALLEve.Data;

namespace WALLEve.Tests;

/// <summary>
/// Issue #37, Akzeptanzkriterium 3: Die additive Migration AddTradeContracts
/// erhält bestehende Opportunities als Legacy-Verträge — IsLegacy = 1,
/// IsActionable = 0 mit Begründung, Evidenztext 1:1 übernommen, KEINE erfundene
/// neue Evidenz (fehlende Eingaben bleiben 0/leer/null). Andere
/// Opportunity-Typen ohne Vertragsart werden nicht angetastet.
/// </summary>
public class TradeContractMigrationTests
{
    /// <summary>Letzte Migration VOR AddTradeContracts (altes Schema ohne Vertragstabellen).</summary>
    private const string PreviousMigration = "20260913132848_AddStockpileTargets";

    private static WalletDbContext CreateDb(string connectionString)
        => new(new DbContextOptionsBuilder<WalletDbContext>()
            .UseSqlite(connectionString)
            .Options);

    [Fact]
    public async Task Migrate_PreservesExistingOpportunities_AsLegacyContracts()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"walleve-tradecontracts-{Guid.NewGuid():N}.db");
        try
        {
            // Phase 1: Schema VOR der Vertrags-Migration aufbauen, eine
            // Bestands-Opportunity (mit Evidenz) und eine fremde Art anlegen.
            await using (var db = CreateDb($"Data Source={dbPath}"))
            {
                await db.Database.GetService<IMigrator>().MigrateAsync(PreviousMigration);

                var inserted = await db.Database.ExecuteSqlRawAsync(
                    "INSERT INTO TradingOpportunities " +
                    "(TypeId, CharacterId, OpportunityType, BuyPrice, SellPrice, EstimatedProfit, " +
                    " RequiredCapital, Score, Provenance, DetectedAt, ExpiresAt, Status, Evidence, AlgorithmVersion) " +
                    "VALUES (34, 90073315, 'inventory_sell', 90.5, 120.25, 8300, 45250, 65, 'heuristic', " +
                    " '2026-09-01 10:00:00', '2026-09-02 10:00:00', 'active', 'Alte Evidenz: 500 x Item', 'inventory-sell-v1')");
                Assert.Equal(1, inserted);

                var insertedOther = await db.Database.ExecuteSqlRawAsync(
                    "INSERT INTO TradingOpportunities " +
                    "(TypeId, CharacterId, OpportunityType, BuyPrice, SellPrice, EstimatedProfit, " +
                    " RequiredCapital, Score, Provenance, DetectedAt, ExpiresAt, Status, Evidence) " +
                    "VALUES (35, 90073315, 'station_trading', 80, 130, 4100, 8000, 60, 'heuristic', " +
                    " '2026-09-01 10:00:00', '2026-09-02 10:00:00', 'active', 'Station Trade Evidenz')");
                Assert.Equal(1, insertedOther);
            }

            // Phase 2: AddTradeContracts anwenden und Legacy-Verträge prüfen.
            await using (var db = CreateDb($"Data Source={dbPath}"))
            {
                await db.Database.MigrateAsync();

                var opp = await db.TradingOpportunities.SingleAsync(o => o.TypeId == 34);
                Assert.Equal(90.5, opp.BuyPrice);
                Assert.Equal("Alte Evidenz: 500 x Item", opp.Evidence); // Opportunity selbst unangetastet

                var contract = await db.TradeContracts.SingleAsync(c =>
                    c.TradingOpportunityId == opp.Id && c.Kind == Models.Trading.TradeKind.InventorySell);
                Assert.True(contract.IsLegacy);                       // als Legacy gestempelt
                Assert.False(contract.IsActionable);                  // nicht ausführbar
                Assert.NotNull(contract.NotActionableReason);         // Begründung vorhanden
                Assert.Contains("Legacy", contract.NotActionableReason);

                // Vorhandene Werte 1:1 übernommen (decimal), Evidenz unverändert.
                Assert.Equal(90.5m, contract.UnitCostBasis);
                Assert.Equal(120.25m, contract.SellPricePerUnit);
                Assert.Equal(8300m, contract.EstimatedProfit);
                Assert.Equal(45250m, contract.RequiredCapital);
                Assert.Equal("inventory-sell-v1", contract.AlgorithmVersion);
                Assert.Equal(opp.DetectedAt, contract.CreatedAt);
                Assert.Equal("Alte Evidenz: 500 x Item", contract.Evidence);

                // KEINE erfundene Evidenz: fehlende Eingaben bleiben NULL/leer.
                Assert.Null(contract.Quantity);
                Assert.Null(contract.SellLocationId);
                Assert.Null(contract.BrokerFee);
                Assert.Null(contract.SalesTax);
                Assert.Null(contract.MarketSnapshotId);
                Assert.Null(contract.CostBasisSource);
                Assert.Null(contract.SellLocationLabel);

                // Fremder Opportunity-Typ erzeugt keinen Vertrag.
                Assert.Equal(0, await db.TradeContracts.CountAsync(c => c.CharacterId == 90073315 && c.TypeId == 35));
            }

            // Phase 3: Neue Datensätze nach der Migration bleiben vertragslos bis
            // zur Analyse — Schema ist additiv (alte Spalten unverändert nutzbar).
            await using (var db = CreateDb($"Data Source={dbPath}"))
            {
                var inserted = await db.Database.ExecuteSqlRawAsync(
                    "INSERT INTO TradingOpportunities " +
                    "(TypeId, CharacterId, OpportunityType, BuyPrice, SellPrice, EstimatedProfit, " +
                    " RequiredCapital, Score, Provenance, DetectedAt, ExpiresAt, Status, Evidence, AlgorithmVersion) " +
                    "VALUES (36, 90073315, 'inventory_sell', 10, 20, 500, 1000, 55, 'heuristic', " +
                    " '2026-09-13 10:00:00', '2026-09-13 11:00:00', 'active', 'Neue Evidenz', 'inventory-sell-v1')");
                Assert.Equal(1, inserted);
                await db.SaveChangesAsync();

                var totalContracts = await db.TradeContracts.CountAsync();
                Assert.Equal(1, totalContracts); // nur der Legacy-Vertrag aus Phase 1
            }
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }
}