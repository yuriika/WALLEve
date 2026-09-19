using WALLEve.Models.Esi.Markets;
using WALLEve.Models.Holdings;
using WALLEve.Models.Stockpiles;
using WALLEve.Services.Stockpiles;

namespace WALLEve.Tests;

/// <summary>
/// Regressionstests für Issue #43 (Bestandsberechnung getrennt nach physisch,
/// eingehend, gebunden). Der reine <see cref="StockpileCalculator"/> ist
/// deterministisch ohne Live-ESI-/DB-Abhängigkeit; der Service-Test nutzt
/// einen geseedeten Holding-Snapshot zur Owner-Isolation.
/// </summary>
public class StockpileCalculationTests
{
    private static StockpileTarget Target(int typeId = 34, long quantity = 100, long? locationId = null,
        bool archived = false, long id = 0)
        => new()
        {
            Id = id != 0 ? id : 1_000 + typeId,
            OwnerType = OwnerType.Character,
            OwnerId = 90073315,
            TypeId = typeId,
            Quantity = quantity,
            LocationId = locationId,
            IsArchived = archived
        };

    private static IReadOnlyList<StockpileCalculationLine> Calc(
        IReadOnlyList<StockpileTarget> targets,
        List<StockpileCalculator.AssetLine>? assets = null,
        List<StockpileCalculator.OrderLine>? orders = null,
        bool physicalAvailable = true,
        bool ordersAvailable = true)
        => StockpileCalculator.Calculate(
            targets,
            assets ?? new List<StockpileCalculator.AssetLine>(),
            orders ?? new List<StockpileCalculator.OrderLine>(),
            physicalAvailable,
            ordersAvailable);

    // ---- Kriterium 1: Escrow-Sell-Orders werden NICHT nochmals vom physischen Bestand abgezogen ----

    [Fact]
    public void EscrowSellOrder_IsNotSubtractedFromPhysicalAgain()
    {
        var targets = new[] { Target(typeId: 34, quantity: 500) };
        var assets = new List<StockpileCalculator.AssetLine> { new(ItemId: 100, TypeId: 34, LocationId: 60003760, Quantity: 120) };
        var orders = new List<StockpileCalculator.OrderLine> { new(TypeId: 34, LocationId: 60003760, IsBuyOrder: false, VolumeRemain: 30) };

        var line = Calc(targets, assets, orders)[0];

        // Physischer Bestand bleibt unangetastet; die Escrow-Menge ist separat ausgewiesen.
        Assert.Equal(120, line.Physical);
        Assert.Equal(30, line.Bound);
        Assert.Equal(0, line.Inbound);
        // Fehlmenge NUR gegen physisch: 500−120 = 380. Ein (verbotener) Abzug der
        // gebundenen 30 ergäbe 410 — genau diese Doppel-Zählung wird hier ausgeschlossen.
        Assert.Equal(380, line.Shortage);
        Assert.Equal(0, line.Surplus);
        Assert.False(line.IsPartial);
    }

    [Fact]
    public void EscrowSellOrder_BoundShownSeparately_WithoutReducingSurplus()
    {
        var targets = new[] { Target(typeId: 34, quantity: 100) };
        var assets = new List<StockpileCalculator.AssetLine> { new(ItemId: 100, TypeId: 34, LocationId: 60003760, Quantity: 120) };
        var orders = new List<StockpileCalculator.OrderLine> { new(TypeId: 34, LocationId: 60003760, IsBuyOrder: false, VolumeRemain: 30) };

        var line = Calc(targets, assets, orders)[0];

        Assert.Equal(120, line.Physical);
        Assert.Equal(30, line.Bound);
        // 120 physisch − 100 Ziel = 20 Überschuss; die 30 gebundenen werden NICHT subtrahiert
        // (sonst ergäbe sich −10 und der Überschuss verschwände in einer stillen Verrechnung).
        Assert.Equal(20, line.Surplus);
        Assert.Equal(0, line.Shortage);
    }

    // ---- Kriterium 2: Ort/Container und Owner begrenzen die Menge korrekt ----

    [Fact]
    public void LocationScope_LimitsPhysicalAndOrderQuantities()
    {
        var targets = new[] { Target(typeId: 34, quantity: 200, locationId: 60003760) };
        var assets = new List<StockpileCalculator.AssetLine>
        {
            new(ItemId: 100, TypeId: 34, LocationId: 60003760, Quantity: 80),
            new(ItemId: 101, TypeId: 34, LocationId: 60003761, Quantity: 50)
        };
        var orders = new List<StockpileCalculator.OrderLine>
        {
            new(TypeId: 34, LocationId: 60003760, IsBuyOrder: false, VolumeRemain: 30),
            new(TypeId: 34, LocationId: 60003761, IsBuyOrder: false, VolumeRemain: 99),
            new(TypeId: 34, LocationId: 60003760, IsBuyOrder: true, VolumeRemain: 10)
        };

        var line = Calc(targets, assets, orders)[0];

        // Nur der Ort des Ziels zählt — weder Assets noch Orders anderer Orte.
        Assert.Equal(80, line.Physical);
        Assert.Equal(30, line.Bound);
        Assert.Equal(10, line.Inbound);
        Assert.Equal(120, line.Shortage);
        Assert.False(line.IsPartial);
    }

    [Fact]
    public void ContainerScope_CountsContentsOnce_AndNestedScopesMatchChain()
    {
        // Container-Item 1001 (eigener Typ 17392) steht an Station 60003760;
        // 40 Einheiten Typ 34 liegen IM Container, 60 lose an der Station.
        var assets = new List<StockpileCalculator.AssetLine>
        {
            new(ItemId: 1001, TypeId: 17392, LocationId: 60003760, Quantity: 1),
            new(ItemId: 2001, TypeId: 34, LocationId: 1001, Quantity: 40),
            new(ItemId: 2002, TypeId: 34, LocationId: 60003760, Quantity: 60)
        };

        var containerTarget = Target(typeId: 34, quantity: 50, locationId: 1001, id: 1);
        var stationTarget = Target(typeId: 34, quantity: 200, locationId: 60003760, id: 2);
        var lines = Calc(new[] { containerTarget, stationTarget }, assets);

        var containerLine = lines.Single(l => l.TargetId == 1);
        var stationLine = lines.Single(l => l.TargetId == 2);

        // Container-Scope: nur der Inhalt des Containers zählt.
        Assert.Equal(40, containerLine.Physical);
        Assert.Equal(10, containerLine.Shortage);

        // Stations-Scope: Inhalt (Kette 1001 → 60003760) UND lose Einheiten,
        // jede Zeile genau EINMAL (40 + 60 = 100, nicht doppelt gezählt).
        Assert.Equal(100, stationLine.Physical);
        Assert.Equal(100, stationLine.Shortage);
        Assert.False(containerLine.IsPartial);
        Assert.False(stationLine.IsPartial);
    }

    [Fact]
    public void CoveredLocationWithZeroOfType_IsRealZero_NotPartial()
    {
        // Der Ort ist im Snapshot abgedeckt (andere Typen), Typ 34 kommt dort aber nicht vor:
        // 0 ist ein belastbares Ergebnis und darf nicht als fehlende Daten blockiert werden.
        var targets = new[] { Target(typeId: 34, quantity: 100, locationId: 60003760) };
        var assets = new List<StockpileCalculator.AssetLine> { new(ItemId: 500, TypeId: 35, LocationId: 60003760, Quantity: 7) };

        var line = Calc(targets, assets)[0];

        Assert.Equal(0, line.Physical);
        Assert.Equal(100, line.Shortage);
        Assert.False(line.IsPartial);
        Assert.Null(line.PartialReason);
    }

    [Fact]
    public void MissingSnapshot_BlocksDerivation_InsteadOfFakeZero()
    {
        var targets = new[] { Target(typeId: 34, quantity: 100) };

        var line = Calc(targets, physicalAvailable: false, ordersAvailable: false)[0];

        Assert.True(line.IsPartial);
        Assert.Equal("physical-source-missing", line.PartialReason);
        Assert.Null(line.Physical);
        Assert.Null(line.Shortage);
        Assert.Null(line.Surplus);
        Assert.Null(line.Inbound);
        Assert.Null(line.Bound);
    }

    [Fact]
    public void UncoveredScope_BlocksDerivation_InsteadOfFakeZero()
    {
        // Snapshot existiert, aber kein Asset liegt an der Ziel-Location:
        // 0 kann nicht von "nicht synchronisiert" unterschieden werden → blockieren.
        var targets = new[] { Target(typeId: 34, quantity: 100, locationId: 70000001) };
        var assets = new List<StockpileCalculator.AssetLine> { new(ItemId: 100, TypeId: 34, LocationId: 60003760, Quantity: 80) };

        var line = Calc(targets, assets)[0];

        Assert.True(line.IsPartial);
        Assert.Equal("scope-not-covered", line.PartialReason);
        Assert.Null(line.Physical);
        Assert.Null(line.Shortage);
    }

    [Fact]
    public void MissingOrders_MarksPartial_ButKeepsPhysicalDerivation()
    {
        var targets = new[] { Target(typeId: 34, quantity: 300) };
        var assets = new List<StockpileCalculator.AssetLine> { new(ItemId: 100, TypeId: 34, LocationId: 60003760, Quantity: 120) };

        var line = Calc(targets, assets, ordersAvailable: false)[0];

        // Physische Basis ist belastbar → Fehlmenge bleibt ableitbar.
        Assert.Equal(120, line.Physical);
        Assert.Equal(180, line.Shortage);
        // Order-Anteile sind unbekannt und werden NICHT als 0 präsentiert.
        Assert.True(line.IsPartial);
        Assert.Equal("orders-source-missing", line.PartialReason);
        Assert.Null(line.Inbound);
        Assert.Null(line.Bound);
    }

    [Fact]
    public void EmptyOrderList_IsARealFact_NotPartial()
    {
        var targets = new[] { Target(typeId: 34, quantity: 100) };
        var assets = new List<StockpileCalculator.AssetLine> { new(ItemId: 100, TypeId: 34, LocationId: 60003760, Quantity: 120) };

        var line = Calc(targets, assets, orders: new List<StockpileCalculator.OrderLine>(), ordersAvailable: true)[0];

        Assert.Equal(0, line.Inbound);
        Assert.Equal(0, line.Bound);
        Assert.False(line.IsPartial);
    }

    // ---- Kriterium 3: Archivierte Ziele bleiben nachvollziehbar, Quellen nie doppelt ----

    [Fact]
    public void ArchivedTargets_RemainTraceable_AsSeparateLines()
    {
        var targets = new[]
        {
            Target(typeId: 34, quantity: 100, id: 1),
            Target(typeId: 34, quantity: 50, archived: true, id: 2)
        };
        var assets = new List<StockpileCalculator.AssetLine> { new(ItemId: 100, TypeId: 34, LocationId: 60003760, Quantity: 120) };

        var lines = Calc(targets, assets);

        var active = lines.Single(l => l.TargetId == 1);
        var archived = lines.Single(l => l.TargetId == 2);

        // Beide Ziele sind nachvollziehbar; das Archivierte ist explizit markiert.
        Assert.False(active.IsArchived);
        Assert.True(archived.IsArchived);
        Assert.Equal(120, active.Physical);
        Assert.Equal(120, archived.Physical);
    }

    // ---- Service-Orchestrierung: Owner-Isolation über den jüngsten abgeschlossenen Snapshot ----

    [Fact]
    public async Task CalculateAsync_UsesOwnersCompletedSnapshot_AndIsolatesOtherOwners()
    {
        await using var db = TestDb.Create();

        var run = new HoldingSyncRun
        {
            OwnerType = OwnerType.Character,
            OwnerId = 90073315,
            StartedAt = DateTime.UtcNow.AddMinutes(-5),
            Status = "completed",
            CompletedAt = DateTime.UtcNow
        };
        var snapshot = new HoldingSnapshot
        {
            OwnerType = OwnerType.Character,
            OwnerId = 90073315,
            SyncedAt = DateTime.UtcNow,
            Source = "esi"
        };
        snapshot.Items.Add(new HoldingItem
        {
            ItemId = 100, TypeId = 34, Quantity = 120, LocationId = 60003760, LocationFlag = "Hangar"
        });
        run.Snapshots.Add(snapshot);
        db.HoldingSyncRuns.Add(run);

        // Fremder Owner mit eigenem (größeren) Bestand — darf nicht einfließen.
        var foreignRun = new HoldingSyncRun
        {
            OwnerType = OwnerType.Character,
            OwnerId = 999,
            StartedAt = DateTime.UtcNow.AddMinutes(-5),
            Status = "completed",
            CompletedAt = DateTime.UtcNow
        };
        var foreignSnapshot = new HoldingSnapshot
        {
            OwnerType = OwnerType.Character,
            OwnerId = 999,
            SyncedAt = DateTime.UtcNow,
            Source = "esi"
        };
        foreignSnapshot.Items.Add(new HoldingItem
        {
            ItemId = 200, TypeId = 34, Quantity = 9999, LocationId = 60003760, LocationFlag = "Hangar"
        });
        foreignRun.Snapshots.Add(foreignSnapshot);
        db.HoldingSyncRuns.Add(foreignRun);
        await db.SaveChangesAsync();

        var stockpileService = new StockpileService(db);
        await stockpileService.CreateAsync(new StockpileTarget
        {
            OwnerType = OwnerType.Character,
            OwnerId = 90073315,
            TypeId = 34,
            Quantity = 300,
            Note = "Testziel"
        });

        var service = new StockpileCalculationService(db, stockpileService);
        var lines = await service.CalculateAsync(
            OwnerType.Character, 90073315,
            orders: new List<MarketOrder>(),
            ordersAvailable: true);

        var line = Assert.Single(lines);
        Assert.Equal(120, line.Physical);   // eigener Snapshot, fremder Owner ignoriert
        Assert.Equal(0, line.Inbound);
        Assert.Equal(0, line.Bound);
        Assert.Equal(180, line.Shortage);
        Assert.False(line.IsPartial);
    }

    // ---- Issue #183: 64-Bit-Mengen — Summen oberhalb int.MaxValue müssen korrekt bleiben ----

    [Fact]
    public void SumAboveIntMaxValue_RemainsCorrect_WithLongQuantities()
    {
        // Zwei Stacks mit je 1.500.000.001 Einheiten → Summe 3.000.000.002 > int.MaxValue (2.147.483.647).
        // Bei int würde die Summe negativ überlaufen (stack overflow im wörtlichen Sinne).
        long bigStack = 1_500_000_001L;
        long expectedTotal = 3_000_000_002L;

        var targets = new[] { Target(typeId: 34, quantity: (int)bigStack * 2) };
        var assets = new List<StockpileCalculator.AssetLine>
        {
            new(ItemId: 100, TypeId: 34, LocationId: 60003760, Quantity: bigStack),
            new(ItemId: 101, TypeId: 34, LocationId: 60003760, Quantity: bigStack)
        };

        var lines = Calc(targets, assets);

        var line = Assert.Single(lines);
        Assert.Equal(expectedTotal, line.Physical);
        Assert.False(line.IsPartial);
    }

    [Fact]
    public void ShortageAboveIntMaxValue_IsComputedInLong()
    {
        // Ziel 5.000.000.000, physisch 1.000.000.000 → Shortage 4.000.000.000 > int.MaxValue
        const long targetQuantity = 5_000_000_000L;
        const long physical = 1_000_000_000L;
        const long expectedShortage = 4_000_000_000L;

        var targets = new[] { Target(typeId: 34, quantity: targetQuantity) };
        var assets = new List<StockpileCalculator.AssetLine>
        {
            new(ItemId: 100, TypeId: 34, LocationId: 60003760, Quantity: physical)
        };

        var lines = Calc(targets, assets);

        var line = Assert.Single(lines);
        Assert.Equal(expectedShortage, line.Shortage);
        Assert.Equal(targetQuantity, line.TargetQuantity);
    }

    [Fact]
    public void SurplusAboveIntMaxValue_IsComputedInLong()
    {
        // Physisch 5.000.000.000, Ziel 1.000.000.000 → Surplus 4.000.000.000 > int.MaxValue
        const long targetQuantity = 1_000_000_000L;
        const long physical = 5_000_000_000L;
        const long expectedSurplus = 4_000_000_000L;

        var targets = new[] { Target(typeId: 34, quantity: targetQuantity) };
        var assets = new List<StockpileCalculator.AssetLine>
        {
            new(ItemId: 100, TypeId: 34, LocationId: 60003760, Quantity: physical)
        };

        var lines = Calc(targets, assets);

        var line = Assert.Single(lines);
        Assert.Equal(expectedSurplus, line.Surplus);
    }

    [Fact]
    public void InboundAboveIntMaxValue_IsComputedInLong()
    {
        // Buy-Orders mit Volumen > int.MaxValue → Inbound muss korrekt sein
        const long volume1 = 1_500_000_001L;
        const long volume2 = 1_500_000_001L;
        const long expectedInbound = 3_000_000_002L;

        var targets = new[] { Target(typeId: 34, quantity: 100) };
        var orders = new List<StockpileCalculator.OrderLine>
        {
            new(TypeId: 34, LocationId: 60003760, IsBuyOrder: true, VolumeRemain: volume1),
            new(TypeId: 34, LocationId: 60003760, IsBuyOrder: true, VolumeRemain: volume2)
        };

        var lines = Calc(targets, orders: orders);

        var line = Assert.Single(lines);
        Assert.Equal(expectedInbound, line.Inbound);
    }

    [Fact]
    public void BoundAboveIntMaxValue_IsComputedInLong()
    {
        // Sell-Orders mit Volumen > int.MaxValue → Bound muss korrekt sein
        const long volume1 = 2_000_000_000L;
        const long volume2 = 2_000_000_000L;
        const long expectedBound = 4_000_000_000L;

        var targets = new[] { Target(typeId: 34, quantity: 100) };
        var orders = new List<StockpileCalculator.OrderLine>
        {
            new(TypeId: 34, LocationId: 60003760, IsBuyOrder: false, VolumeRemain: volume1),
            new(TypeId: 34, LocationId: 60003760, IsBuyOrder: false, VolumeRemain: volume2)
        };

        var lines = Calc(targets, orders: orders);

        var line = Assert.Single(lines);
        Assert.Equal(expectedBound, line.Bound);
    }
}