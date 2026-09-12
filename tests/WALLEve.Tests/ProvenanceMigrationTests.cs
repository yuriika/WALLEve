using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using WALLEve.Data;
using WALLEve.Models.Database;

namespace WALLEve.Tests;

/// <summary>
/// Migrationstest für Issue #33 (Provenienz statt AI-Confidence):
/// Die additive Migration AddProvenanceToTradingOpportunity muss bestehende
/// Trading-Opportunities vollständig erhalten. Alte Confidence-/AIModel-/
/// Reasoning-Werte wandern korrekt in Score/Provenance/Evidence, und alle
/// vor der Migration erzeugten Datensätze werden als "legacy" gestempelt —
/// ihre alten Confidence-Werte sind KEINE neue Evidenz.
/// </summary>
public class ProvenanceMigrationTests
{
    /// <summary>Letzte Migration VOR der Provenienz-Migration (altes Schema mit Confidence/AIModel/Reasoning).</summary>
    private const string PreviousMigration = "20260907145303_AddCharacterToTradingOpportunity";

    private static WalletDbContext CreateDb(string connectionString)
        => new(new DbContextOptionsBuilder<WalletDbContext>()
            .UseSqlite(connectionString)
            .Options);

    [Fact]
    public async Task Migrate_PreservesExistingRecords_AndFlagsThemAsLegacy()
    {
        // Echte Datei-DB, damit alle Migrationen (inkl. Index/DATA-Änderungen) real durchlaufen.
        var dbPath = Path.Combine(Path.GetTempPath(), $"walleve-provenance-{Guid.NewGuid():N}.db");
        try
        {
            // Phase 1: Schema bis VOR der Provenienz-Migration aufbauen und einen
            // Legacy-Datensatz im ALTEN Schema (Confidence/AIModel/Reasoning) anlegen.
            await using (var db = CreateDb($"Data Source={dbPath}"))
            {
                await db.Database.GetService<IMigrator>().MigrateAsync(PreviousMigration);

                var inserted = await db.Database.ExecuteSqlRawAsync(
                    "INSERT INTO TradingOpportunities " +
                    "(TypeId, CharacterId, OpportunityType, BuyPrice, SellPrice, EstimatedProfit, " +
                    " RequiredCapital, Confidence, AIModel, Reasoning, DetectedAt, ExpiresAt, Status) " +
                    "VALUES (1, 90073315, 'inventory_sell', 90, 110, 8450, 90000, " +
                    " 70, 'llama3.1:8b', 'alte AI-Begruendung', '2026-09-01 10:00:00', '2026-09-02 10:00:00', 'active')");
                Assert.Equal(1, inserted);
            }

            // Phase 2: Provenienz-Migration anwenden und Datensatz zurücklesen.
            await using (var db = CreateDb($"Data Source={dbPath}"))
            {
                await db.Database.MigrateAsync();

                var opp = await db.TradingOpportunities.SingleAsync(o => o.TypeId == 1);

                // Datensatz bleibt erhalten; Werte landen an der richtigen Stelle.
                Assert.Equal(70, opp.Score);                          // Confidence-Wert bewahrt
                Assert.Equal("legacy", opp.Provenance);               // explizit als Legacy gestempelt
                Assert.Equal("alte AI-Begruendung", opp.Evidence);    // Reasoning-Text als Evidenz bewahrt
                Assert.Equal(8450.0, opp.EstimatedProfit);
                Assert.Equal("inventory_sell", opp.OpportunityType);
                Assert.Equal("active", opp.Status);

                // Legacy-Daten tragen KEINE neue Evidenz: keine Algorithmusversion,
                // keine Datenqualität — die App wertet sie nicht als aktuelle Evidenz.
                Assert.Null(opp.AlgorithmVersion);
                Assert.Null(opp.DataQuality);
            }

            // Phase 3: Neue Datensätze nach der Migration (Antwort der App) tragen
            // ehrliche Provenienz statt AI-Angaben (Rückwärtskompatibilität des Schemas).
            await using (var db = CreateDb($"Data Source={dbPath}"))
            {
                db.TradingOpportunities.Add(new TradingOpportunity
                {
                    CharacterId = 90073315,
                    TypeId = 2,
                    OpportunityType = "inventory_sell",
                    EstimatedProfit = 42.25,
                    RequiredCapital = 450.0,
                    Score = 65,
                    Provenance = TradingOpportunity.ProvenanceHeuristic,
                    AlgorithmVersion = "inventory-sell-v1",
                    DataQuality = "partial",
                    Evidence = "Ortsgebunden (Station): 5 × Item — Verkauf ...",
                    DetectedAt = DateTime.UtcNow,
                    ExpiresAt = DateTime.UtcNow.AddHours(1),
                    Status = "active"
                });
                await db.SaveChangesAsync();

                var newer = await db.TradingOpportunities.SingleAsync(o => o.TypeId == 2);
                Assert.Equal(TradingOpportunity.ProvenanceHeuristic, newer.Provenance);
                Assert.Equal("inventory-sell-v1", newer.AlgorithmVersion);
            }
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }
}