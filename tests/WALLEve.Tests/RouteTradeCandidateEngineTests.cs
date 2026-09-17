using WALLEve.Models.Database;
using WALLEve.Models.Esi.Markets;
using WALLEve.Models.Risk;
using WALLEve.Services.Trading;

namespace WALLEve.Tests;

/// <summary>
/// Issue #74, Akzeptanzkriterien 1 und 2: Die ausführbare Menge erfüllt Cargo,
/// Kapital und Tiefe GLEICHZEITIG (Minimum der drei Grenzen); ohne gültige
/// Route gibt es keinen Handel. zKillboard-Ausfall entfernt nur das
/// Risiko-Enrichment — die erwartete Netto-Spanne (mit/ohne Transportkosten)
/// bleibt unverändert nachvollziehbar. Der Motor ist reine C#-Logik ohne
/// Datenbank und ohne Live-ESI.
/// </summary>
public class RouteTradeCandidateEngineTests
{
    private const int BuyRegionId = 10000002;       // The Forge (Jita)
    private const int SellRegionId = 10000043;      // Domain (Amarr)
    private const int TypeId = 34;                  // Tritanium
    private const long BuyLocation = 60003760;      // Jita IV-4
    private const long SellLocation = 60008494;     // Amarr VIII
    private const long OtherLocation = 60004588;    // andere Station
    private static readonly DateTime Now = new(2026, 9, 16, 12, 0, 0, DateTimeKind.Utc);

    private static readonly StationTradeFees Fees = new(BrokerFeeRate: 0.015, SalesTaxRate: 0.03);

    // Jita → Amarr: dokumentierte Beispiel-Route über 7 Sprünge (3 Highsec, 3 Lowsec, 1 Nullsec).
    private static readonly RouteTradeRoute Route = new(
        SystemIds: new[] { 30000142, 30000143, 30000144, 30000145, 30000146, 30000147, 30000148, 30002187 },
        Jumps: 7,
        HighSecJumps: 3,
        LowSecJumps: 3,
        NullSecJumps: 1);

    /// <summary>Default-Annahmen: Cargo 1.000 Einheiten, Kapital 5.000 ISK, 2 Min/Sprung, kein Transportpreis.</summary>
    private static readonly RouteTradeAssumptions Assumptions =
        new(CargoUnits: 1000, MaxCapitalIsk: 5000, MinutesPerJump: 2, TransportCostPerUnit: 0);

    /// <summary>Vollständige 30-Tage-History der Kaufregion mit dokumentiertem Schnitt.</summary>
    private static List<MarketHistory> History(double averagePrice = 5.0, long dailyVolume = 1_000_000, int days = 30)
        => Enumerable.Range(1, days)
            .Select(i => new MarketHistory
            {
                RegionId = BuyRegionId,
                TypeId = TypeId,
                Date = Now.Date.AddDays(-i),
                Average = averagePrice,
                Highest = averagePrice * 1.1,
                Lowest = averagePrice * 0.9,
                Volume = dailyVolume,
                OrderCount = 100
            })
            .ToList();

    private static RegionalMarketOrder Order(double price, int volume, bool isBuyOrder, long locationId, TimeSpan age)
        => new()
        {
            OrderId = (long)(price * 1000) + (isBuyOrder ? 1 : 2) + locationId,
            TypeId = TypeId,
            LocationId = locationId,
            SystemId = isBuyOrder ? 30002187 : 30000142,
            VolumeTotal = volume,
            VolumeRemain = volume,
            MinVolume = 1,
            Price = price,
            IsBuyOrder = isBuyOrder,
            Duration = 90,
            Issued = Now - age,
            Range = "station"
        };

    /// <summary>Belastbarer Kandidat: Ask 4,00 / Bid 5,00, genug Tiefe und History.</summary>
    private static List<RegionalMarketOrder> ProfitableAsks() => new()
    {
        Order(4.00, 1000, isBuyOrder: false, BuyLocation, TimeSpan.FromHours(2)),
        Order(4.05, 500, isBuyOrder: false, BuyLocation, TimeSpan.FromHours(6))
    };

    private static List<RegionalMarketOrder> ProfitableBids() => new()
    {
        Order(5.00, 800, isBuyOrder: true, SellLocation, TimeSpan.FromHours(5)),
        Order(4.95, 400, isBuyOrder: true, SellLocation, TimeSpan.FromHours(9))
    };

    private static RouteTradeCandidateResult Evaluate(
        IReadOnlyList<RegionalMarketOrder>? asks = null,
        IReadOnlyList<RegionalMarketOrder>? bids = null,
        IReadOnlyList<MarketHistory>? history = null,
        StationTradeFees? fees = null,
        RouteTradeRoute? route = null,
        RouteTradeAssumptions? assumptions = null,
        RouteRiskSummary? risk = null)
        => RouteTradeCandidateEngine.Evaluate(
            BuyRegionId, SellRegionId, TypeId,
            asks ?? ProfitableAsks(),
            bids ?? ProfitableBids(),
            history ?? History(),
            fees ?? Fees,
            route ?? Route,
            assumptions ?? Assumptions,
            risk,
            Now);

    /// <summary>Risiko-Summary mit nicht verfügbarem zKillboard (Nr. 73: konservativ unbekannt).</summary>
    private static RouteRiskSummary RiskWithZkillboardUnavailable()
    {
        var summary = new RouteRiskSummary
        {
            RouteLevel = RiskLevel.Unknown,
            LastCollectedAt = Now
        };
        summary.UnavailableSources.Add(RiskEvidenceSource.Zkillboard);
        return summary;
    }

    [Fact]
    public void Evaluate_ProfitableCandidate_IsActionableWithCargoCapitalDepthMin()
    {
        var result = Evaluate();

        Assert.True(result.IsActionable);
        Assert.Null(result.NotActionableReason);
        Assert.Equal(RouteTradeCandidateEngine.AlgorithmVersion, result.AlgorithmVersion);

        // Tiefe: Asks bis 4,08 ISK = 1.500; Bids ab 4,90 ISK = 1.200 → Tiefen-Min 1.200.
        Assert.Equal(1500, result.CumulativeAskDepth);
        Assert.Equal(1200, result.CumulativeBidDepth);

        // Kapitalgrenze = floor(5.000 / (4,00 × 1,015)) = floor(1.231,5) = 1.231.
        Assert.Equal(1231, result.CapitalLimitQuantity);
        Assert.Equal(1000, result.CargoLimitQuantity);

        // Menge = Min(Cargo 1.000, Kapital 1.231, Tiefen 1.200) = 1.000 (Cargo bindet).
        Assert.Equal(1000, result.Quantity);

        Assert.Equal(4.00, result.BuyPricePerUnit!.Value, 6);
        Assert.Equal(5.00, result.SellPricePerUnit!.Value, 6);
        Assert.Equal(BuyLocation, result.BuyLocationId);
        Assert.Equal(SellLocation, result.SellLocationId);

        // Netto mit Transportannahme (0 ISK/Stück → identisch): 0,715 ISK/Stück × 1.000.
        Assert.Equal(715.0, result.NetProfit!.Value, 6);
        Assert.Equal(result.NetProfit, result.NetProfitExclTransport);

        Assert.Equal(7, result.JumpCount);
        Assert.Equal(14.0, result.TransportMinutes!.Value, 6); // 7 Sprünge × 2 Min

        Assert.Contains("Ausführbar: 1.000 Stück (Min(Cargo, Kapital, Tiefen)", result.Evidence);
        Assert.Contains("Route: 7 Sprünge (Highsec 3, Lowsec 3, Nullsec 1)", result.Evidence);
        Assert.Contains("14 Min Transportzeit", result.Evidence);
    }

    [Fact]
    public void Evaluate_CapitalBindsQuantity_IsMinOfAllThreeLimits()
    {
        // Cargo 100.000 (nicht bindend), Kapital 3.000 ISK → floor(3.000/4,06) = 738.
        var assumptions = new RouteTradeAssumptions(
            CargoUnits: 100_000, MaxCapitalIsk: 3000, MinutesPerJump: 2, TransportCostPerUnit: 0);
        var result = Evaluate(assumptions: assumptions);

        Assert.True(result.IsActionable);
        Assert.Equal(100_000, result.CargoLimitQuantity);
        Assert.Equal(738, result.CapitalLimitQuantity);
        Assert.Equal(738, result.Quantity);
        // Evidenz nennt alle drei Grenzen — die Menge ist nachvollziehbar.
        Assert.Contains("Min(Cargo, Kapital, Tiefen): Cargo 100.000, Kapital 738, Tiefen 1.200", result.Evidence);
    }

    [Fact]
    public void Evaluate_MissingRoute_IsNotActionable()
    {
        var emptyRoute = new RouteTradeRoute(SystemIds: new List<int>(), Jumps: 0, HighSecJumps: 0, LowSecJumps: 0, NullSecJumps: 0);
        var noJumpRoute = new RouteTradeRoute(SystemIds: new[] { 30000142 }, Jumps: 0, HighSecJumps: 0, LowSecJumps: 0, NullSecJumps: 0);

        var emptyResult = Evaluate(route: emptyRoute);
        var noJumpResult = Evaluate(route: noJumpRoute);

        Assert.False(emptyResult.IsActionable);
        Assert.Contains("Keine gültige Route", emptyResult.NotActionableReason);
        Assert.False(noJumpResult.IsActionable);
        Assert.Contains("Keine gültige Route", noJumpResult.NotActionableReason);
        Assert.Null(emptyResult.Quantity);
    }

    [Fact]
    public void Evaluate_QuantityBelowMinimum_IsNotActionable()
    {
        // Kapitalgrenze unter der Mindestmenge: floor(500 / 4,06) = 123 < 100 → nein, 123 ≥ 100.
        // Stattdessen Cargo 50: Min(Cargo 50, Kapital 1.231, Tiefen 1.200) = 50 < 100.
        var smallCargo = new RouteTradeAssumptions(
            CargoUnits: 50, MaxCapitalIsk: 5000, MinutesPerJump: 2, TransportCostPerUnit: 0);
        var result = Evaluate(assumptions: smallCargo);

        Assert.False(result.IsActionable);
        Assert.Contains("Menge erfüllt Cargo/Kapital/Tiefe nicht gleichzeitig", result.NotActionableReason);
        Assert.Null(result.Quantity);
    }

    [Fact]
    public void Evaluate_NegativeNetAfterTransport_IsNotActionable()
    {
        // Netto je Stück vor Transport = 0,715 ISK → Transportannahme 1 ISK/Stück kippt die Marge.
        var expensiveTransport = new RouteTradeAssumptions(
            CargoUnits: 1000, MaxCapitalIsk: 5000, MinutesPerJump: 2, TransportCostPerUnit: 1.0);
        var result = Evaluate(assumptions: expensiveTransport);

        Assert.False(result.IsActionable);
        Assert.Contains("Negative Nettomarge nach Gebühren und Transport", result.NotActionableReason);
        Assert.Null(result.NetProfit);
    }

    [Fact]
    public void Evaluate_TransportCostReducesNet_BothBoundsReported()
    {
        // Transportannahme 0,20 ISK/Stück: Netto mit Transport 0,515 × 1.000 = 515,
        // ohne Transport 0,715 × 1.000 = 715 — die Spanne bleibt in der Evidenz.
        var withTransport = new RouteTradeAssumptions(
            CargoUnits: 1000, MaxCapitalIsk: 5000, MinutesPerJump: 2, TransportCostPerUnit: 0.20);
        var result = Evaluate(assumptions: withTransport);

        Assert.True(result.IsActionable);
        Assert.Equal(515.0, result.NetProfit!.Value, 6);
        Assert.Equal(715.0, result.NetProfitExclTransport!.Value, 6);
        Assert.Contains("Netto 515 ISK mit Transport (715 ISK ohne Transport)", result.Evidence);
    }

    [Fact]
    public void Evaluate_SameInputsTwice_IsReproducible()
    {
        var first = Evaluate();
        var second = Evaluate();

        Assert.Equal(first, second);
        Assert.Equal(first.Evidence, second.Evidence);
    }

    [Fact]
    public void Evaluate_ManipulatedTopAsk_IsNotActionable()
    {
        // Best-Ask 2,00 ISK liegt unter 50 % des 30-Tage-Schnitts (5,00 ISK).
        var asks = new List<RegionalMarketOrder> { Order(2.00, 1000, isBuyOrder: false, BuyLocation, TimeSpan.FromHours(2)) };
        var result = Evaluate(asks: asks);

        Assert.False(result.IsActionable);
        Assert.Contains("Verdächtige Top-Order", result.NotActionableReason);
    }

    [Fact]
    public void Evaluate_ZkillboardUnavailable_RemovesOnlyEnrichment_NetAndQuantityUnchanged()
    {
        var withRisk = Evaluate(risk: RiskWithZkillboardUnavailable());
        var withoutRisk = Evaluate(risk: null);

        // Akzeptanzkriterium 2: Die Rechnung bleibt identisch — nur das
        // Enrichment (Risiko-Anzeige) unterscheidet sich.
        Assert.True(withRisk.IsActionable);
        Assert.Equal(withoutRisk.Quantity, withRisk.Quantity);
        Assert.Equal(withoutRisk.NetProfit, withRisk.NetProfit);
        Assert.Equal(withoutRisk.NetProfitExclTransport, withRisk.NetProfitExclTransport);
        Assert.Equal(withoutRisk.Evidence, withRisk.Evidence); // Risiko ist reines Enrichment, nie Teil der Rechnung

        Assert.Equal(RiskLevel.Unknown, withRisk.RouteRiskLevel);
        Assert.Contains("Routen-Risiko: unbekannt", string.Join("\n", withRisk.RiskLines));
        Assert.Contains("zKillboard nicht verfügbar", string.Join("\n", withRisk.RiskLines));
        Assert.Empty(withoutRisk.RiskLines);
    }

    [Fact]
    public void BuildRiskLines_EsiOnly_NotesMissingZkillboard()
    {
        var summary = new RouteRiskSummary { RouteLevel = RiskLevel.Low };
        summary.UnavailableSources.Add(RiskEvidenceSource.Zkillboard);

        var lines = RouteTradeCandidateEngine.BuildRiskLines(summary);

        Assert.Contains(lines, l => l.Contains("Routen-Risiko: niedrig", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("zKillboard nicht verfügbar", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildRiskLines_Null_IsEmpty()
    {
        Assert.Empty(RouteTradeCandidateEngine.BuildRiskLines(null));
    }
}