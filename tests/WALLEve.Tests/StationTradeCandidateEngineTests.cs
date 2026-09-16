using WALLEve.Models.Database;
using WALLEve.Models.Esi.Markets;
using WALLEve.Models.Trading;
using WALLEve.Services.Trading;

namespace WALLEve.Tests;

/// <summary>
/// Issue #71, Akzeptanzkriterium 1 und 2: Manipulierte Top-Order, illiquider
/// Spread, fehlende History und negative Nettomarge ergeben KEINE belastbare
/// Chance; die ausführbare Menge und die Gebühren-Fixture sind reproduzierbar.
/// Der Motor ist reine C#-Logik ohne Datenbank und ohne Live-ESI.
/// </summary>
public class StationTradeCandidateEngineTests
{
    private const int RegionId = 10000002;      // The Forge
    private const int TypeId = 34;              // Tritanium
    private const long BuyLocation = 60003760;  // Jita IV-4
    private const long SellLocation = 60008494; // Amarr VIII
    private static readonly DateTime Now = new(2026, 9, 16, 12, 0, 0, DateTimeKind.Utc);

    private static readonly StationTradeFees Fees = new(BrokerFeeRate: 0.015, SalesTaxRate: 0.03);

    /// <summary>Vollständige 30-Tage-History mit dokumentiertem Schnitt und Tagesvolumen.</summary>
    private static List<MarketHistory> History(double averagePrice = 5.0, long dailyVolume = 1_000_000, int days = 30)
        => Enumerable.Range(1, days)
            .Select(i => new MarketHistory
            {
                RegionId = RegionId,
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

    private static StationTradeCandidateResult Evaluate(
        IReadOnlyList<RegionalMarketOrder>? asks = null,
        IReadOnlyList<RegionalMarketOrder>? bids = null,
        IReadOnlyList<MarketHistory>? history = null,
        StationTradeFees? fees = null)
        => StationTradeCandidateEngine.Evaluate(
            RegionId, TypeId,
            asks ?? ProfitableAsks(),
            bids ?? ProfitableBids(),
            history ?? History(),
            fees ?? Fees,
            Now);

    [Fact]
    public void Evaluate_ProfitableCandidate_IsActionableWithExecutableQuantityAndFees()
    {
        var result = Evaluate();

        Assert.True(result.IsActionable);
        Assert.Null(result.NotActionableReason);
        Assert.Equal(StationTradeCandidateEngine.AlgorithmVersion, result.AlgorithmVersion);

        // Ausführbare Menge = Min(kumulative Ask-Tiefe, kumulative Bid-Tiefe):
        // Asks bis 4,08 ISK (4,00 + 2 %) = 1.500; Bids ab 4,90 ISK = 1.200.
        Assert.Equal(1500, result.CumulativeAskDepth);
        Assert.Equal(1200, result.CumulativeBidDepth);
        Assert.Equal(1200, result.Quantity);

        Assert.Equal(4.00, result.BuyPricePerUnit!.Value, 6);
        Assert.Equal(5.00, result.SellPricePerUnit!.Value, 6);
        Assert.Equal(BuyLocation, result.BuyLocationId);
        Assert.Equal(SellLocation, result.SellLocationId);

        // Gebühren-Fixture: Kauf 4,00 × 1,015 = 4,06; Verkauf 5,00 × 0,955 = 4,775.
        Assert.Equal(4800.0, result.RequiredCapital!.Value, 6);
        Assert.Equal(858.0, result.NetProfit!.Value, 6); // 0,715 ISK × 1.200
        Assert.Equal(858.0 / 4872.0 * 100.0, result.RoiPercent!.Value, 6);

        Assert.Equal(30, result.HistoryDays);
        Assert.Equal(1_000_000.0, result.AvgDailyVolume!.Value, 6);

        // Evidenz nennt Menge, Alter der Top-Orders und die Gebührenherkunft-Zahlen.
        Assert.Contains("Ausführbar: 1.200 Stück", result.Evidence);
        Assert.Contains("Top-Order 2 h alt", result.Evidence);
        Assert.Contains("Broker 1,5 %", result.Evidence);
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
        // Lockvogel-Ask deutlich unter dem 30-Tage-Schnitt (1,00 ISK statt 5,00).
        var asks = new List<RegionalMarketOrder>
        {
            Order(1.00, 1000, isBuyOrder: false, BuyLocation, TimeSpan.FromHours(1))
        };

        var result = Evaluate(asks: asks);

        Assert.False(result.IsActionable);
        Assert.Contains("Verdächtige Top-Order", result.NotActionableReason);
        Assert.Null(result.Quantity);
    }

    [Fact]
    public void Evaluate_ManipulatedTopBid_IsNotActionable()
    {
        // Übertrieben hohes Lockgebot über dem Zweifachen des Schnitts.
        var bids = new List<RegionalMarketOrder>
        {
            Order(20.00, 1000, isBuyOrder: true, SellLocation, TimeSpan.FromHours(1))
        };

        var result = Evaluate(bids: bids);

        Assert.False(result.IsActionable);
        Assert.Contains("Verdächtige Top-Order", result.NotActionableReason);
    }

    [Fact]
    public void Evaluate_IlliquidSpreadTooLittleDepth_IsNotActionable()
    {
        var asks = new List<RegionalMarketOrder> { Order(4.00, 20, isBuyOrder: false, BuyLocation, TimeSpan.FromHours(2)) };
        var bids = new List<RegionalMarketOrder> { Order(5.00, 20, isBuyOrder: true, SellLocation, TimeSpan.FromHours(2)) };

        var result = Evaluate(asks: asks, bids: bids);

        Assert.False(result.IsActionable);
        Assert.Contains("Illiquider Spread", result.NotActionableReason);
        Assert.Contains("20 ausführbare Einheiten", result.NotActionableReason);
    }

    [Fact]
    public void Evaluate_DepthAboveHistoricalVolume_IsNotActionable()
    {
        // 1.200 ausführbare Einheiten bei 100 Stück historischem Tagesvolumen
        // sind Tiefe ohne Liquidität.
        var result = Evaluate(history: History(dailyVolume: 100));

        Assert.False(result.IsActionable);
        Assert.Contains("Illiquide Tiefe", result.NotActionableReason);
    }

    [Fact]
    public void Evaluate_MissingHistory_IsNotActionable()
    {
        var result = Evaluate(history: History(days: 10));

        Assert.False(result.IsActionable);
        Assert.Contains("Fehlende History", result.NotActionableReason);
        Assert.Contains("10 von mindestens 14 Tagen", result.NotActionableReason);
    }

    [Fact]
    public void Evaluate_NegativeNetMarginAfterFees_IsNotActionable()
    {
        // Spread 0 bei 4,5 % Gesamtgebühren kann keine positive Nettomarge ergeben.
        var bids = new List<RegionalMarketOrder>
        {
            Order(4.00, 1000, isBuyOrder: true, SellLocation, TimeSpan.FromHours(5))
        };

        var result = Evaluate(bids: bids);

        Assert.False(result.IsActionable);
        Assert.Contains("Negative Nettomarge", result.NotActionableReason);
    }

    [Fact]
    public void Evaluate_MissingOrdersOnOneSide_IsNotActionable()
    {
        var result = Evaluate(bids: new List<RegionalMarketOrder>());

        Assert.False(result.IsActionable);
        Assert.Contains("Keine Orders", result.NotActionableReason);
    }

    [Fact]
    public void Evaluate_InvalidFeeRates_IsNotActionable()
    {
        var result = Evaluate(fees: new StationTradeFees(BrokerFeeRate: 1.5, SalesTaxRate: 0.03));

        Assert.False(result.IsActionable);
        Assert.Contains("Ungültige Gebühren-Sätze", result.NotActionableReason);
    }

    /// <summary>
    /// Akzeptanzkriterium 2 (UI): Die ausführbare Menge ist aus der Evidenz des
    /// Kandidaten für die Action Card ableitbar — das dokumentierte Muster
    /// „Ausführbar: N Stück" wird geparst, fremde Typen bleiben unberührt.
    /// </summary>
    [Fact]
    public void Evaluate_Evidence_ExposesQuantityForActionCard()
    {
        var result = Evaluate();
        var opportunity = new TradingOpportunity
        {
            TypeId = TypeId,
            CharacterId = 90073315,
            OpportunityType = "station_trading",
            Evidence = result.Evidence,
            AlgorithmVersion = result.AlgorithmVersion
        };

        Assert.Equal(1200, TradingActionCardService.TryParseQuantity(opportunity));
    }

    /// <summary>
    /// Der StationTrade-Vertrag bleibt ohne Sprungdistanz ausführbar: Die Route
    /// zwischen den Stationen ist ohne Routengraph nicht belegbar — der Vertrag
    /// speichert null statt einer erfundenen Zahl.
    /// </summary>
    [Fact]
    public void StationTradeContract_WithoutJumpDistance_IsActionableWithNullJumpDistance()
    {
        var contract = TradeContractFactory.CreateStationTrade(
            tradingOpportunityId: 7, characterId: 90073315, typeId: TypeId,
            algorithmVersion: StationTradeCandidateEngine.AlgorithmVersion, createdAt: Now,
            quantity: 1200, buyLocationId: BuyLocation, sellLocationId: SellLocation,
            buyRegionId: RegionId, sellRegionId: RegionId,
            buyPricePerUnit: 4.00m, sellPricePerUnit: 5.00m,
            buyMarketSnapshotId: 11, sellMarketSnapshotId: 11,
            brokerRatePercent: 1.5m, salesTaxPercent: 3.0m, jumpDistance: null,
            estimatedNetProceeds: 5730m, estimatedProfit: 858m, requiredCapital: 4800m,
            netRoiPercent: 17.61m, evidence: "Station-Trade (Region 10000002): …");

        Assert.True(contract.IsActionable);
        Assert.Null(contract.JumpDistance);
        Assert.Equal(TradeKind.StationTrade, contract.Kind);
        Assert.Equal(1200, contract.Quantity);
    }
}