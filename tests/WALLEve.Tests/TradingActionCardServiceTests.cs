using WALLEve.Models.Database;
using WALLEve.Services.Trading;
using WALLEve.Services.Trading.Interfaces;

namespace WALLEve.Tests;

/// <summary>
/// Tests der Action-Card-Engine (Issue #66): Schlüsselfakten (Was/Wo/Menge/Preis/Netto/Annahmen)
/// zuerst, aufklappbare Evidenz, ehrliche Mengen-Ableitung aus der Evidenz (keine erfundenen
/// Mengen), Kopier-Payloads und die Unveränderlichkeit der Karten: Hilfsaktionen sind ein
/// Seitenkanal und verändern die Empfehlung nie. Die Engine ist rein — keine Uhr, kein Netz,
/// keine Live-ESI; die Analyse funktioniert ohne EVE-UI-Scope.
/// </summary>
public class TradingActionCardServiceTests
{
    private readonly ITradingActionCardService _service = new TradingActionCardService();

    private static TradingOpportunity Opportunity(
        int id = 1,
        string type = "inventory_sell",
        int typeId = 34,
        string evidence = "Ortsgebunden (Jita): 1.234 von 5.000 Einheiten — Verkauf bei 1.200,00 ISK bringt netto 500.000 ISK (ROI 12,3%, Break-even 1.000,00 ISK).",
        string? dataQuality = "complete",
        double? actualProfit = null,
        string status = "active")
        => new()
        {
            Id = id,
            CharacterId = 42,
            TypeId = typeId,
            OpportunityType = type,
            BuyLocationId = 60003760,
            BuyPrice = 900.0,
            SellPrice = 1200.0,
            EstimatedProfit = 500_000,
            ActualProfit = actualProfit,
            Score = 78,
            AlgorithmVersion = "inventory-sell-v1",
            Provenance = TradingOpportunity.ProvenanceHeuristic,
            DataQuality = dataQuality,
            BrokerFeeRate = 3.2,
            BrokerFeeOrigin = "automatic",
            SalesTaxRate = 2.0,
            SalesTaxOrigin = "automatic",
            Evidence = evidence,
            Status = status,
            DetectedAt = new DateTime(2026, 9, 15, 10, 0, 0, DateTimeKind.Utc),
            ExpiresAt = new DateTime(2026, 9, 16, 10, 0, 0, DateTimeKind.Utc)
        };

    [Fact]
    public void BuildCards_MapsKeyFacts_WhatWhereQuantityPriceNet()
    {
        var cards = _service.BuildCards(
            new[] { Opportunity() },
            new Dictionary<int, string> { [34] = "Tritanium" },
            new Dictionary<long, string> { [60003760] = "Jita 4-4" });

        var card = Assert.Single(cards);
        Assert.Equal("Tritanium", card.TypeName);                       // Was
        Assert.Equal("Jita 4-4", card.LocationLabel);                   // Wo
        Assert.Equal(1234, card.Quantity);                              // Menge (aus Evidenz)
        Assert.Equal(900.0, card.BuyPrice);                             // Preis
        Assert.Equal(1200.0, card.SellPrice);
        Assert.Equal(500_000, card.NetValue);                           // Netto (Schätzung)
        Assert.False(card.HasActualResult);
    }

    [Fact]
    public void BuildCards_CarriesEsiUiIds_TypeIdAndLocationId()
    {
        var cards = _service.BuildCards(
            new[] { Opportunity(typeId: 34) },
            new Dictionary<int, string> { [34] = "Tritanium" },
            new Dictionary<long, string> { [60003760] = "Jita 4-4" });

        var card = Assert.Single(cards);
        Assert.Equal(34, card.TypeId);                    // Marktdetails öffnen (type_id)
        Assert.Equal(60003760L, card.LocationId);         // Wegpunkt setzen (Kaufstation)
    }

    [Fact]
    public void BuildCards_WithActualProfit_ShowsActualResult()
    {
        var cards = _service.BuildCards(
            new[] { Opportunity(actualProfit: 480_000) },
            new Dictionary<int, string> { [34] = "Tritanium" },
            new Dictionary<long, string>());

        var card = Assert.Single(cards);
        Assert.True(card.HasActualResult);
        Assert.Equal(480_000, card.NetValue);
        Assert.Equal(500_000, card.EstimatedProfit);
        Assert.Equal(480_000, card.ActualProfit);
    }

    [Theory]
    [InlineData("Ortsgebunden (Jita): 1.234 von 5.000 Einheiten — Verkauf …", 1234)]
    [InlineData("Ortsgebunden (Ort): 50 × Tritanium — Verkauf …", 50)]
    [InlineData("Ortsgebunden (Ort): 7 von 7 Einheiten — Verkauf …", 7)]
    public void TryParseQuantity_KnownEvidencePatterns_ReturnsQuantity(string evidence, int expected)
    {
        var opp = Opportunity(evidence: evidence);
        Assert.Equal(expected, TradingActionCardService.TryParseQuantity(opp));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Station Trading: Spread zwischen zwei Regionen.")]
    [InlineData(null)]
    public void TryParseQuantity_UnknownOrForeignPattern_ReturnsNull(string? evidence)
    {
        var opp = Opportunity(type: "arbitrage", evidence: evidence ?? string.Empty);
        Assert.Null(TradingActionCardService.TryParseQuantity(opp));
    }

    [Fact]
    public void TryParseQuantity_NonInventoryType_NeverParses()
    {
        // Auch wenn die Evidenz eine Menge enthält, gilt die Ableitung nur für
        // Bestands-Verkaufsempfehlungen — kein erfundener Wert für andere Typen.
        var opp = Opportunity(type: "trend", evidence: "Ortsgebunden (Jita): 1.234 von 5.000 Einheiten — …");
        Assert.Null(TradingActionCardService.TryParseQuantity(opp));
    }

    [Fact]
    public void BuildCards_CopyPayloads_SellPriceAndQuantity()
    {
        var cards = _service.BuildCards(
            new[] { Opportunity() },
            new Dictionary<int, string> { [34] = "Tritanium" },
            new Dictionary<long, string>());

        var card = Assert.Single(cards);
        Assert.Equal("1200.00", card.CopyPricePayload);   // Sell-Preis bevorzugt, invariant
        Assert.Equal("1234", card.CopyQuantityPayload);
    }

    [Fact]
    public void BuildCards_CopyPayloads_WithoutSellPriceUsesBuyPrice()
    {
        var opp = Opportunity();
        opp.SellPrice = null;

        var cards = _service.BuildCards(new[] { opp }, new Dictionary<int, string>(), new Dictionary<long, string>());
        var card = Assert.Single(cards);
        Assert.Equal("900.00", card.CopyPricePayload);
    }

    [Fact]
    public void BuildCards_UnknownQuantity_NoCopyQuantityPayload()
    {
        var opp = Opportunity(evidence: "Keine Menge in dieser Evidenz.");
        var cards = _service.BuildCards(new[] { opp }, new Dictionary<int, string>(), new Dictionary<long, string>());

        var card = Assert.Single(cards);
        Assert.Null(card.Quantity);
        Assert.Null(card.CopyQuantityPayload);
    }

    [Fact]
    public void BuildCards_Assumptions_FromDataQualityAndFeeOrigins()
    {
        var cards = _service.BuildCards(
            new[] { Opportunity() },
            new Dictionary<int, string>(),
            new Dictionary<long, string>());

        var card = Assert.Single(cards);
        Assert.Contains(card.Assumptions, a => a.Contains("Cost-Basis gesichert"));
        Assert.Contains(card.Assumptions, a => a.Contains("Brokergebühr 3,20%") || a.Contains("Brokergebühr 3.20%"));
        Assert.Contains(card.Assumptions, a => a.Contains("Verkaufssteuer"));
    }

    [Fact]
    public void BuildCards_PartialDataQuality_AssumptionMarksEstimate()
    {
        var cards = _service.BuildCards(
            new[] { Opportunity(dataQuality: "partial") },
            new Dictionary<int, string>(),
            new Dictionary<long, string>());

        var card = Assert.Single(cards);
        Assert.Contains(card.Assumptions, a => a.Contains("Cost-Basis geschätzt"));
    }

    [Fact]
    public void BuildCards_EvidenceLines_SplitIntoLines()
    {
        var cards = _service.BuildCards(
            new[] { Opportunity(evidence: "Zeile 1\nZeile 2\n") },
            new Dictionary<int, string>(),
            new Dictionary<long, string>());

        var card = Assert.Single(cards);
        Assert.Equal(2, card.EvidenceLines.Count);
        Assert.Equal("Zeile 1", card.EvidenceLines[0]);
        Assert.Equal("Zeile 2", card.EvidenceLines[1]);
    }

    [Theory]
    [InlineData("executed", true)]
    [InlineData("expired", true)]
    [InlineData("invalid", true)]
    [InlineData("active", false)]
    [InlineData("planned", false)]
    [InlineData("dismissed", false)]
    public void BuildCards_TerminalStatus_NoFurtherUserMarking(string status, bool expectedTerminal)
    {
        var cards = _service.BuildCards(
            new[] { Opportunity(status: status) },
            new Dictionary<int, string>(),
            new Dictionary<long, string>());

        var card = Assert.Single(cards);
        Assert.Equal(status, card.Status);
        Assert.Equal(expectedTerminal, card.IsTerminalStatus);
    }

    [Fact]
    public void BuildCards_LegacyProvenance_MarkedOnCard()
    {
        var opp = Opportunity();
        opp.Provenance = TradingOpportunity.ProvenanceLegacy;

        var cards = _service.BuildCards(new[] { opp }, new Dictionary<int, string>(), new Dictionary<long, string>());
        Assert.True(Assert.Single(cards).IsLegacyProvenance);
    }

    [Fact]
    public void BuildCards_MissingNames_FallbackLabels()
    {
        var cards = _service.BuildCards(
            new[] { Opportunity() },
            new Dictionary<int, string>(),
            new Dictionary<long, string>());

        var card = Assert.Single(cards);
        Assert.Equal("Type 34", card.TypeName);
        Assert.Equal("Location 60003760", card.LocationLabel);
    }

    [Fact]
    public void BuildCards_CardIsImmutable_HelperActionsCannotAlterRecommendation()
    {
        // Die Karte ist init-only: Es gibt keine API, über die eine Hilfsaktion
        // (Kopieren, später Marktdetails/Wegpunkt) die Empfehlung verändern könnte.
        var cards = _service.BuildCards(
            new[] { Opportunity() },
            new Dictionary<int, string> { [34] = "Tritanium" },
            new Dictionary<long, string>());

        var card = Assert.Single(cards);
        Assert.Equal(500_000, card.NetValue);
        Assert.Equal(1200.0, card.SellPrice);
        Assert.Equal(1234, card.Quantity);
    }

    [Fact]
    public void BuildCards_EmptyList_ReturnsEmpty()
    {
        var cards = _service.BuildCards(Array.Empty<TradingOpportunity>(), new Dictionary<int, string>(), new Dictionary<long, string>());
        Assert.Empty(cards);
    }

    [Fact]
    public void BuildCards_PreservesInputOrder_SortAppliesToCardList()
    {
        // Regression zu Review-Befund #66 (PR #139): die Trading-Seite sortiert
        // _filteredOpportunities (Score/Profit/Zeitstempel) und rendert daraus die
        // Karten. BuildCards darf die übergebene Reihenfolge nicht verändern, sonst
        // zeigt die Kartenliste eine andere Sortierung als die gefilterte Liste.
        var low = Opportunity(id: 1);
        var high = Opportunity(id: 2);
        high.Score = 99;
        high.EstimatedProfit = 1_000_000;

        var cards = _service.BuildCards(
            new[] { high, low },
            new Dictionary<int, string>(),
            new Dictionary<long, string>());

        Assert.Equal(2, cards.Count);
        Assert.Equal(2, cards[0].OpportunityId);
        Assert.Equal(1, cards[1].OpportunityId);
    }
}
