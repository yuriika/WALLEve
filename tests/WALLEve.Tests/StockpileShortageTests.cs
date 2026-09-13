using WALLEve.Models.Stockpiles;
using WALLEve.Services.Stockpiles;

namespace WALLEve.Tests;

/// <summary>
/// Regressionstests für Issue #59 (Fehlmengenliste mit Marktvergleich):
/// - Gruppierung nach Ort, Shortage-only, Archiv-Filter, Determinismus.
/// - Quellen bleiben getrennt: Physisch/Eingehend/Gebunden werden nie still
///   addiert; die Fehlmenge bleibt an die physische Basis gebunden.
/// - Unbekannte Bewertung ist sichtbar: fehlender Vergleichsmarkt, fehlender
///   Snapshot und nicht auflösbare Orte liefern NIE einen 0-Preis/0-Sprung.
/// Alles deterministisch ohne DB-/ESI-Abhängigkeiten.
/// </summary>
public class StockpileShortageTests
{
    private static StockpileCalculationLine Line(
        int typeId,
        int? shortage,
        long? locationId = null,
        int? physical = 100,
        int? inbound = null,
        int? bound = null,
        bool archived = false,
        bool partial = false,
        string? partialReason = null)
        => new()
        {
            TargetId = typeId * 10,
            TypeId = typeId,
            LocationId = locationId,
            TargetQuantity = 100,
            Physical = physical,
            Inbound = inbound,
            Bound = bound,
            Shortage = shortage,
            Surplus = shortage is 0 ? 0 : null,
            IsArchived = archived,
            IsPartial = partial,
            PartialReason = partialReason
        };

    // ---- Shortage-only + Gruppierung nach Ort ----

    [Fact]
    public void GroupByLocation_FiltersShortageOnly_AndGroupsByLocation()
    {
        var lines = new List<StockpileCalculationLine>
        {
            Line(34, 0),                                   // kein Shortage → raus
            Line(35, null, partial: true),                 // blockiert → raus (kein 0-Shortage)
            Line(36, 25, 60003760),                        // Gruppe Ort A
            Line(37, 10, 60003760),                        // Gruppe Ort A
            Line(38, 5)                                    // Gesamtbestand
        };

        var groups = StockpileShortageGrouper.GroupByLocation(lines);

        Assert.Equal(2, groups.Count);
        // Gesamtbestand (null) deterministisch zuerst, dann scoped Orte aufsteigend.
        Assert.Null(groups[0].LocationId);
        Assert.Equal(new[] { 38 }, groups[0].Lines.Select(l => l.TypeId).ToArray());
        Assert.Equal(60003760, groups[1].LocationId);
        Assert.Equal(new[] { 36, 37 }, groups[1].Lines.Select(l => l.TypeId).ToArray());
    }

    [Fact]
    public void GroupByLocation_OrdersLocationsDeterministically()
    {
        var lines = new List<StockpileCalculationLine>
        {
            Line(36, 1, 60009999),
            Line(37, 1, 60001111),
            Line(38, 1, 60005555)
        };

        var groups = StockpileShortageGrouper.GroupByLocation(lines);

        Assert.Equal(new long?[] { 60001111, 60005555, 60009999 }, groups.Select(g => g.LocationId).ToArray());
    }

    [Fact]
    public void GroupByLocation_ExcludesArchivedUnlessRequested()
    {
        var lines = new List<StockpileCalculationLine>
        {
            Line(36, 20, 60003760, archived: true),
            Line(37, 5, 60003760)
        };

        var defaultGroups = StockpileShortageGrouper.GroupByLocation(lines);
        Assert.Single(defaultGroups);
        Assert.Equal(new[] { 37 }, defaultGroups[0].Lines.Select(l => l.TypeId).ToArray());

        var withArchived = StockpileShortageGrouper.GroupByLocation(lines, includeArchived: true);
        Assert.Equal(new[] { 36, 37 }, withArchived[0].Lines.Select(l => l.TypeId).ToArray());
    }

    [Fact]
    public void GroupByLocation_KeepsSourcesSeparate_AndShortageBoundToPhysical()
    {
        // Eingehend/Gebunden dürfen die Fehlmenge nicht verrechnen:
        // Shortage bleibt max(0, Ziel − physisch).
        var lines = new List<StockpileCalculationLine>
        {
            Line(34, 50, 60003760, physical: 50, inbound: 900, bound: 700)
        };

        var group = StockpileShortageGrouper.GroupByLocation(lines).Single();

        Assert.Equal(50, group.Lines[0].Shortage);
        Assert.Equal(50, group.Lines[0].Physical);
        Assert.Equal(900, group.Lines[0].Inbound);
        Assert.Equal(700, group.Lines[0].Bound);
        Assert.Equal(50, group.TotalShortage);
    }

    [Fact]
    public void GroupByLocation_EmptyResult_WhenNoShortageExists()
    {
        var lines = new List<StockpileCalculationLine> { Line(34, 0), Line(35, null) };

        Assert.Empty(StockpileShortageGrouper.GroupByLocation(lines));
    }

    [Fact]
    public void GroupByLocation_ReportsPartialLines()
    {
        var lines = new List<StockpileCalculationLine>
        {
            Line(34, 5, 60003760, partial: true, partialReason: "orders-source-missing"),
            Line(35, 3, 60003760)
        };

        var group = StockpileShortageGrouper.GroupByLocation(lines).Single();

        Assert.True(group.HasPartialLines);
    }

    // ---- Bewertung: unbekannt statt 0 ----

    [Fact]
    public void Quote_WithoutSnapshot_IsUnknown_NotZero()
    {
        var quote = new StockpileMarketQuote { TypeId = 34 };

        Assert.False(quote.HasQuote);
        Assert.Null(quote.BestSellPrice);
        Assert.Null(quote.BestBuyPrice);
        Assert.Null(quote.QuoteTimestamp);
    }

    [Fact]
    public void Quote_WithoutPricesEvenWithTimestamp_IsUnknown()
    {
        var quote = new StockpileMarketQuote
        {
            TypeId = 34,
            QuoteTimestamp = DateTime.UtcNow
        };

        Assert.False(quote.HasQuote);
    }

    [Fact]
    public void Quote_WithSnapshot_IsKnown_AndCarriesPriceTimestamp()
    {
        var timestamp = new DateTime(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc);
        var quote = new StockpileMarketQuote
        {
            TypeId = 34,
            BestSellPrice = 5.5,
            BestBuyPrice = 5.1,
            QuoteTimestamp = timestamp
        };

        Assert.True(quote.HasQuote);
        Assert.Equal(timestamp, quote.QuoteTimestamp);
    }

    [Fact]
    public void MarketContext_WithoutComparisonMarket_IsExplicitlyUnknown()
    {
        var context = new StockpileMarketContext();

        Assert.False(context.HasComparisonMarket);
        Assert.Null(context.ComparisonMarketName);
        Assert.Empty(context.Quotes);
        Assert.Empty(context.HubsByLocation);
    }

    // ---- Hub-Beschreibung: jeder Fehlfall bleibt benannt ----

    [Fact]
    public void LocationHub_UnresolvedSystem_IsUnknown()
    {
        var hub = new StockpileLocationHub { SystemResolved = false };

        Assert.Equal("Ort/System unbekannt", hub.Describe());
    }

    [Fact]
    public void LocationHub_GraphUnavailable_IsNamed()
    {
        var hub = new StockpileLocationHub { SystemResolved = true, GraphAvailable = false };

        Assert.Equal("Systemkarte nicht verfügbar", hub.Describe());
    }

    [Fact]
    public void LocationHub_NoActiveHub_IsNamed()
    {
        var hub = new StockpileLocationHub { SystemResolved = true, GraphAvailable = true };

        Assert.Equal("kein aktiver Hub konfiguriert", hub.Describe());
    }

    [Fact]
    public void LocationHub_WithDistance_ShowsExactJumps()
    {
        var hub = new StockpileLocationHub
        {
            SystemResolved = true,
            GraphAvailable = true,
            HubName = "Jita",
            JumpDistance = 5
        };

        Assert.Equal("Jita (5 Sprünge)", hub.Describe());
    }

    [Fact]
    public void LocationHub_UnreachableHub_IsNotZeroJumps()
    {
        var hub = new StockpileLocationHub
        {
            SystemResolved = true,
            GraphAvailable = true,
            HubName = "Jita",
            JumpDistance = null
        };

        Assert.Equal("Jita (nicht erreichbar)", hub.Describe());
    }
}