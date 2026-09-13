using System.Linq;
using Microsoft.EntityFrameworkCore;
using WALLEve.Data;
using WALLEve.Models.Esi.Character;
using WALLEve.Models.Holdings;
using WALLEve.Models.Sde;
using WALLEve.Services.Holdings;
using WALLEve.Services.Holdings.Interfaces;
using WALLEve.Services.Sde.Interfaces;

namespace WALLEve.Tests;

/// <summary>
/// Regression-Tests für die aggregierte Holdings-Sicht (#57): Type- und
/// Type/Location-Projektionen mit Owner, Qualität und Freshness sowie der
/// Ortsbaum mit Drill-down auf die Rohdimensionen. Akzeptanzkriterien:
/// Aggregate sind mengengleich mit dem Snapshot, unbekannte Orte sind separat
/// sichtbar, zwei Owner vermischen sich weder im Baum noch in Summen.
/// Deterministisch: nur Fakes und In-Memory-DB, keine Live-ESI-/SDE-Zugriffe.
/// </summary>
public class HoldingsAggregateServiceTests
{
    private const int CharacterA = 90073315;
    private const int CharacterB = 90073316;
    private const long JitaStation = 60003760;
    private const int Tritanium = 34;
    private const int Pyerite = 35;
    private const long ContainerOne = 9001;
    private const long ContainerTwo = 9002;

    // ---------------------------------------------------------------- Fakes

    private sealed class FakeSde : ISdeUniverseService
    {
        public Dictionary<int, string> TypeNames { get; } = new();

        public Task<bool> IsDatabaseAvailableAsync() => Task.FromResult(true);
        public Task<string?> GetTypeNameAsync(int typeId)
            => Task.FromResult(TypeNames.TryGetValue(typeId, out var name) ? name : null);
        public Task<string?> GetTypeGroupAsync(int typeId) => Task.FromResult<string?>(null);
        public Task<SolarSystemInfo?> GetSolarSystemAsync(int solarSystemId) => Task.FromResult<SolarSystemInfo?>(null);
        public Task<StationInfo?> GetStationAsync(long stationId) => Task.FromResult<StationInfo?>(null);
        public Task<string?> GetRegionNameAsync(int regionId) => Task.FromResult<string?>(null);
        public Task<string?> GetLocationNameAsync(long locationId) => Task.FromResult<string?>(null);
        public Task<int?> GetRegionIdForLocationAsync(long locationId) => Task.FromResult<int?>(null);
        public Task<int?> GetSolarSystemIdForLocationAsync(long locationId) => Task.FromResult<int?>(null);
        public Task<Dictionary<int, string>> GetAllMarketItemsAsync() => Task.FromResult(new Dictionary<int, string>());
        public Task<Dictionary<int, string>> GetAllRegionsAsync() => Task.FromResult(new Dictionary<int, string>());
        public Task<Dictionary<int, string>> SearchSolarSystemsAsync(string searchQuery, int maxResults = 10)
            => Task.FromResult(new Dictionary<int, string>());
    }

    /// <summary>
    /// Resolver-Fake: nutzt die vorgegebene Auflösung, oder baut ohne weitere
    /// Abhängigkeiten die einfache Direkt-Location je Item (bekannte ID-Bereiche
    /// aufgelöst, unbekannte ehrlich Unresolved).
    /// </summary>
    private sealed class FakeResolver : IHoldingsLocationResolver
    {
        public IReadOnlyList<ResolvedHoldingItem> Resolution { get; set; } = new List<ResolvedHoldingItem>();

        public Task<IReadOnlyList<ResolvedHoldingItem>> ResolveSnapshotAsync(
            IEnumerable<HoldingItem>? items,
            CancellationToken ct = default)
        {
            if (Resolution.Count > 0)
                return Task.FromResult(Resolution);

            var resolved = (items ?? Enumerable.Empty<HoldingItem>()).Select(item =>
            {
                var location = new LocationResolution
                {
                    LocationId = item.LocationId,
                    Kind = LocationChainResolver.Classify(item.LocationId),
                    Name = $"Loc {item.LocationId}"
                };
                var isResolved = location.Kind != LocationKind.Unresolved;
                return new ResolvedHoldingItem
                {
                    Item = item,
                    Location = location,
                    Chain = new[] { location },
                    Anchor = isResolved ? location : null
                };
            }).ToList();
            return Task.FromResult<IReadOnlyList<ResolvedHoldingItem>>(resolved);
        }
    }

    // ---------------------------------------------------------------- Helpers

    private static HoldingItem Item(long itemId, int typeId, long locationId, int quantity, string flag = "Hangar")
        => new()
        {
            ItemId = itemId,
            TypeId = typeId,
            Quantity = quantity,
            IsSingleton = false,
            LocationId = locationId,
            LocationFlag = flag,
            SnapshotId = 1
        };

    private static LocationResolution Station(long stationId = JitaStation)
        => new() { LocationId = stationId, Kind = LocationKind.Station, Name = "Jita IV - Moon 4 - Caldari Navy Assembly Plant" };

    private static LocationResolution Container(long containerId)
        => new() { LocationId = containerId, Kind = LocationKind.Container };

    private static LocationResolution Unresolved(long locationId, string reason)
        => new() { LocationId = locationId, Kind = LocationKind.Unresolved, UnresolvedReason = reason };

    private static ResolvedHoldingItem ResolvedChain(HoldingItem item, params LocationResolution[] chain)
    {
        var anchor = chain.LastOrDefault(c => c.IsResolved);
        return new ResolvedHoldingItem
        {
            Item = item,
            Location = chain[0],
            Chain = chain,
            Anchor = anchor
        };
    }

    /// <summary>Seeds einen abgeschlossenen Snapshots-Lauf mit Items für einen Owner.</summary>
    private static async Task<HoldingSnapshot> SeedCompletedSnapshotAsync(
        WalletDbContext db, int ownerId, params HoldingItem[] items)
    {
        var run = new HoldingSyncRun
        {
            OwnerType = OwnerType.Character,
            OwnerId = ownerId,
            StartedAt = DateTime.UtcNow.AddHours(-3),
            CompletedAt = DateTime.UtcNow.AddHours(-2),
            Status = "completed"
        };
        var snapshot = new HoldingSnapshot
        {
            SyncRun = run,
            OwnerType = OwnerType.Character,
            OwnerId = ownerId,
            SyncedAt = DateTime.UtcNow.AddHours(-2),
            Source = $"test/{ownerId}",
            Items = items.ToList()
        };
        db.HoldingSnapshots.Add(snapshot);
        await db.SaveChangesAsync();
        return snapshot;
    }

    private static HoldingsAggregateService CreateService(WalletDbContext db, FakeResolver resolver, FakeSde sde)
        => new(db, resolver, sde);

    // ---------------------------------------------------------------- Builder

    [Fact]
    public void Builder_Aggregates_AreQuantityEqualToRawItems()
    {
        // Station 60003760: 2 direkte Tritanium-Items; Container 9001 (in Station):
        // 1 Pyerite-Item direkt, Container 9002 (in 9001): weiteres Pyerite-Item.
        var itemsById = new Dictionary<long, HoldingItem>
        {
            [ContainerOne] = Item(ContainerOne, 22, JitaStation, 1, "Hangar"),
            [ContainerTwo] = Item(ContainerTwo, 23, ContainerOne, 1, "Hangar")
        };
        var resolved = new List<ResolvedHoldingItem>
        {
            ResolvedChain(Item(1, Tritanium, JitaStation, 5), Station()),
            ResolvedChain(Item(2, Tritanium, JitaStation, 3), Station()),
            ResolvedChain(Item(3, Pyerite, ContainerOne, 10), Container(ContainerOne), Station()),
            ResolvedChain(Item(4, Pyerite, ContainerTwo, 2), Container(ContainerTwo), Container(ContainerOne), Station())
        };
        var typeNames = new Dictionary<int, string>
        {
            [Tritanium] = "Tritanium",
            [Pyerite] = "Pyerite",
            [22] = "Gemeinschaftskasten"
        };

        var result = HoldingsTreeBuilder.Build(
            OwnerType.Character, CharacterA, 1, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow,
            resolved, typeNames, itemsById);

        // Mengengleich: Gesamtsumme = Summe der Roh-Items.
        Assert.Equal(20, result.TotalQuantity);
        Assert.Equal(4, result.RawItemCount);
        Assert.Equal(4, result.ResolvedItemCount);
        Assert.Equal(0, result.UnknownLocationItemCount);
        Assert.Null(result.UnknownLocations);

        // Type-Projektion.
        Assert.Equal(2, result.TypeAggregates.Count);
        var trit = result.TypeAggregates.Single(t => t.TypeId == Tritanium);
        Assert.Equal(8, trit.TotalQuantity);
        Assert.Equal(2, trit.RawItemCount);
        Assert.Equal(2, trit.ResolvedLocationItemCount);
        Assert.Equal(0, trit.UnknownLocationItemCount);
        var pyer = result.TypeAggregates.Single(t => t.TypeId == Pyerite);
        Assert.Equal(12, pyer.TotalQuantity);

        // Type/Location-Projektion: je (TypeId, direkte Location) eine Zeile.
        Assert.Equal(3, result.TypeLocationAggregates.Count);
        var tritStation = result.TypeLocationAggregates.Single(l => l.TypeId == Tritanium && l.LocationId == JitaStation);
        Assert.Equal(8, tritStation.Quantity);
        Assert.Equal(2, tritStation.RawItemCount);
        var pyerOne = result.TypeLocationAggregates.Single(l => l.TypeId == Pyerite && l.LocationId == ContainerOne);
        Assert.Equal(10, pyerOne.Quantity);
        Assert.Equal(2, pyerOne.ChainLocationIds.Count);
        var pyerTwo = result.TypeLocationAggregates.Single(l => l.TypeId == Pyerite && l.LocationId == ContainerTwo);
        Assert.Equal(2, pyerTwo.Quantity);
        Assert.Equal(new long[] { ContainerTwo, ContainerOne, JitaStation }, pyerTwo.ChainLocationIds);

        // Ortsbaum: eine Wurzel (Station), Container als Kinder, Mengen bottom-up.
        Assert.Single(result.LocationTrees);
        var root = result.LocationTrees[0];
        Assert.Equal(JitaStation, root.LocationId);
        Assert.Equal(20, root.Quantity);
        Assert.Equal(4, root.RawItemCount);
        Assert.Equal(2, root.Items.Count); // direkte Roh-Items an der Station
        Assert.Equal(5 + 3, root.Items.Sum(i => i.Quantity));
        var c1 = Assert.Single(root.Children);
        Assert.Equal(ContainerOne, c1.LocationId);
        Assert.Equal("Gemeinschaftskasten", c1.Name); // Container-Typname aus itemsById/typeNames
        Assert.Equal("Hangar", c1.LocationFlag);
        Assert.Equal(12, c1.Quantity); // 10 direkt + 2 im Kind
        var c1Leaf = Assert.Single(c1.Items);
        Assert.Equal(3, c1Leaf.ItemId);
        Assert.Equal(Pyerite, c1Leaf.TypeId);
        Assert.Equal(10, c1Leaf.Quantity);
        var c2 = Assert.Single(c1.Children);
        Assert.Equal(ContainerTwo, c2.LocationId);
        var c2Leaf = Assert.Single(c2.Items);
        Assert.Equal(4, c2Leaf.ItemId);
        Assert.Equal(2, c2Leaf.Quantity);
        Assert.Equal("Hangar", c2Leaf.LocationFlag);
    }

    [Fact]
    public void Builder_UnknownLocations_AreSeparatelyVisible()
    {
        var itemsById = new Dictionary<long, HoldingItem>
        {
            [ContainerOne] = Item(ContainerOne, 22, 888888, 1, "Hangar")
        };
        var resolved = new List<ResolvedHoldingItem>
        {
            ResolvedChain(Item(1, Tritanium, JitaStation, 5), Station()),
            ResolvedChain(Item(2, Tritanium, 999999, 7), Unresolved(999999, "unknown-location")),
            ResolvedChain(Item(3, Pyerite, ContainerOne, 2), Container(ContainerOne), Unresolved(888888, "missing-parent"))
        };

        var result = HoldingsTreeBuilder.Build(
            OwnerType.Character, CharacterA, 1, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow,
            resolved, new Dictionary<int, string>(), itemsById);

        Assert.Equal(14, result.TotalQuantity);
        Assert.Equal(1, result.ResolvedItemCount);
        Assert.Equal(2, result.UnknownLocationItemCount);
        Assert.Single(result.LocationTrees);
        Assert.Equal(5, result.LocationTrees[0].Quantity);

        // Separater Bucket: nur die unbekannten Mengen, unter „Unbekannte Orte“.
        Assert.NotNull(result.UnknownLocations);
        Assert.Equal(9, result.UnknownLocations!.Quantity);
        Assert.Equal(2, result.UnknownLocations.RawItemCount);
        Assert.Equal(2, result.UnknownLocations.Children.Count);
        var unknown999 = result.UnknownLocations.Children.Single(c => c.LocationId == 999999);
        Assert.Equal(7, unknown999.Quantity);
        Assert.Single(unknown999.Items);
        var unknown888 = result.UnknownLocations.Children.Single(c => c.LocationId == 888888);
        Assert.Equal("missing-parent", unknown888.UnresolvedReason);
        var container = Assert.Single(unknown888.Children);
        Assert.Equal(ContainerOne, container.LocationId);

        // Type-Projektion: Qualität getrennt gezählt.
        var trit = result.TypeAggregates.Single(t => t.TypeId == Tritanium);
        Assert.Equal(12, trit.TotalQuantity);
        Assert.Equal(1, trit.ResolvedLocationItemCount);
        Assert.Equal(1, trit.UnknownLocationItemCount);
        Assert.False(trit.AllLocationsResolved);
        var tritLocation = result.TypeLocationAggregates.Single(l => l.TypeId == Tritanium && l.LocationId == 999999);
        Assert.False(tritLocation.IsResolved);
        Assert.Equal("unknown-location", tritLocation.UnresolvedReason);

        // Kein unbekanntes Item taucht in einem aufgelösten Baum auf.
        Assert.DoesNotContain(result.LocationTrees, t => t.LocationId is 999999 or 888888);
    }

    [Fact]
    public void Builder_Freshness_ReflectsSnapshotAge()
    {
        var resolved = new List<ResolvedHoldingItem>
        {
            ResolvedChain(Item(1, Tritanium, JitaStation, 1), Station())
        };
        var now = new DateTime(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc);

        var fresh = HoldingsTreeBuilder.Build(
            OwnerType.Character, CharacterA, 1, now.AddHours(-1), now, resolved, new Dictionary<int, string>(),
            new Dictionary<long, HoldingItem>());
        Assert.True(fresh.IsFresh);
        Assert.Equal(TimeSpan.FromHours(1), fresh.Age);

        var stale = HoldingsTreeBuilder.Build(
            OwnerType.Character, CharacterA, 2, now.AddDays(-3), now, resolved, new Dictionary<int, string>(),
            new Dictionary<long, HoldingItem>());
        Assert.False(stale.IsFresh);
        Assert.Equal(TimeSpan.FromDays(3), stale.Age);
    }

    // ---------------------------------------------------------------- Service

    [Fact]
    public async Task Service_WithoutCompletedSnapshot_ReturnsNull()
    {
        var db = TestDb.Create();
        var service = CreateService(db, new FakeResolver(), new FakeSde());

        Assert.Null(await service.BuildTreeAsync(OwnerType.Character, CharacterA));

        // Nur ein laufender (nicht abgeschlossener) Lauf genügt nicht.
        var running = new HoldingSyncRun
        {
            OwnerType = OwnerType.Character,
            OwnerId = CharacterA,
            StartedAt = DateTime.UtcNow,
            Status = "running"
        };
        db.HoldingSyncRuns.Add(running);
        db.HoldingSnapshots.Add(new HoldingSnapshot
        {
            SyncRun = running,
            OwnerType = OwnerType.Character,
            OwnerId = CharacterA,
            SyncedAt = DateTime.UtcNow,
            Source = "test",
            Items = new List<HoldingItem> { Item(1, Tritanium, JitaStation, 1) }
        });
        await db.SaveChangesAsync();

        Assert.Null(await service.BuildTreeAsync(OwnerType.Character, CharacterA));
    }

    [Fact]
    public async Task Service_UsesLatestCompletedSnapshotOnly()
    {
        var db = TestDb.Create();
        var service = CreateService(db, new FakeResolver(), new FakeSde());

        var older = await SeedCompletedSnapshotAsync(db, CharacterA,
            Item(1, Tritanium, JitaStation, 5));
        older.SyncedAt = DateTime.UtcNow.AddDays(-3);
        await db.SaveChangesAsync();

        var newer = await SeedCompletedSnapshotAsync(db, CharacterA,
            Item(2, Tritanium, JitaStation, 3),
            Item(3, Pyerite, JitaStation, 4));

        var result = await service.BuildTreeAsync(OwnerType.Character, CharacterA);

        Assert.NotNull(result);
        Assert.True(result!.SyncedAt >= newer.SyncedAt.AddSeconds(-1));
        Assert.Equal(7, result.TotalQuantity);
        Assert.Equal(2, result.RawItemCount);
        Assert.Equal(new long[] { 2, 3 }, result.LocationTrees[0].Items.Select(i => i.ItemId).OrderBy(id => id));
    }

    [Fact]
    public async Task Service_TwoOwners_NeverMixInTreeOrSums()
    {
        var db = TestDb.Create();
        var service = CreateService(db, new FakeResolver(), new FakeSde());

        await SeedCompletedSnapshotAsync(db, CharacterA,
            Item(1, Tritanium, JitaStation, 5),
            Item(2, Tritanium, JitaStation, 3));
        await SeedCompletedSnapshotAsync(db, CharacterB,
            Item(101, Pyerite, JitaStation, 100),
            Item(102, Pyerite, JitaStation, 200),
            Item(103, Pyerite, JitaStation, 300));

        var a = await service.BuildTreeAsync(OwnerType.Character, CharacterA);
        var b = await service.BuildTreeAsync(OwnerType.Character, CharacterB);

        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.Equal(8, a!.TotalQuantity);
        Assert.Equal(2, a.RawItemCount);
        Assert.Equal(600, b!.TotalQuantity);
        Assert.Equal(3, b.RawItemCount);
        Assert.Equal(new long[] { 1, 2 }, a.LocationTrees[0].Items.Select(i => i.ItemId).OrderBy(id => id));
        Assert.Equal(new long[] { 101, 102, 103 }, b.LocationTrees[0].Items.Select(i => i.ItemId).OrderBy(id => id));
        Assert.DoesNotContain(a.LocationTrees[0].Items, i => i.ItemId >= 100);
        Assert.DoesNotContain(b.LocationTrees[0].Items, i => i.ItemId < 100);
        Assert.Single(a.TypeAggregates);
        Assert.Single(b.TypeAggregates);
        Assert.Equal(Tritanium, a.TypeAggregates[0].TypeId);
        Assert.Equal(Pyerite, b.TypeAggregates[0].TypeId);
    }

    [Fact]
    public async Task Service_ContainerNames_ResolvedFromSdeOnce()
    {
        var db = TestDb.Create();
        var items = new List<HoldingItem>
        {
            Item(3, Pyerite, ContainerOne, 10),
            Item(ContainerOne, 22, JitaStation, 1)
        };
        var sde = new FakeSde();
        sde.TypeNames[22] = "Gemeinschaftskasten";
        var resolver = new FakeResolver
        {
            Resolution = new List<ResolvedHoldingItem>
            {
                ResolvedChain(items[0], Container(ContainerOne), Station()),
                ResolvedChain(items[1], Station())
            }
        };
        var service = CreateService(db, resolver, sde);
        await SeedCompletedSnapshotAsync(db, CharacterA, items.ToArray());

        var result = await service.BuildTreeAsync(OwnerType.Character, CharacterA);

        Assert.NotNull(result);
        var root = Assert.Single(result!.LocationTrees);
        var container = Assert.Single(root.Children);
        Assert.Equal(ContainerOne, container.LocationId);
        Assert.Equal("Gemeinschaftskasten", container.Name);
    }
}