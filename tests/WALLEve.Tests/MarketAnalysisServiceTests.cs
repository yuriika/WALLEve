using Microsoft.EntityFrameworkCore;
using WALLEve.Data;
using WALLEve.Models.Database;
using WALLEve.Services.AI.Interfaces;
using WALLEve.Services.Market;

namespace WALLEve.Tests;

/// <summary>
/// Tests für die Markt-Analyse: Dedup (keine Duplikate bei wiederholter Analyse),
/// Fee-basierte Profit-Berechnung statt pauschalem 0.95-Faktor und Cleanup
/// abgelaufener Opportunities.
/// </summary>
public class MarketAnalysisServiceTests
{
    private sealed class FakeOllamaService : IOllamaService
    {
        public Task<string> GenerateAsync(string prompt, object? context = null, string? model = null)
            => Task.FromResult("mock");

        public Task<T?> GenerateJsonAsync<T>(string prompt, object? context = null, string? model = null)
            => Task.FromResult(default(T));

        public Task<bool> IsAvailableAsync() => Task.FromResult(false);
        public Task<List<string>?> GetAvailableModelsAsync() => Task.FromResult<List<string>?>(null);
    }

    private static MarketAnalysisService CreateService(WalletDbContext db)
        => new(new FakeOllamaService(), db, new FeeCalculatorService(), NullLogger());

    private static Microsoft.Extensions.Logging.ILogger<MarketAnalysisService> NullLogger()
        => Microsoft.Extensions.Logging.Abstractions.NullLogger<MarketAnalysisService>.Instance;

    /// <summary>Legt einen Snapshot mit gutem Spread an (Type 44992, Jita, vor 10 Min).</summary>
    private static async Task SeedSnapshotAsync(WalletDbContext db)
    {
        db.MarketSnapshots.Add(new MarketSnapshot
        {
            RegionId = 10000002,
            TypeId = 44992,
            Timestamp = DateTime.UtcNow.AddMinutes(-10),
            BestBuyPrice = 100.0,
            BestSellPrice = 110.0,
            BestBuySystemId = 30000142,
            BestSellSystemId = 30000142,
            BuyVolume = 1000,
            SellVolume = 1000,
            Spread = 10.0
        });
        await db.SaveChangesAsync();
    }

    // ------------------------------------------------------------------
    // Dedup
    // ------------------------------------------------------------------

    [Fact]
    public async Task Analyze_TwiceWithSameSnapshot_CreatesOnlyOneOpportunity()
    {
        using var db = TestDb.Create();
        await SeedSnapshotAsync(db);
        var service = CreateService(db);

        var first = await service.AnalyzeMarketDataAsync();
        var second = await service.AnalyzeMarketDataAsync();

        Assert.Single(first.Where(o => o.TypeId == 44992 && o.Status == "active"));
        var dbCount = await db.TradingOpportunities.CountAsync(o => o.TypeId == 44992 && o.Status == "active");
        Assert.Equal(1, dbCount); // Zweiter Lauf erzeugt KEIN Duplikat
    }

    // ------------------------------------------------------------------
    // Fee-basierte Profit-Berechnung
    // ------------------------------------------------------------------

    [Fact]
    public async Task Analyze_ProfitUsesFeeCalculator_NotFlat095()
    {
        using var db = TestDb.Create();
        await SeedSnapshotAsync(db); // Buy 100, Sell 110
        var service = CreateService(db);

        var opportunities = await service.AnalyzeMarketDataAsync();
        var opp = opportunities.First(o => o.TypeId == 44992);

        // Ohne Skills: 3% Broker auf Buy (3 ISK) + 3% Broker + 7,5% Tax auf Sell (11,55 ISK)
        // Netto = 110 − 11,55 − 103 = −4,55 ISK → pauschal ×0.95 hätte +4.5 ergeben (falsch!)
        Assert.Equal(-4.55, opp.EstimatedProfit, 2);
    }

    [Fact]
    public async Task Analyze_WideSpread_ProfitIsPositive()
    {
        using var db = TestDb.Create();
        // Spread 40%: Sell 140 → Netto = 140×(1−0.03−0.075) = 125,3; Buy inkl. 3% Broker = 103
        // Profit = 125,3 − 103 = 22,3
        db.MarketSnapshots.Add(new MarketSnapshot
        {
            RegionId = 10000042,
            TypeId = 40520,
            Timestamp = DateTime.UtcNow.AddMinutes(-10),
            BestBuyPrice = 100.0,
            BestSellPrice = 140.0,
            BestBuySystemId = 30002071,
            BestSellSystemId = 30002071,
            BuyVolume = 5000,
            SellVolume = 5000,
            Spread = 40.0
        });
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var opportunities = await service.AnalyzeMarketDataAsync();
        var opp = opportunities.First(o => o.TypeId == 40520);

        Assert.Equal(22.3, opp.EstimatedProfit, 2);
        Assert.Contains("fees", opp.Reasoning);
    }

    // ------------------------------------------------------------------
    // Cleanup abgelaufener Opportunities
    // ------------------------------------------------------------------

    [Fact]
    public async Task Analyze_RemovesExpiredOpportunities()
    {
        using var db = TestDb.Create();
        await SeedSnapshotAsync(db);

        // Alte, abgelaufene Opportunity mit gleichem Type in der DB
        db.TradingOpportunities.Add(new TradingOpportunity
        {
            TypeId = 44992,
            OpportunityType = "station_trading",
            BuyPrice = 90, SellPrice = 95,
            EstimatedProfit = 1, RequiredCapital = 90, Confidence = 70,
            AIModel = "heuristic", Reasoning = "old",
            DetectedAt = DateTime.UtcNow.AddHours(-3),
            ExpiresAt = DateTime.UtcNow.AddHours(-2), // abgelaufen!
            Status = "active"
        });
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var opportunities = await service.AnalyzeMarketDataAsync();

        // Abgelaufene wurde gelöscht, neue aktive (aus Snapshot) existiert
        Assert.Single(opportunities.Where(o => o.TypeId == 44992));
        var staleCount = await db.TradingOpportunities.CountAsync(o => o.ExpiresAt < DateTime.UtcNow);
        Assert.Equal(0, staleCount);
    }

    // ------------------------------------------------------------------
    // GetActiveOpportunitiesAsync (lesend)
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetActive_ReturnsOnlyActiveAndSortedByConfidence()
    {
        using var db = TestDb.Create();
        db.TradingOpportunities.AddRange(
            new TradingOpportunity
            {
                TypeId = 1, OpportunityType = "t", BuyPrice = 1, SellPrice = 2,
                EstimatedProfit = 1, Confidence = 50, AIModel = "heuristic",
                DetectedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddHours(1),
                Status = "active"
            },
            new TradingOpportunity
            {
                TypeId = 2, OpportunityType = "t", BuyPrice = 1, SellPrice = 2,
                EstimatedProfit = 1, Confidence = 90, AIModel = "heuristic",
                DetectedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddHours(1),
                Status = "active"
            },
            new TradingOpportunity
            {
                TypeId = 3, OpportunityType = "t", BuyPrice = 1, SellPrice = 2,
                EstimatedProfit = 1, Confidence = 80, AIModel = "heuristic",
                DetectedAt = DateTime.UtcNow.AddHours(-2), ExpiresAt = DateTime.UtcNow.AddHours(-1),
                Status = "expired"
            });
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var active = await service.GetActiveOpportunitiesAsync();

        Assert.Equal(2, active.Count);
        Assert.Equal(2, active[0].TypeId);   // höchste Confidence zuerst
        Assert.Equal(1, active[1].TypeId);
    }
}