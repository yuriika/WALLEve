using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WALLEve.Data;
using WALLEve.Models.Database;
using WALLEve.Services.Market;

namespace WALLEve.Tests;

/// <summary>
/// Tests für Issue #52 (idempotentes Buchungsledger):
/// Duplicate-Imports und verspätete Transaktionen führen nach Replay zum
/// selben Zustand; alte manuelle Werte und Wallet-Historie bleiben erhalten.
/// </summary>
public class CostBasisLedgerServiceTests
{
    private static WalletDbContext CreateDb()
        => TestDb.Create();

    private static WalletTransactionRecord Tx(long id, int typeId, DateTime date, bool isBuy,
        int quantity, double unitPrice)
        => new()
        {
            CharacterId = 90073315,
            TransactionId = id,
            TypeId = typeId,
            Date = date,
            IsBuy = isBuy,
            Quantity = quantity,
            UnitPrice = unitPrice
        };

    [Fact]
    public async Task Import_SameTransactionsTwice_ImportsOnce()
    {
        await using var db = CreateDb();
        var service = new CostBasisLedgerService(db);

        var transactions = new List<WalletTransactionRecord>
        {
            Tx(1, 44992, new DateTime(2026, 9, 1), isBuy: true, quantity: 10, unitPrice: 100),
            Tx(2, 44992, new DateTime(2026, 9, 2), isBuy: true, quantity: 5, unitPrice: 120)
        };

        var first = await service.ImportTransactionsAsync(90073315, transactions);
        var second = await service.ImportTransactionsAsync(90073315, transactions);

        Assert.Equal(2, first);
        Assert.Equal(0, second); // Doppel-Import wird vollständig übersprungen
        Assert.Equal(2, await db.CostBasisLedgerEntries.CountAsync());
    }

    [Fact]
    public async Task Replay_DuplicateImportAndLateTransaction_DeterministicChronologicalState()
    {
        await using var db = CreateDb();
        var service = new CostBasisLedgerService(db);

        // Verspätete Transaktion (älteres Datum als bereits Importierte) + Duplikate
        var messy = new List<WalletTransactionRecord>
        {
            Tx(2, 44992, new DateTime(2026, 9, 2), isBuy: true, quantity: 10, unitPrice: 200),
            Tx(3, 44992, new DateTime(2026, 9, 3), isBuy: false, quantity: 5, unitPrice: 250),
            Tx(0, 44992, new DateTime(2026, 8, 30), isBuy: true, quantity: 4, unitPrice: 90), // verspätet
            Tx(1, 44992, new DateTime(2026, 9, 1), isBuy: true, quantity: 10, unitPrice: 100),
            Tx(1, 44992, new DateTime(2026, 9, 1), isBuy: true, quantity: 10, unitPrice: 100) // Duplikat
        };
        await service.ImportTransactionsAsync(90073315, messy);

        // Der Duplikat-Import wird übersprungen (4 eindeutige Ereignisse).
        Assert.Equal(4, await db.CostBasisLedgerEntries.CountAsync());

        var actual = (await service.ReplayAsync(90073315)).Single();

        // Verspätete Transaktion wird NUR EINMAL in chronologischer Ordnung eingespielt:
        // Kauf 4 @90 → Kauf 10 @100 → Kauf 10 @200 → Verkauf 5 @250 (zum Durchschnitt 140).
        // Menge 4+10+10 = 24 belegt; Verkauf konsumiert 5 belegte Einheiten → 19 on hand.
        Assert.Equal(19, actual.QuantityOnHand);
        Assert.Equal(2660, actual.InventoryValue, 5); // 3360 − 5×140
        Assert.Equal(140, actual.AverageUnitCost!.Value, 5);
        Assert.Equal(550, actual.RealizedProfit, 5); // (250−140)×5
        Assert.False(actual.WasOversold);

        // Vollständig idempotentes Replay: erneutes Replay liefert exakt denselben Zustand.
        var replayAgain = (await service.ReplayAsync(90073315)).Single();
        Assert.Equal(actual.QuantityOnHand, replayAgain.QuantityOnHand);
        Assert.Equal(actual.InventoryValue, replayAgain.InventoryValue, 5);
        Assert.Equal(actual.RealizedProfit, replayAgain.RealizedProfit, 5);
        Assert.False(replayAgain.WasOversold);
    }

    [Fact]
    public async Task UniqueIndex_BlocksDuplicateImportAtDatabaseLevel()
    {
        await using var db = CreateDb();
        db.CostBasisLedgerEntries.Add(new CostBasisLedgerEntry
        {
            CharacterId = 90073315,
            TypeId = 44992,
            SourceTransactionId = 42,
            Date = new DateTime(2026, 9, 1),
            IsBuy = true,
            Quantity = 10,
            UnitPrice = 100,
            ImportedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        db.CostBasisLedgerEntries.Add(new CostBasisLedgerEntry
        {
            CharacterId = 90073315,
            TypeId = 44992,
            SourceTransactionId = 42, // identische Quell-ID → Unique-Index greift
            Date = new DateTime(2026, 9, 1),
            IsBuy = true,
            Quantity = 10,
            UnitPrice = 100,
            ImportedAt = DateTime.UtcNow
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Replay_NoDoubleAcquisitionFee_ValueUsesUnitPriceOnce()
    {
        await using var db = CreateDb();
        var service = new CostBasisLedgerService(db);

        // 10 Einheiten à 100 ISK → Erwerbskosten exakt 1000, keine erneute Gebühr.
        await service.ImportTransactionsAsync(90073315, new List<WalletTransactionRecord>
        {
            Tx(1, 44992, new DateTime(2026, 9, 1), isBuy: true, quantity: 10, unitPrice: 100)
        });

        var position = (await service.ReplayAsync(90073315)).Single();
        Assert.Equal(1000, position.InventoryValue, 5);
        Assert.Equal(100, position.AverageUnitCost!.Value, 5);
    }

    [Fact]
    public async Task GetPosition_LateTransactions_ReplaysInChronologicalOrder()
    {
        await using var db = CreateDb();
        var service = new CostBasisLedgerService(db);

        await service.ImportTransactionsAsync(90073315, new List<WalletTransactionRecord>
        {
            Tx(2, 44992, new DateTime(2026, 9, 2), isBuy: true, quantity: 10, unitPrice: 200),
            Tx(1, 44992, new DateTime(2026, 9, 1), isBuy: true, quantity: 10, unitPrice: 100)
        });

        var position = await service.GetPositionAsync(90073315, 44992);
        Assert.NotNull(position);
        Assert.Equal(20, position!.QuantityOnHand);
        // Chronologische Reihenfolge: erst 100er-Kauf, dann 200er-Kauf → Durchschnitt 150
        Assert.Equal(150, position.AverageUnitCost!.Value, 5);
    }
}