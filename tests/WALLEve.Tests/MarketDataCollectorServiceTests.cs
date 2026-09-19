using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using WALLEve.Data;
using WALLEve.Models.Authentication;
using WALLEve.Models.Database;
using WALLEve.Models.Esi.Markets;
using WALLEve.Services.Authentication.Interfaces;
using WALLEve.Services.Market;
using WALLEve.Services.Market.Interfaces;

namespace WALLEve.Tests;

/// <summary>
/// Regressionstests zum Review-Befund an PR #145: Die Owner-Priorisierung der
/// Sammelreihenfolge (AK3) darf denselben Item-Typ nicht mehrfach aufnehmen. Ein Typ,
/// den mehrere Owner favorisieren, erzeugt pro (Region, Typ) genau EINEN
/// MarketSnapshot statt eines Duplikats pro Owner.
/// </summary>
public class MarketDataCollectorServiceTests
{
    private const int RegionJita = 10000002;

    // ------------------------------------------------------------------
    // Fakes
    // ------------------------------------------------------------------

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

    private sealed class FakeMarketDataService : IMarketDataService
    {
        public Task<List<int>> GetTrackedTypeIdsAsync() => Task.FromResult(new List<int>());
        public Task<List<int>> GetTrackedRegionIdsAsync() => Task.FromResult(new List<int>());
        public Task<List<MarketSnapshot>> GetMarketSnapshotsAsync(int typeId, int? regionId = null, DateTime? from = null, DateTime? to = null, int limit = 500)
            => Task.FromResult(new List<MarketSnapshot>());
        public Task<MarketSnapshot?> GetLatestSnapshotAsync(int typeId, int regionId)
            => Task.FromResult<MarketSnapshot?>(null);
        public Task<MarketDataStatistics> GetMarketDataStatisticsAsync()
            => Task.FromResult(new MarketDataStatistics());
        public Task<List<MarketFavorit>?> GetMarketFavoritsAsync(int characterId)
            => Task.FromResult<List<MarketFavorit>?>(null);
        public Task<bool> AddMarketFavoritAsync(MarketFavorit favorit) => Task.FromResult(true);
        public Task<bool> RemoveMarketFavoritAsync(int characterId, int typeId) => Task.FromResult(true);
    }

    private sealed class FakeAuthService : IEveAuthenticationService
    {
        public EveAuthState? State { get; set; }

        public Task<EveAuthState?> GetAuthStateAsync() => Task.FromResult(State);
        public Task<bool> IsAuthenticatedAsync() => Task.FromResult(State?.IsValid == true);
        public string GetLoginUrl() => string.Empty;
        public Task<bool> HandleCallbackAsync(string code, string state) => Task.FromResult(false);
        public Task<string?> GetAccessTokenAsync() => Task.FromResult(State?.AccessToken);
        public Task LogoutAsync() => Task.CompletedTask;
        public Task<List<KnownCharacter>> GetAllCharactersAsync() => Task.FromResult(new List<KnownCharacter>());
        public Task<bool> SwitchCharacterAsync(int characterId) => Task.FromResult(false);
        public Task<bool> ForceRefreshAccessTokenAsync() => Task.FromResult(State?.IsValid == true);
        public event EventHandler<bool>? AuthenticationStateChanged
        {
            add { }
            remove { }
        }
    }

    // ------------------------------------------------------------------
    // Tests
    // ------------------------------------------------------------------

    /// <summary>
    /// Derselbe Typ (42) ist Favorit von Owner 100 (eigener Charakter) UND Owner 200.
    /// Die priorisierte Reihenfolge darf 42 nur EINMAL enthalten — die
    /// Owner-Priorität (eigene zuerst) bleibt dabei erhalten.
    /// </summary>
    [Fact]
    public void BuildPrioritizedTypeIds_DuplicateFavoriteAcrossOwners_EmitsTypeOnce()
    {
        var ownerPriority = new List<(int CharacterId, List<int> TypeIds)>
        {
            (100, new List<int> { 42, 7 }),
            (200, new List<int> { 42 })
        };
        var allTypeIds = new HashSet<int> { 42, 7, 44992 };

        var result = MarketDataCollectorService.BuildPrioritizedTypeIds(ownerPriority, allTypeIds);

        Assert.Equal(new[] { 42, 7 }, result); // 42 genau einmal, eigene Favoriten zuerst
    }

    /// <summary>
    /// Vollständiger Sammellauf mit zwei Ownern, die denselben Typ favorisieren:
    /// Es wird genau EIN MarketSnapshot pro (Region, Typ) persistiert.
    /// Vor der Deduplikation entstand hier eine zweite Zeile pro Owner.
    /// </summary>
    [Fact]
    public async Task CollectMarketData_SameTypeFavoritedByMultipleOwners_StoresOneSnapshotPerRegionAndType()
    {
        var db = TestDb.Create();
        // Favoriten sind per FK an WalletCharacter gebunden — beide Charaktere anlegen.
        db.Characters.AddRange(
            new WalletCharacter { CharacterId = 100, CharacterName = "Alpha" },
            new WalletCharacter { CharacterId = 200, CharacterName = "Beta" });
        db.MarketFavorits.AddRange(
            new MarketFavorit { CharacterId = 100, TypeId = 42, Name = "Geteilter Favorit" },
            new MarketFavorit { CharacterId = 200, TypeId = 42, Name = "Geteilter Favorit" });
        await db.SaveChangesAsync();

        var cache = new FakeRegionalMarketCache();
        cache.AddOrders(RegionJita,
            new RegionalMarketOrder { OrderId = 1, TypeId = 42, IsBuyOrder = false, Price = 100, VolumeRemain = 10, SystemId = 30000142, LocationId = 60003760 },
            new RegionalMarketOrder { OrderId = 2, TypeId = 42, IsBuyOrder = true, Price = 90, VolumeRemain = 5, SystemId = 30000142, LocationId = 60003760 });

        var services = new ServiceCollection();
        services.AddSingleton<WalletDbContext>(db);
        services.AddScoped<IRegionalMarketCacheService>(_ => cache);
        services.AddScoped<IMarketDataService>(_ => new FakeMarketDataService());
        services.AddScoped<IEveAuthenticationService>(_ => new FakeAuthService
        {
            State = new EveAuthState { AccessToken = "access", RefreshToken = "refresh", CharacterId = 100 }
        });
        services.AddLogging();
        using var provider = services.BuildServiceProvider();

        var collector = new MarketDataCollectorService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<MarketDataCollectorService>.Instance);

        await collector.CollectMarketDataAsync(CancellationToken.None);

        var snapshots = await db.MarketSnapshots
            .Where(s => s.RegionId == RegionJita && s.TypeId == 42)
            .ToListAsync();

        Assert.Single(snapshots);
    }

    [Fact]
    public async Task CollectMarketData_MinedType_StoresRegionalQuoteForActiveCharacter()
    {
        var db = TestDb.Create();
        db.MiningLedgerEntries.Add(new Models.Mining.MiningLedgerEntry
        {
            CharacterId = 100, Date = DateTime.UtcNow.Date, TypeId = 777,
            SolarSystemId = 30000142, Quantity = 10, UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var cache = new FakeRegionalMarketCache();
        cache.AddOrders(RegionJita,
            new RegionalMarketOrder { OrderId = 7, TypeId = 777, IsBuyOrder = false, Price = 123, VolumeRemain = 1, SystemId = 30000142, LocationId = 60003760 });
        var services = new ServiceCollection();
        services.AddSingleton<WalletDbContext>(db);
        services.AddScoped<IRegionalMarketCacheService>(_ => cache);
        services.AddScoped<IMarketDataService>(_ => new FakeMarketDataService());
        services.AddScoped<IEveAuthenticationService>(_ => new FakeAuthService { State = new EveAuthState { AccessToken = "access", RefreshToken = "refresh", CharacterId = 100 } });
        services.AddLogging();
        using var provider = services.BuildServiceProvider();

        var collector = new MarketDataCollectorService(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<MarketDataCollectorService>.Instance);
        await collector.CollectMarketDataAsync(CancellationToken.None);

        var snapshot = await db.MarketSnapshots.SingleAsync(s => s.RegionId == RegionJita && s.TypeId == 777);
        Assert.Equal(123, snapshot.BestSellPrice);
    }
}