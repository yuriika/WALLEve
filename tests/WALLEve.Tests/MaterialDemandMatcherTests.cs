using WALLEve.Models.Industry;
using WALLEve.Services.Industry;
using WALLEve.Services.Stockpiles;

namespace WALLEve.Tests;

/// <summary>
/// Regressionstests für Issue #62 (Materialbedarf gegen Stockpiles abgleichen):
/// - Gleiches Material in mehreren Orten/Plänen wird NIE als mehrfach verfügbar
///   behauptet: EIN deduplizierter physischer Pool über den gesamten Snapshot,
///   Fehlmenge einmalig je Material (Akzeptanzkriterium 1).
/// - Buy-/Sell-Orders bleiben getrennte Mengen (Inbound/Bound) und werden nie
///   still addiert oder zur Fehlmenge verrechnet (Akzeptanzkriterium 2).
/// - Fehlende/unkomplette Quellen markieren partial; null ist nie Nullbestand.
/// Alles deterministisch ohne DB-/ESI-Abhängigkeiten.
/// </summary>
public class MaterialDemandMatcherTests
{
    private static MaterialDemandRequest Request(string planName, params (int TypeId, long Qty)[] materials)
        => new(planName, materials.Select(m => new MaterialDemandLine(m.TypeId, m.Qty)).ToList());

    private static StockpileCalculator.AssetLine Asset(int typeId, long locationId, int quantity, long itemId = 0)
        => new(itemId, typeId, locationId, quantity);

    private static StockpileCalculator.OrderLine Buy(int typeId, long locationId, int volume)
        => new(typeId, locationId, true, volume);

    private static StockpileCalculator.OrderLine Sell(int typeId, long locationId, int volume)
        => new(typeId, locationId, false, volume);

    // ---- Akzeptanzkriterium 1: kein mehrfaches „verfügbar“ über Pläne/Orte ----

    [Fact]
    public void Match_SameMaterialInMultiplePlans_UsesOnePoolAndOneShortage()
    {
        // Zwei Pläne brauchen dasselbe Material (Tritanium 34): 100 + 60.
        var requests = new List<MaterialDemandRequest>
        {
            Request("Rifter BPO", (34, 100)),
            Request("Incursus BPO", (34, 60))
        };
        // Bestand nur 120 physisch.
        var assets = new List<StockpileCalculator.AssetLine> { Asset(34, 1, 120) };

        var matches = MaterialDemandMatcher.Match(requests, assets, new List<StockpileCalculator.OrderLine>());

        var match = Assert.Single(matches);
        Assert.Equal(34, match.MaterialTypeId);
        Assert.Equal(2, match.Plans.Count);
        Assert.Equal(160, match.PlannedQuantity);
        Assert.Equal(120, match.PhysicalAvailable);     // EIN Pool, nicht 2×120
        Assert.Equal(40, match.Shortage);               // einmalig, nicht je Plan
        Assert.False(match.IsPartial);
    }

    [Fact]
    public void Match_SameMaterialInMultipleLocations_SumsIntoOnePool()
    {
        // Dasselbe Material liegt in zwei Orten (je 100): der Pool ist 200 —
        // die Zeile darf nicht pro Ort 200 „verfügbar“ behaupten.
        var requests = new List<MaterialDemandRequest> { Request("Plan A", (34, 250)) };
        var assets = new List<StockpileCalculator.AssetLine>
        {
            Asset(34, 100, 100),
            Asset(34, 200, 100)
        };

        var matches = MaterialDemandMatcher.Match(requests, assets, new List<StockpileCalculator.OrderLine>());

        var match = Assert.Single(matches);
        Assert.Equal(200, match.PhysicalAvailable);
        Assert.Equal(50, match.Shortage);
    }

    [Fact]
    public void Match_NoShortage_WhenPoolCoversPlannedDemand()
    {
        var requests = new List<MaterialDemandRequest> { Request("Plan A", (34, 100)) };
        var assets = new List<StockpileCalculator.AssetLine> { Asset(34, 1, 300) };

        var matches = MaterialDemandMatcher.Match(requests, assets, new List<StockpileCalculator.OrderLine>());

        var match = Assert.Single(matches);
        Assert.Equal(0, match.Shortage);
    }

    // ---- Akzeptanzkriterium 2: Orders sind getrennte Mengen ----

    [Fact]
    public void Match_ActiveOrders_StaySeparateAndNeverReduceShortage()
    {
        var requests = new List<MaterialDemandRequest> { Request("Plan A", (34, 200)) };
        var assets = new List<StockpileCalculator.AssetLine> { Asset(34, 1, 100) };
        var orders = new List<StockpileCalculator.OrderLine>
        {
            Buy(34, 1, 50),   // eingehend
            Sell(34, 1, 30)   // gebunden (Escrow gehört NICHT zum Bestand)
        };

        var matches = MaterialDemandMatcher.Match(requests, assets, orders);

        var match = Assert.Single(matches);
        Assert.Equal(100, match.PhysicalAvailable); // Sell-Escrow wird nicht abgezogen
        Assert.Equal(50, match.Inbound);
        Assert.Equal(30, match.Bound);
        Assert.Equal(100, match.Shortage);          // Orders ändern die Fehlmenge nicht
        Assert.False(match.IsPartial);
    }

    // ---- Akzeptanzkriterium 2: Partial/Unknown markieren, nie Nullbestand ----

    [Fact]
    public void Match_WithoutPhysicalSnapshot_MarksPartialWithMissingSource()
    {
        var requests = new List<MaterialDemandRequest> { Request("Plan A", (34, 100)) };

        var matches = MaterialDemandMatcher.Match(
            requests,
            new List<StockpileCalculator.AssetLine>(),
            new List<StockpileCalculator.OrderLine>(),
            physicalSourceAvailable: false,
            ordersSourceAvailable: true);

        var match = Assert.Single(matches);
        Assert.True(match.IsPartial);
        Assert.Equal("physical-source-missing", match.PartialReason);
        Assert.Null(match.PhysicalAvailable);   // „unbekannt“, nie 0
        Assert.Null(match.Shortage);
    }

    [Fact]
    public void Match_WithoutOrders_MarksPartialShortageStillDerivedFromPhysical()
    {
        var requests = new List<MaterialDemandRequest> { Request("Plan A", (34, 100)) };
        var assets = new List<StockpileCalculator.AssetLine> { Asset(34, 1, 80) };

        var matches = MaterialDemandMatcher.Match(
            requests,
            assets,
            new List<StockpileCalculator.OrderLine>(),
            physicalSourceAvailable: true,
            ordersSourceAvailable: false);

        var match = Assert.Single(matches);
        Assert.True(match.IsPartial);
        Assert.Equal("orders-source-missing", match.PartialReason);
        Assert.Null(match.Inbound);
        Assert.Null(match.Bound);
        Assert.Equal(80, match.PhysicalAvailable);
        Assert.Equal(20, match.Shortage);       // physische Basis bleibt belastbar
    }

    // ---- Randfälle und Determinismus ----

    [Fact]
    public void Match_NoRequests_ReturnsEmpty()
    {
        var matches = MaterialDemandMatcher.Match(
            new List<MaterialDemandRequest>(),
            new List<StockpileCalculator.AssetLine>(),
            new List<StockpileCalculator.OrderLine>());

        Assert.Empty(matches);
    }

    [Fact]
    public void Match_OrdersForOtherTypes_DoNotAffectMaterial()
    {
        var requests = new List<MaterialDemandRequest> { Request("Plan A", (34, 100)) };
        var assets = new List<StockpileCalculator.AssetLine> { Asset(34, 1, 100) };
        var orders = new List<StockpileCalculator.OrderLine>
        {
            Buy(39, 1, 999),
            Sell(38, 1, 999)
        };

        var matches = MaterialDemandMatcher.Match(requests, assets, orders);

        var match = Assert.Single(matches);
        Assert.Equal(0, match.Inbound);
        Assert.Equal(0, match.Bound);
        Assert.Equal(0, match.Shortage);
    }

    [Fact]
    public void Match_Ordering_ByDemandDescendingThenTypeId()
    {
        var requests = new List<MaterialDemandRequest>
        {
            Request("Plan A", (34, 50), (36, 500), (35, 50))
        };

        var matches = MaterialDemandMatcher.Match(
            requests,
            new List<StockpileCalculator.AssetLine>(),
            new List<StockpileCalculator.OrderLine>());

        // Größter Bedarf zuerst (36), dann TypeId aufsteigend (34 vor 35).
        Assert.Equal(new[] { 36, 34, 35 }, matches.Select(m => m.MaterialTypeId).ToArray());
    }
}