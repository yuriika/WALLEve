using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using WALLEve.Data;
using WALLEve.Models.Database;

namespace WALLEve.Tests;

/// <summary>
/// Migrationstests für Issue #52 (idempotentes Buchungsledger):
/// Die additive Migration AddCostBasisLedger legt die CostBasisLedgerEntries-Tabelle
/// an, ohne bestehende Wallet-Historie, Cost-Basis-Werte oder manuelle Einträge
/// anzutasten; unvollständige Herkunft (Source=None) bleibt sichtbar.
/// </summary>
public class CostBasisLedgerMigrationTests
{
    /// <summary>Letzte Migration VOR der Ledger-Migration (Portfolio-Snapshots #51).</summary>
    private const string PreviousMigration = "20260912211316_AddPortfolioSnapshots";

    private static WalletDbContext CreateDb(string connectionString)
        => new(new DbContextOptionsBuilder<WalletDbContext>()
            .UseSqlite(connectionString)
            .Options);

    [Fact]
    public async Task Migrate_PreservesWalletHistoryAndCostBasisValues_AndCreatesEmptyLedger()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"walleve-ledger-{Guid.NewGuid():N}.db");
        try
        {
            var txDate = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Unspecified);

            // Phase 1: Schema bis VOR der Ledger-Migration aufbauen und
            // Wallet-Transaktionen plus Cost-Basis-Einträge (inkl. manuellem Wert) anlegen.
            await using (var db = CreateDb($"Data Source={dbPath}"))
            {
                await db.Database.GetService<IMigrator>().MigrateAsync(PreviousMigration);

                db.WalletTransactionRecords.AddRange(
                    new WalletTransactionRecord
                    {
                        CharacterId = 90073315,
                        TransactionId = 1001,
                        TypeId = 44992,
                        Date = txDate,
                        IsBuy = true,
                        IsPersonal = true,
                        JournalRefId = 555,
                        LocationId = 60003760,
                        Quantity = 10,
                        UnitPrice = 100
                    },
                    new WalletTransactionRecord
                    {
                        CharacterId = 90073315,
                        TransactionId = 1002,
                        TypeId = 44992,
                        Date = txDate.AddDays(1),
                        IsBuy = false,
                        IsPersonal = true,
                        JournalRefId = 556,
                        LocationId = 60003760,
                        Quantity = 2,
                        UnitPrice = 150
                    });

                db.CostBasisEntries.AddRange(
                    new CostBasisEntry
                    {
                        CharacterId = 90073315,
                        TypeId = 44992,
                        Value = 98.5,
                        Source = CostBasisSource.Manual, // alter manueller Wert
                        PurchaseDate = txDate,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new CostBasisEntry
                    {
                        CharacterId = 90073315,
                        TypeId = 45010,
                        Value = null,
                        Source = CostBasisSource.None, // unvollständige Herkunft bleibt sichtbar
                        UpdatedAt = DateTime.UtcNow
                    });
                await db.SaveChangesAsync();
            }

            // Phase 2: Ledger-Migration anwenden und ALLE Altwerte zurücklesen.
            await using (var db = CreateDb($"Data Source={dbPath}"))
            {
                await db.Database.MigrateAsync();

                // Wallet-Historie unangetastet
                Assert.Equal(2, await db.WalletTransactionRecords.CountAsync());

                // Manuelle Cost-Basis-Werte erhalten, unvollständige Herkunft bleibt sichtbar
                var manual = await db.CostBasisEntries
                    .SingleAsync(e => e.CharacterId == 90073315 && e.TypeId == 44992);
                Assert.Equal(98.5, manual.Value!.Value, 5);
                Assert.Equal(CostBasisSource.Manual, manual.Source);

                var unknown = await db.CostBasisEntries
                    .SingleAsync(e => e.CharacterId == 90073315 && e.TypeId == 45010);
                Assert.Equal(CostBasisSource.None, unknown.Source);

                // Ledger-Tabelle ist additiv erzeugt und mit der vorhandenen
                // Wallet-Historie backgefüllt (2 Transaktionen → 2 Ledger-Ereignisse).
                var ledgerEntries = await db.CostBasisLedgerEntries
                    .OrderBy(e => e.SourceTransactionId)
                    .ToListAsync();
                Assert.Equal(2, ledgerEntries.Count);
                Assert.Equal(1001, ledgerEntries[0].SourceTransactionId);
                Assert.Equal(44992, ledgerEntries[0].TypeId);
                Assert.Equal(100, ledgerEntries[0].UnitPrice, 5);
                Assert.Equal(1002, ledgerEntries[1].SourceTransactionId);
                Assert.False(ledgerEntries[1].IsBuy);
            }
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task UniqueIndex_BlocksDuplicateLedgerEntryForSameSourceTransaction()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"walleve-ledger-uniq-{Guid.NewGuid():N}.db");
        try
        {
            await using (var db = CreateDb($"Data Source={dbPath}"))
            {
                await db.Database.MigrateAsync();

                db.CostBasisLedgerEntries.Add(new CostBasisLedgerEntry
                {
                    CharacterId = 90073315,
                    TypeId = 44992,
                    SourceTransactionId = 1001,
                    Date = new DateTime(2026, 9, 1),
                    IsBuy = true,
                    Quantity = 10,
                    UnitPrice = 100,
                    ImportedAt = DateTime.UtcNow
                });
                await db.SaveChangesAsync();

                // Zweiter Eintrag für dieselbe Quell-Transaktion/Type muss verworfen werden.
                db.CostBasisLedgerEntries.Add(new CostBasisLedgerEntry
                {
                    CharacterId = 90073315,
                    TypeId = 44992,
                    SourceTransactionId = 1001,
                    Date = new DateTime(2026, 9, 1),
                    IsBuy = true,
                    Quantity = 10,
                    UnitPrice = 100,
                    ImportedAt = DateTime.UtcNow
                });
                await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            }
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }
}