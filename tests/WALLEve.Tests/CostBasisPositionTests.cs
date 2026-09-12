using Microsoft.EntityFrameworkCore;
using WALLEve.Data;
using WALLEve.Models.Database;
using WALLEve.Models.Holdings;
using WALLEve.Services.Market;
using WALLEve.Services.Market.Interfaces;

namespace WALLEve.Tests;

/// <summary>
/// Regressionstests für die Cost-Basis-Zustandsmaschine (Issue #42):
/// gleitender Durchschnitt, Verkäufe zum laufenden Durchschnitt, kompletter
/// Abverkauf/Wiederkauf, Rundung, fehlende Basis, Transfers ohne Gewinn,
/// getrennte unbekannte Eröffnungsbestände und Provenienz ohne erfundene
/// Zuordnung — plus der Transaktions-Replay-Service.
/// </summary>
public class CostBasisPositionTests
{
    private const int CharacterId = 90073315;

    // ------------------------------------------------------------------
    // Sequenzen aus Käufen und Verkäufen (AK: Sequenzen mehrerer Käufe/Verkäufe)
    // ------------------------------------------------------------------

    [Fact]
    public void MultipleBuys_BuildWeightedAverage()
    {
        var pos = new CostBasisPosition(CharacterId, 1);

        pos.ApplyBuy(100, 10.0);
        pos.ApplyBuy(50, 20.0);

        // (100×10 + 50×20) / 150 = 13,3333…
        Assert.Equal(150, pos.QuantityOnHand);
        Assert.Equal(2000.0, pos.InventoryValue, 6);
        Assert.Equal(13.3333, pos.AverageUnitCost!.Value, 4);
        Assert.Equal(CostBasisQuality.Full, pos.Quality);
        Assert.Equal(CostBasisSource.Transaction, pos.Source);
        Assert.Equal(0, pos.UnknownQuantity);
    }

    [Fact]
    public void Sell_BooksOutAtRunningAverage()
    {
        var pos = new CostBasisPosition(CharacterId, 1);
        pos.ApplyBuy(100, 10.0);
        pos.ApplyBuy(50, 20.0);

        pos.ApplySell(30, 15.0);

        // Ausbuchung zum Durchschnitt: 120 übrig à 13,3333… → Wert 1600
        Assert.Equal(120, pos.QuantityOnHand);
        Assert.Equal(1600.0, pos.InventoryValue, 6);
        Assert.Equal(13.3333, pos.AverageUnitCost!.Value, 4);
        Assert.Equal(50.0, pos.RealizedProfit, 6); // (15 − 13,3333…) × 30
    }

    [Fact]
    public void CompleteSellout_LeavesEmptyPosition_WithRealizedProfit()
    {
        var pos = new CostBasisPosition(CharacterId, 1);
        pos.ApplyBuy(100, 10.0);
        pos.ApplyBuy(50, 20.0);

        pos.ApplySell(150, 15.0);

        Assert.Equal(0, pos.QuantityOnHand);
        Assert.Equal(0.0, pos.InventoryValue, 6);
        Assert.Null(pos.AverageUnitCost);
        Assert.Equal(250.0, pos.RealizedProfit, 6); // 150 × (15 − 13,3333…)
        Assert.Equal(CostBasisQuality.Full, pos.Quality);
        Assert.False(pos.WasOversold);
    }

    [Fact]
    public void Rebuy_AfterCompleteSellout_StartsWithFreshBasis()
    {
        var pos = new CostBasisPosition(CharacterId, 1);
        pos.ApplyBuy(100, 10.0);
        pos.ApplySell(100, 15.0);

        pos.ApplyBuy(10, 30.0);

        // Der alte Durchschnitt (10) darf NICHT in die neue Position hineinwirken.
        Assert.Equal(10, pos.QuantityOnHand);
        Assert.Equal(300.0, pos.InventoryValue, 6);
        Assert.Equal(30.0, pos.AverageUnitCost!.Value, 10);
    }

    // ------------------------------------------------------------------
    // Rundung (AK: Rundung)
    // ------------------------------------------------------------------

    [Fact]
    public void Rounding_AverageStaysConsistent_AndSelloutHasNoResidue()
    {
        var pos = new CostBasisPosition(CharacterId, 1);
        pos.ApplyBuy(1, 1.0);
        pos.ApplyBuy(2, 2.0); // Wert 5, Menge 3 → Durchschnitt 5/3 ≈ 1,6667

        pos.ApplySell(2, 3.0);

        Assert.Equal(1, pos.QuantityOnHand);
        Assert.Equal(5.0 / 3.0, pos.InventoryValue, 10);
        // Nach jedem Schritt gilt: Durchschnitt = Wert / Menge (kein Drift durch Rundung)
        Assert.Equal(pos.AverageUnitCost!.Value, pos.InventoryValue / pos.QuantityOnHand, 10);

        pos.ApplySell(1, 3.0);

        Assert.Equal(0, pos.QuantityOnHand);
        Assert.True(Math.Abs(pos.InventoryValue) < 1e-9, "Nach komplettem Abverkauf darf kein Rundungsrest bleiben.");
        Assert.Equal(4.0, pos.RealizedProfit, 6);
    }

    // ------------------------------------------------------------------
    // Fehlende Basis (AK: fehlende Basis)
    // ------------------------------------------------------------------

    [Fact]
    public void Sell_WithoutAnyBasis_InventNoProfit()
    {
        var pos = new CostBasisPosition(CharacterId, 1);

        pos.ApplySell(10, 100.0);

        Assert.Equal(0, pos.QuantityOnHand);
        Assert.Equal(0.0, pos.InventoryValue, 6);
        Assert.Equal(0.0, pos.RealizedProfit, 6);
        Assert.Null(pos.AverageUnitCost);
        Assert.True(pos.WasOversold);
    }

    [Fact]
    public void Oversell_ClampsAtZero_NeverNegativeQuantity()
    {
        var pos = new CostBasisPosition(CharacterId, 1);
        pos.ApplyOpeningBalance(5);
        pos.ApplyBuy(3, 10.0);

        pos.ApplySell(10, 15.0);

        // 3 belegte à 10 ausgebucht (Gewinn 15), 5 unbekannt ohne P&L, 2 Überschuss abgeschnitten
        Assert.Equal(0, pos.QuantityOnHand);
        Assert.Equal(0.0, pos.InventoryValue, 6);
        Assert.Equal(15.0, pos.RealizedProfit, 6);
        Assert.True(pos.WasOversold);
    }

    // ------------------------------------------------------------------
    // Transfers: kein Gewinn, kein kostenloser Zugang (AK: Transfer)
    // ------------------------------------------------------------------

    [Fact]
    public void TransferIn_IsNot_FreeAcquisition()
    {
        var pos = new CostBasisPosition(CharacterId, 1);

        pos.ApplyTransfer(25, isInbound: true);

        Assert.Equal(25, pos.UnknownQuantity);
        Assert.Equal(0, pos.KnownQuantity);
        Assert.Equal(0.0, pos.InventoryValue, 6);
        Assert.Null(pos.AverageUnitCost);
        Assert.Equal(0.0, pos.RealizedProfit, 6);
        Assert.Equal(CostBasisQuality.Missing, pos.Quality);
        Assert.Equal(CostBasisSource.None, pos.Source);
    }

    [Fact]
    public void TransferOut_CreatesNoProfit_ConsumesUnknownFirst()
    {
        var pos = new CostBasisPosition(CharacterId, 1);
        pos.ApplyOpeningBalance(100);
        pos.ApplyBuy(50, 10.0);

        pos.ApplyTransfer(120, isInbound: false);

        // 100 unbekannt ohne P&L, 20 belegte à 10 ausgebucht — KEIN realisierter Gewinn
        Assert.Equal(30, pos.QuantityOnHand);
        Assert.Equal(0, pos.UnknownQuantity);
        Assert.Equal(300.0, pos.InventoryValue, 6);
        Assert.Equal(0.0, pos.RealizedProfit, 6);
        Assert.False(pos.WasOversold);
        Assert.Equal(CostBasisQuality.Full, pos.Quality);
    }

    [Fact]
    public void TransferOut_BeyondHoldings_ClampsWithoutProfit()
    {
        var pos = new CostBasisPosition(CharacterId, 1);
        pos.ApplyOpeningBalance(10);
        pos.ApplyBuy(5, 10.0);

        pos.ApplyTransfer(40, isInbound: false);

        Assert.Equal(0, pos.QuantityOnHand);
        Assert.Equal(0.0, pos.InventoryValue, 6);
        Assert.Equal(0.0, pos.RealizedProfit, 6);
        Assert.True(pos.WasOversold);
    }

    // ------------------------------------------------------------------
    // Unbekannte Eröffnungsbestände getrennt (AK: keine rückwirkende Schätzung)
    // ------------------------------------------------------------------

    [Fact]
    public void OpeningBalance_StaysSeparate_NoRetroactiveEstimateFromBuys()
    {
        var pos = new CostBasisPosition(CharacterId, 1);
        pos.ApplyOpeningBalance(500);

        pos.ApplyBuy(100, 10.0);

        Assert.Equal(600, pos.QuantityOnHand);
        Assert.Equal(100, pos.KnownQuantity);
        Assert.Equal(500, pos.UnknownQuantity);
        Assert.Equal(1000.0, pos.InventoryValue, 6);
        // Durchschnitt NUR der belegten Einheiten — die 500 unbekannten werden nicht
        // rückwirkend mit 10 ISK „geschätzt" (kein erfundener Wert 6000, kein erfundener Schnitt 1,67).
        Assert.Equal(10.0, pos.AverageUnitCost!.Value, 10);
        Assert.Equal(CostBasisQuality.Partial, pos.Quality);
    }

    [Fact]
    public void Sell_ConsumesKnownUnitsAtAverage_BeforeUnknownUnits()
    {
        var pos = new CostBasisPosition(CharacterId, 1);
        pos.ApplyOpeningBalance(500);
        pos.ApplyBuy(100, 10.0);

        pos.ApplySell(30, 15.0);

        Assert.Equal(70, pos.KnownQuantity);
        Assert.Equal(500, pos.UnknownQuantity);
        Assert.Equal(700.0, pos.InventoryValue, 6);
        Assert.Equal(150.0, pos.RealizedProfit, 6);
        Assert.Equal(CostBasisQuality.Partial, pos.Quality);
    }

    [Fact]
    public void Sell_ExhaustingKnownThenUnknown_EndsClean()
    {
        var pos = new CostBasisPosition(CharacterId, 1);
        pos.ApplyOpeningBalance(500);
        pos.ApplyBuy(100, 10.0);

        pos.ApplySell(600, 15.0);

        Assert.Equal(0, pos.QuantityOnHand);
        Assert.Equal(0, pos.UnknownQuantity);
        Assert.Equal(500.0, pos.RealizedProfit, 6); // nur 100 belegte tragen Gewinn
        Assert.False(pos.WasOversold);
    }

    // ------------------------------------------------------------------
    // Manuelle Werte und Provenienz (AK: Manual als eigene Provenienz, Manual gewinnt)
    // ------------------------------------------------------------------

    [Fact]
    public void ManualEntry_ConvertsUnknownToKnown_AtDeclaredPrice()
    {
        var pos = new CostBasisPosition(CharacterId, 1);
        pos.ApplyOpeningBalance(100);

        pos.ApplyManualEntry(50.0);

        Assert.Equal(100, pos.KnownQuantity);
        Assert.Equal(0, pos.UnknownQuantity);
        Assert.Equal(5000.0, pos.InventoryValue, 6);
        Assert.Equal(50.0, pos.AverageUnitCost!.Value, 10);
        Assert.Equal(CostBasisSource.Manual, pos.Source);
        Assert.Equal(CostBasisQuality.Full, pos.Quality);
    }

    [Fact]
    public void ManualSource_WinsOverTransactionSource()
    {
        var pos = new CostBasisPosition(CharacterId, 1);
        pos.ApplyOpeningBalance(100);
        pos.ApplyBuy(10, 5.0);
        Assert.Equal(CostBasisSource.Transaction, pos.Source);

        pos.ApplyManualEntry(7.0);

        Assert.Equal(CostBasisSource.Manual, pos.Source);
        Assert.Equal(110, pos.KnownQuantity);
        Assert.Equal(750.0, pos.InventoryValue, 6); // 10×5 + 100×7
    }

    [Fact]
    public void ManualEntry_WithoutUnknownQuantity_Throws()
    {
        var pos = new CostBasisPosition(CharacterId, 1);
        pos.ApplyBuy(10, 5.0);

        Assert.Throws<InvalidOperationException>(() => pos.ApplyManualEntry(7.0));
    }

    [Fact]
    public void Provenance_NotInvented_OpeningBalanceNeverCreatesBasis()
    {
        // Mining/Loot/Production/Contract gelangen als Eröffnungsbestand in die Maschine —
        // es darf KEINE erfundene Zuordnung (Basis, Wert oder Gewinn) entstehen.
        var pos = new CostBasisPosition(CharacterId, 1);
        pos.ApplyOpeningBalance(42);

        Assert.Equal(42, pos.UnknownQuantity);
        Assert.Equal(42, pos.QuantityOnHand);
        Assert.Equal(0.0, pos.InventoryValue, 6);
        Assert.Null(pos.AverageUnitCost);
        Assert.Equal(0.0, pos.RealizedProfit, 6);
        Assert.Equal(CostBasisSource.None, pos.Source);
        Assert.Equal(CostBasisQuality.Missing, pos.Quality);
    }

    // ------------------------------------------------------------------
    // Eingabevalidierung
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void InvalidQuantities_Throw(long quantity)
    {
        var pos = new CostBasisPosition(CharacterId, 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => pos.ApplyBuy(quantity, 1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => pos.ApplySell(quantity, 1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => pos.ApplyOpeningBalance(quantity));
        Assert.Throws<ArgumentOutOfRangeException>(() => pos.ApplyTransfer(quantity, true));
    }

    [Fact]
    public void NegativePrices_Throw()
    {
        var pos = new CostBasisPosition(CharacterId, 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => pos.ApplyBuy(1, -1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => pos.ApplySell(1, -1.0));
    }

    // ------------------------------------------------------------------
    // Replay-Service: chronologische Wiedergabe der Transaktionsspiegelung
    // ------------------------------------------------------------------

    private static WalletDbContext CreateDbWithTransactions(params WalletTransactionRecord[] records)
    {
        var db = TestDb.Create();
        db.WalletTransactionRecords.AddRange(records);
        db.SaveChanges();
        return db;
    }

    [Fact]
    public async Task Replay_ReplaysChronologically_AndGroupsByType()
    {
        var db = CreateDbWithTransactions(
            new WalletTransactionRecord { CharacterId = CharacterId, TransactionId = 1, TypeId = 1, Date = new DateTime(2026, 1, 1), IsBuy = true, Quantity = 100, UnitPrice = 10.0 },
            new WalletTransactionRecord { CharacterId = CharacterId, TransactionId = 2, TypeId = 2, Date = new DateTime(2026, 1, 2), IsBuy = true, Quantity = 50, UnitPrice = 20.0 },
            new WalletTransactionRecord { CharacterId = CharacterId, TransactionId = 3, TypeId = 1, Date = new DateTime(2026, 1, 3), IsBuy = false, Quantity = 30, UnitPrice = 15.0 },
            new WalletTransactionRecord { CharacterId = CharacterId, TransactionId = 4, TypeId = 2, Date = new DateTime(2026, 1, 4), IsBuy = false, Quantity = 20, UnitPrice = 30.0 });

        var service = new CostBasisLedgerService(db);
        var positions = await service.ReplayAsync(CharacterId);

        Assert.Equal(2, positions.Count);
        Assert.Equal(new[] { 1, 2 }, positions.Select(p => p.TypeId).ToArray());

        var type1 = positions[0];
        // 100 @ 10 gekauft, 30 @ 15 verkauft → 70 übrig à 10
        Assert.Equal(70, type1.QuantityOnHand);
        Assert.Equal(700.0, type1.InventoryValue, 6);
        Assert.Equal(10.0, type1.AverageUnitCost!.Value, 10);
        Assert.Equal(150.0, type1.RealizedProfit, 6);

        var type2 = positions[1];
        // 50 @ 20 gekauft, 20 @ 30 verkauft → 30 übrig à 20
        Assert.Equal(30, type2.QuantityOnHand);
        Assert.Equal(600.0, type2.InventoryValue, 6);
        Assert.Equal(200.0, type2.RealizedProfit, 6);
    }

    [Fact]
    public async Task Replay_EqualDates_AreOrderedByTransactionId()
    {
        var db = CreateDbWithTransactions(
            new WalletTransactionRecord { CharacterId = CharacterId, TransactionId = 2, TypeId = 1, Date = new DateTime(2026, 1, 1), IsBuy = true, Quantity = 10, UnitPrice = 5.0 },
            new WalletTransactionRecord { CharacterId = CharacterId, TransactionId = 1, TypeId = 1, Date = new DateTime(2026, 1, 1), IsBuy = true, Quantity = 10, UnitPrice = 10.0 });

        var service = new CostBasisLedgerService(db);
        var position = await service.GetPositionAsync(CharacterId, 1);

        Assert.NotNull(position);
        // Determinismus: erst T1 (10@10), dann T2 (10@5) → (100+50)/20 = 7,5
        Assert.Equal(7.5, position!.AverageUnitCost!.Value, 10);
    }

    [Fact]
    public async Task Replay_BuySellBuySequence_ReflectsCurrentState()
    {
        var db = CreateDbWithTransactions(
            new WalletTransactionRecord { CharacterId = CharacterId, TransactionId = 1, TypeId = 1, Date = new DateTime(2026, 1, 1), IsBuy = true, Quantity = 10, UnitPrice = 10.0 },
            new WalletTransactionRecord { CharacterId = CharacterId, TransactionId = 2, TypeId = 1, Date = new DateTime(2026, 1, 2), IsBuy = false, Quantity = 4, UnitPrice = 12.0 },
            new WalletTransactionRecord { CharacterId = CharacterId, TransactionId = 3, TypeId = 1, Date = new DateTime(2026, 1, 3), IsBuy = true, Quantity = 5, UnitPrice = 8.0 });

        var service = new CostBasisLedgerService(db);
        var position = await service.GetPositionAsync(CharacterId, 1);

        Assert.NotNull(position);
        // 10×10 − 4×10 + 5×8 = 100 − 40 + 40 = 100; Menge 11 → Schnitt 100/11
        Assert.Equal(11, position!.QuantityOnHand);
        Assert.Equal(100.0, position.InventoryValue, 6);
        Assert.Equal(100.0 / 11.0, position.AverageUnitCost!.Value, 10);
        Assert.Equal(8.0, position.RealizedProfit, 6); // (12 − 10) × 4
    }

    [Fact]
    public async Task GetPosition_WithoutTransactions_ReturnsNull()
    {
        var db = CreateDbWithTransactions(
            new WalletTransactionRecord { CharacterId = CharacterId, TransactionId = 1, TypeId = 1, Date = new DateTime(2026, 1, 1), IsBuy = true, Quantity = 10, UnitPrice = 5.0 });

        var service = new CostBasisLedgerService(db);
        var position = await service.GetPositionAsync(CharacterId, 99);

        Assert.Null(position);
    }
}