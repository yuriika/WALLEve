using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using WALLEve.Data;
using WALLEve.Models.Trading;

namespace WALLEve.Tests;

/// <summary>
/// Issue #45, Akzeptanzkriterium 2: Die additive Migration AddTradeStatusChanges
/// erstellt die Historientabelle, ohne bestehende Opportunities anzutasten —
/// Ablauf/Invalidierung bewahrt die ursprüngliche Empfehlung samt Eingaben.
/// Der Legacy-Status "active" bleibt als Dokumentation erhalten und wird erst
/// beim Lesen als planned normalisiert (kein Daten-Update).
/// </summary>
public class TradeStatusMigrationTests
{
    /// <summary>Letzte Migration VOR AddTradeStatusChanges (altes Schema ohne Statushistorie).</summary>
    private const string PreviousMigration = "20260913164303_AddTradeProfiles";

    private static WalletDbContext CreateDb(string connectionString)
        => new(new DbContextOptionsBuilder<WalletDbContext>()
            .UseSqlite(connectionString)
            .Options);

    [Fact]
    public async Task Migrate_CreatesStatusHistoryTable_PreservesOpportunitiesAndInputs()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"walleve-tradestatus-{Guid.NewGuid():N}.db");
        try
        {
            // Phase 1: Schema VOR der Status-Änderung aufbauen und eine aktive
            // Bestands-Opportunity (mit Evidenz = ursprüngliche Empfehlung) anlegen.
            await using (var db = CreateDb($"Data Source={dbPath}"))
            {
                await db.Database.GetService<IMigrator>().MigrateAsync(PreviousMigration);

                var inserted = await db.Database.ExecuteSqlRawAsync(
                    "INSERT INTO TradingOpportunities " +
                    "(TypeId, CharacterId, OpportunityType, BuyPrice, SellPrice, EstimatedProfit, " +
                    " RequiredCapital, Score, Provenance, DetectedAt, ExpiresAt, Status, Evidence, AlgorithmVersion) " +
                    "VALUES (34, 90073315, 'inventory_sell', 90.5, 120.25, 8300, 45250, 65, 'heuristic', " +
                    " '2026-09-01 10:00:00', '2026-09-02 10:00:00', 'active', 'Ortsgebunden: 500 x Item', 'inventory-sell-v1')");
                Assert.Equal(1, inserted);
            }

            // Phase 2: AddTradeStatusChanges anwenden — Tabelle entsteht, Daten bleiben.
            await using (var db = CreateDb($"Data Source={dbPath}"))
            {
                await db.Database.MigrateAsync();

                // Ursprüngliche Empfehlung samt Evidenz unverändert vorhanden.
                var opp = await db.TradingOpportunities.SingleAsync(o => o.TypeId == 34);
                Assert.Equal("active", opp.Status); // Legacy-Wert wird nicht umgeschrieben
                Assert.Equal("Ortsgebunden: 500 x Item", opp.Evidence);
                Assert.Equal(90.5, opp.BuyPrice);
                Assert.Equal(120.25, opp.SellPrice);

                // Historie ist leer und nutzbar: ein Statuswechsel lässt sich persistieren.
                Assert.Empty(await db.TradeStatusChanges.ToListAsync());
                var change = new TradeStatusChange
                {
                    TradingOpportunityId = opp.Id,
                    CharacterId = opp.CharacterId,
                    FromStatus = "active",
                    ToStatus = TradeStatus.Expired.ToDatabaseValue(),
                    Source = TradeStatusSource.System.ToDatabaseValue(),
                    ChangedAt = DateTime.UtcNow,
                    Reason = "Automatisch abgelaufen"
                };
                db.TradeStatusChanges.Add(change);
                await db.SaveChangesAsync();

                var reloaded = await db.TradeStatusChanges.SingleAsync(c => c.TradingOpportunityId == opp.Id);
                Assert.Equal("expired", reloaded.ToStatus);
                Assert.Equal("system", reloaded.Source);
            }
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }
}