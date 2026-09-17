using Microsoft.EntityFrameworkCore;
using WALLEve.Data;
using WALLEve.Models.Authentication;
using WALLEve.Models.Database;
using WALLEve.Models.Esi.Alliance;
using WALLEve.Models.Esi.Character;
using WALLEve.Models.Esi.Corporation;
using WALLEve.Models.Esi.Markets;
using WALLEve.Models.Esi.Universe;
using WALLEve.Models.Esi.Wallet;
using WALLEve.Models.Measurement;
using WALLEve.Models.Trading;
using WALLEve.Services.Authentication.Interfaces;
using WALLEve.Services.Esi.Interfaces;
using WALLEve.Services.Market;
using WALLEve.Services.Market.Interfaces;
using WALLEve.Services.Trading;
using WALLEve.Services.Trading.Interfaces;

namespace WALLEve.Tests;

/// <summary>
/// Review-Auflage zu Issue #71: abgelaufene station_trading-Empfehlungen dürfen
/// nicht reaktiviert werden. "expired" ist terminal (Issue #45) — der Lauf
/// markiert abgelaufene Zeilen als abgelaufen, lässt sie unverändert und erzeugt
/// für einen weiterhin belastbaren Kandidaten eine NEUE Empfehlung, statt die
/// abgelaufene Zeile fortzuschreiben.
/// </summary>
public class StationTradeAnalysisServiceTests
{
    private const int CharacterId = 90073315;
    private const int RegionId = 10000002;      // The Forge (Jita), erster TrackedHub
    private const int TypeId = 34;              // Tritanium (Standard-Handelsgut)
    private const long BuyLocation = 60003760;  // Jita IV-4
    private const long SellLocation = 60008494; // Amarr VIII

    private static readonly DateTime Now = DateTime.UtcNow;

    // ------------------------------------------------------------------
    // Fakes
    // ------------------------------------------------------------------

    private sealed class FakeAuthService : IEveAuthenticationService
    {
        public Task<EveAuthState?> GetAuthStateAsync()
            => Task.FromResult<EveAuthState?>(new EveAuthState
            {
                AccessToken = "tok", RefreshToken = "ref",
                CharacterId = CharacterId, CharacterName = "Test"
            });

        public Task<bool> IsAuthenticatedAsync() => Task.FromResult(true);
        public string GetLoginUrl() => "http://login";
        public Task<bool> HandleCallbackAsync(string code, string state) => Task.FromResult(true);
        public Task<string?> GetAccessTokenAsync() => Task.FromResult<string?>("tok");
        public Task LogoutAsync() => Task.CompletedTask;
        public Task<List<KnownCharacter>> GetAllCharactersAsync() => Task.FromResult(new List<KnownCharacter>());
        public Task<bool> SwitchCharacterAsync(int characterId) => Task.FromResult(true);

        event EventHandler<bool>? IEveAuthenticationService.AuthenticationStateChanged
        {
            add { }
            remove { }
        }
    }

    /// <summary>Fake: nur GetCharacterSkillsAsync wird in der Analyse verwendet; Rest wirft.</summary>
    private sealed class FakeEsiApiService : IEsiApiService
    {
        public CharacterSkills? SkillsResponse { get; set; } = new();

        public Task<CharacterSkills?> GetCharacterSkillsAsync()
            => Task.FromResult(SkillsResponse);

        public Task<CharacterOverview?> GetCharacterOverviewAsync() => throw new NotImplementedException();
        public Task<EveCharacter?> GetCharacterAsync(int characterId) => throw new NotImplementedException();
        public Task<EveCorporation?> GetCorporationAsync(int corporationId) => throw new NotImplementedException();
        public Task<EveAlliance?> GetAllianceAsync(int allianceId) => throw new NotImplementedException();
        public Task<double?> GetWalletBalanceAsync(int characterId) => throw new NotImplementedException();
        public Task<CharacterLocation?> GetLocationAsync(int characterId) => throw new NotImplementedException();
        public Task<CharacterShip?> GetCurrentShipAsync(int characterId) => throw new NotImplementedException();
        public Task<CharacterOnlineStatus?> GetOnlineStatusAsync(int characterId) => throw new NotImplementedException();
        public Task<SolarSystem?> GetSolarSystemAsync(int systemId) => throw new NotImplementedException();
        public Task<EveType?> GetTypeAsync(int typeId) => throw new NotImplementedException();
        public Task<StructureLookupResult> GetStructureAsync(long structureId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<CharacterAsset>?> GetCharacterAssetsAsync(int characterId) => throw new NotImplementedException();

        public Task<List<CharacterMiningEntry>?> GetCharacterMiningLedgerAsync(int characterId, CancellationToken ct = default) => throw new NotImplementedException();

        public Task<List<CharacterIndustryJob>?> GetCharacterIndustryJobsAsync(int characterId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<WalletJournalEntry>?> GetWalletJournalAsync(int characterId, int page = 1) => throw new NotImplementedException();
        public Task<List<WalletTransaction>?> GetWalletTransactionsAsync(int characterId) => throw new NotImplementedException();
        public Task<List<WalletJournalEntry>?> GetAllWalletJournalPagesAsync(int characterId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<WalletTransaction>?> GetAllWalletTransactionsPagesAsync(int characterId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<MarketOrder>?> GetMarketOrdersAsync(int characterId) => throw new NotImplementedException();
        public Task<List<MarketOrderHistory>?> GetMarketOrderHistoryAsync(int characterId) => throw new NotImplementedException();
        public Task<List<WalletJournalEntry>?> GetCorporationWalletJournalAsync(int corporationId, int division, int page = 1) => throw new NotImplementedException();
        public Task<List<WalletTransaction>?> GetCorporationWalletTransactionsAsync(int corporationId, int division) => throw new NotImplementedException();
        public Task<List<SystemJumps>?> GetSystemJumpsAsync() => throw new NotImplementedException();
        public Task<List<SystemKills>?> GetSystemKillsAsync() => throw new NotImplementedException();
        public Task<List<RegionalMarketOrder>?> GetRegionalMarketOrdersAsync(int regionId, int? typeId = null, string orderType = "all", int page = 1) => throw new NotImplementedException();
        public Task<List<RegionalMarketOrder>?> GetAllRegionalMarketOrdersAsync(int regionId, int? typeId = null, string orderType = "all", CancellationToken ct = default, Action<RegionalScanPageTelemetry>? telemetrySink = null) => throw new NotImplementedException();
        public Task<List<MarketHistoryEntry>?> GetMarketHistoryAsync(int regionId, int typeId) => throw new NotImplementedException();
        public Task<List<MarketPrice>?> GetMarketPricesAsync() => throw new NotImplementedException();
    }

    private sealed class FakeRegionalMarketCache : IRegionalMarketCacheService
    {
        private readonly Dictionary<int, List<RegionalMarketOrder>> _orders = new();

        public void AddOrders(int regionId, params RegionalMarketOrder[] orders)
            => _orders[regionId] = orders.ToList();

        public Task<IReadOnlyList<RegionalMarketOrder>> GetRegionOrdersAsync(int regionId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RegionalMarketOrder>>(
                _orders.TryGetValue(regionId, out var orders)
                    ? orders
                    : new List<RegionalMarketOrder>());

        public Task<IReadOnlyList<RegionalMarketOrder>> GetOrdersForTypeAsync(int regionId, int typeId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RegionalMarketOrder>>(
                _orders.TryGetValue(regionId, out var orders)
                    ? orders.Where(o => o.TypeId == typeId).ToList()
                    : new List<RegionalMarketOrder>());

        public RegionalMarketCacheInfo? GetCacheInfo(int regionId)
            => _orders.TryGetValue(regionId, out var orders)
                ? new RegionalMarketCacheInfo(regionId, DateTime.UtcNow, orders.Count, 1, true)
                : null;

        public void Clear() => _orders.Clear();
    }

    // ------------------------------------------------------------------
    // Fixtures (gleiche Form wie der Motoren-Test: Ask 4,00 / Bid 5,00,
    // 30 Tage History, ausführbare Menge 1.200)
    // ------------------------------------------------------------------

    private static StationTradeAnalysisService CreateService(WalletDbContext db, FakeRegionalMarketCache cache)
        => new(
            db, new FeeCalculatorService(), new FakeAuthService(), new FakeEsiApiService(),
            cache, new TradeStatusService(db),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<StationTradeAnalysisService>.Instance);

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

    /// <summary>Aktionierbarer Kandidat: Ask 4,00 (Tiefe 1.500) / Bid 5,00 (Tiefe 1.200).</summary>
    private static RegionalMarketOrder[] ActionableOrders() =>
    [
        Order(4.00, 1000, isBuyOrder: false, BuyLocation, TimeSpan.FromHours(2)),
        Order(4.05, 500, isBuyOrder: false, BuyLocation, TimeSpan.FromHours(6)),
        Order(5.00, 800, isBuyOrder: true, SellLocation, TimeSpan.FromHours(5)),
        Order(4.95, 400, isBuyOrder: true, SellLocation, TimeSpan.FromHours(9))
    ];

    /// <summary>Abgelaufene station_trading-Zeile im Legacy-Zustand "planned" (vor #45 auch "active").</summary>
    private static TradingOpportunity ExpiredStationTrade(DateTime detectedAt, DateTime expiresAt) => new()
    {
        CharacterId = CharacterId,
        TypeId = TypeId,
        OpportunityType = "station_trading",
        BuyRegionId = RegionId,
        SellRegionId = RegionId,
        BuySystemId = 30000142, SellSystemId = 30002187,
        BuyLocationId = BuyLocation, SellLocationId = SellLocation,
        BuyPrice = 4.0, SellPrice = 5.0,
        EstimatedProfit = 355, RequiredCapital = 4944, Score = 70,
        Provenance = TradingOpportunity.ProvenanceHeuristic,
        Evidence = "alte Empfehlung (unverändert zu lassen)",
        DetectedAt = detectedAt,
        ExpiresAt = expiresAt,
        Status = RecommendationStatus.Planned
    };

    // ------------------------------------------------------------------
    // Review-Auflage: abgelaufene Empfehlung wird nicht reaktiviert
    // ------------------------------------------------------------------

    [Fact]
    public async Task Analyze_ExpiredOpportunity_StaysExpiredAndProducesFreshOpportunity()
    {
        using var db = TestDb.Create();
        var cache = new FakeRegionalMarketCache();
        cache.AddOrders(RegionId, ActionableOrders());
        db.MarketHistory.AddRange(History());

        var detectedAt = Now.AddHours(-5);
        var expiresAt = Now.AddHours(-3);
        var old = ExpiredStationTrade(detectedAt, expiresAt);
        db.TradingOpportunities.Add(old);
        await db.SaveChangesAsync();

        var service = CreateService(db, cache);
        var result = await service.AnalyzeStationTradesAsync();

        // Die abgelaufene Zeile bleibt terminal "expired" und wird nicht
        // fortgeschrieben (keine verlängerte Ablaufzeit, keine neuen Zahlen).
        var reloaded = await db.TradingOpportunities.AsNoTracking().SingleAsync(o => o.Id == old.Id);
        Assert.Equal(RecommendationStatus.Expired, reloaded.Status);
        Assert.Equal(expiresAt, reloaded.ExpiresAt);
        Assert.Equal(detectedAt, reloaded.DetectedAt);
        Assert.Equal("alte Empfehlung (unverändert zu lassen)", reloaded.Evidence);

        // Genau ein System-Übergang planned → expired; nichts anderes.
        var changes = await db.TradeStatusChanges.AsNoTracking()
            .Where(c => c.TradingOpportunityId == old.Id)
            .ToListAsync();
        var change = Assert.Single(changes);
        Assert.Equal(RecommendationStatus.Planned, change.FromStatus);
        Assert.Equal(RecommendationStatus.Expired, change.ToStatus);
        Assert.Equal(TradeStatusSource.System, change.Source);

        // Der weiterhin belastbare (Region, Item)-Kandidat existiert als NEUE
        // Empfehlung — nicht als reaktivierte Altzeile.
        var fresh = await db.TradingOpportunities.AsNoTracking()
            .SingleAsync(o => o.Id != old.Id && o.OpportunityType == "station_trading");
        Assert.Equal(RecommendationStatus.Planned, fresh.Status);
        Assert.Equal(TypeId, fresh.TypeId);
        Assert.Equal(RegionId, fresh.BuyRegionId);
        Assert.True(fresh.ExpiresAt > DateTime.UtcNow);

        Assert.Single(result, o => o.Id == fresh.Id);
    }

    [Fact]
    public async Task Analyze_ExpiredOpportunity_NoLongerActionable_IsNotInvalidated()
    {
        using var db = TestDb.Create();
        // Keine Orders für die Region → der Kandidat ist heute nicht belastbar;
        // die Altzeile darf dadurch trotzdem nicht von "planned" nach "invalid"
        // wandern (sie ist bereits terminal abgelaufen).
        var cache = new FakeRegionalMarketCache();

        var old = ExpiredStationTrade(Now.AddHours(-5), Now.AddHours(-3));
        db.TradingOpportunities.Add(old);
        await db.SaveChangesAsync();

        var service = CreateService(db, cache);
        var result = await service.AnalyzeStationTradesAsync();

        var reloaded = await db.TradingOpportunities.AsNoTracking().SingleAsync(o => o.Id == old.Id);
        Assert.Equal(RecommendationStatus.Expired, reloaded.Status);
        Assert.Equal(Now.AddHours(-3), reloaded.ExpiresAt);

        var changes = await db.TradeStatusChanges.AsNoTracking()
            .Where(c => c.TradingOpportunityId == old.Id)
            .ToListAsync();
        Assert.DoesNotContain(changes, c => c.ToStatus == RecommendationStatus.Invalid);
        Assert.Empty(result);
    }

    [Fact]
    public async Task Analyze_ActiveOpportunity_WithinLifecycle_IsUpdatedNotDuplicated()
    {
        using var db = TestDb.Create();
        var cache = new FakeRegionalMarketCache();
        cache.AddOrders(RegionId, ActionableOrders());
        db.MarketHistory.AddRange(History());

        var active = ExpiredStationTrade(Now.AddMinutes(-30), Now.AddMinutes(90));
        active.Evidence = "alte Empfehlung (unverändert zu lassen)";
        db.TradingOpportunities.Add(active);
        await db.SaveChangesAsync();

        var service = CreateService(db, cache);
        var result = await service.AnalyzeStationTradesAsync();

        // Nicht abgelaufen → dieselbe Zeile wird aktualisiert (kein Duplikat),
        // nur ihr Zustand bleibt "planned"; kein Ablauf-Historieeintrag.
        var row = Assert.Single(await db.TradingOpportunities.AsNoTracking().ToListAsync());
        Assert.Equal(active.Id, row.Id);
        Assert.Equal(RecommendationStatus.Planned, row.Status);
        Assert.True(row.ExpiresAt > Now.AddMinutes(90));
        Assert.DoesNotContain(await db.TradeStatusChanges.AsNoTracking().ToListAsync(),
            c => c.ToStatus == RecommendationStatus.Expired);
        Assert.Single(result, o => o.Id == active.Id);
    }
}
