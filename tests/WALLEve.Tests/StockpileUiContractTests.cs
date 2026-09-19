using WALLEve.Data;
using WALLEve.Models.Database;
using WALLEve.Models.Esi.Markets;
using WALLEve.Models.Holdings;
using WALLEve.Models.Market;
using WALLEve.Models.Sde;
using WALLEve.Models.Stockpiles;
using WALLEve.Services.Market.Interfaces;
using WALLEve.Services.Sde.Interfaces;
using WALLEve.Services.Stockpiles;

namespace WALLEve.Tests;

/// <summary>
/// Regressionstests für Issue #53 (Stockpile-UI-Verträge):
/// - Bulk-Zielpflege: Parser/Validierung je Zeile, keine Duplikate, keine ungültigen Mengen.
/// - Darstellung: unvollständige Bestandsdaten erscheinen NIE als Nullbestand („—"/Hinweis).
/// - Übersichts-Service: Owner-Isolation (Owner-Wechsel zeigt keine fremden Ziele),
///   Archiv-Filter, Quellen-/Freshness-Metadaten.
/// Alles deterministisch ohne Live-ESI.
/// </summary>
public class StockpileUiContractTests
{
    private const int OwnerA = 90073315;
    private const int OwnerB = 99988877;

    // ---- Bulk-Parser: gültige Eingaben ----

    [Fact]
    public void BulkParse_AcceptsTypeIdLines_AndSkipsCommentsAndEmptyLines()
    {
        var result = StockpileBulkParser.Parse(
            "# Kommentar\n\n34;100\n582;50;60003760;Lager im Jita-4-4\n");

        Assert.False(result.HasErrors);
        Assert.Equal(2, result.Entries.Count);
        Assert.Equal(34, result.Entries[0].TypeId);
        Assert.Equal(100, result.Entries[0].Quantity);
        Assert.Null(result.Entries[0].LocationId);
        Assert.Equal(3, result.Entries[0].LineNumber);
        Assert.Equal(582, result.Entries[1].TypeId);
        Assert.Equal(50, result.Entries[1].Quantity);
        Assert.Equal(60003760, result.Entries[1].LocationId);
        Assert.Equal("Lager im Jita-4-4", result.Entries[1].Note);
        Assert.Equal(4, result.Entries[1].LineNumber);
    }

    [Fact]
    public void BulkParse_ResolvesItemNames_CaseInsensitive()
    {
        var names = new Dictionary<int, string> { { 34, "Tritanium" }, { 582, "Veldspar" } };
        int? Resolve(string name)
        {
            foreach (var kv in names)
            {
                if (kv.Value.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return kv.Key;
                }
            }
            return null;
        }

        var result = StockpileBulkParser.Parse("tritanium;250", Resolve);

        Assert.False(result.HasErrors);
        var entry = Assert.Single(result.Entries);
        Assert.Equal(34, entry.TypeId);
        Assert.Equal(250, entry.Quantity);
    }

    // ---- Bulk-Parser: Fehler je Zeile ----

    [Fact]
    public void BulkParse_FlagsInvalidQuantity_PerLine()
    {
        var result = StockpileBulkParser.Parse("34;0\n35;-5\n36;abc");

        Assert.Empty(result.Entries);
        Assert.Equal(3, result.Errors.Count);
        Assert.All(result.Errors, e => Assert.Contains("Zielmenge", e.Message));
    }

    [Fact]
    public void BulkParse_FlagsUnknownItem()
    {
        var result = StockpileBulkParser.Parse("NichtexistentesItem;10", _ => null);

        var error = Assert.Single(result.Errors);
        Assert.Equal(1, error.LineNumber);
        Assert.Contains("Unbekanntes Item", error.Message);
    }

    [Fact]
    public void BulkParse_FlagsDuplicateWithinText()
    {
        var result = StockpileBulkParser.Parse("34;100\n34;200");

        Assert.Single(result.Entries);
        var error = Assert.Single(result.Errors);
        Assert.Equal(2, error.LineNumber);
        Assert.Contains("doppelt", error.Message);
    }

    [Fact]
    public void BulkParse_FlagsExistingActiveTarget_SameOwnerScopeRule()
    {
        var existingA = new List<StockpileTarget>
        {
            new() { Id = 1, OwnerType = OwnerType.Character, OwnerId = OwnerA, TypeId = 34, Quantity = 100, IsArchived = false }
        };

        // Gleicher Type + gleicher Owner → wie der Duplikat-Guard des StockpileService (#36).
        var result = StockpileBulkParser.Parse("34;300", existingTargets: existingA, ownerType: OwnerType.Character, ownerId: OwnerA);

        // Gleicher Type, ANDERER Owner → kein Konflikt für den aktuellen Owner.
        var foreignTargets = new List<StockpileTarget>
        {
            new() { Id = 2, OwnerType = OwnerType.Character, OwnerId = OwnerB, TypeId = 34, Quantity = 100, IsArchived = false }
        };
        var foreign = StockpileBulkParser.Parse("34;300", existingTargets: foreignTargets, ownerType: OwnerType.Character, ownerId: OwnerA);

        Assert.Empty(result.Entries);
        Assert.Contains("bereits ein aktives Ziel", Assert.Single(result.Errors).Message);
        Assert.False(foreign.HasErrors);
        _ = Assert.Single(foreign.Entries);
    }

    [Fact]
    public void BulkParse_FlagsTooLongNote_AndTooManyFields()
    {
        var result = StockpileBulkParser.Parse($"34;100;;{new string('x', 501)}\n35;10;1;x;y");

        Assert.Empty(result.Entries);
        Assert.Equal(2, result.Errors.Count);
        Assert.Contains("500 Zeichen", result.Errors[0].Message);
        Assert.Contains("zu viele Felder", result.Errors[1].Message);
    }

    [Fact]
    public void BulkParse_AcceptsQuantityAboveIntMaxValue()
    {
        // 64-Bit: Zielmengen oberhalb int.MaxValue (2.147.483.647) müssen korrekt parsen und speichern (#183).
        const string bigQuantity = "5000000000";
        var result = StockpileBulkParser.Parse($"34;{bigQuantity}");

        Assert.False(result.HasErrors);
        var entry = Assert.Single(result.Entries);
        Assert.Equal(34, entry.TypeId);
        Assert.Equal(5_000_000_000L, entry.Quantity);
    }

    [Fact]
    public void BulkParse_AcceptsMaxLongQuantity()
    {
        // Grenzwert: eine Menge knapp unter long.MaxValue (9.223.372.036.854.775.807).
        // Der Parser nutzt long.TryParse, das diesen Wert korrekt akzeptiert.
        const string nearMax = "9223372036854775000";
        var result = StockpileBulkParser.Parse($"34;{nearMax}");

        Assert.False(result.HasErrors);
        var entry = Assert.Single(result.Entries);
        Assert.Equal(9_223_372_036_854_775_000L, entry.Quantity);
    }

    [Fact]
    public void BulkParse_RejectsOverflowQuantity()
    {
        // long.MaxValue + 1: muss als ungültige Menge abgewiesen werden.
        var result = StockpileBulkParser.Parse("34;9223372036854775808");

        Assert.Empty(result.Entries);
        var error = Assert.Single(result.Errors);
        Assert.Contains("Zielmenge", error.Message);
    }

    [Fact]
    public void BulkParse_AcceptsMaxIntPlusOne()
    {
        // int.MaxValue + 1 = 2.147.483.648 → gültige long-Menge, muss akzeptiert werden.
        var result = StockpileBulkParser.Parse("34;2147483648");

        Assert.False(result.HasErrors);
        var entry = Assert.Single(result.Entries);
        Assert.Equal(34, entry.TypeId);
        Assert.Equal(2_147_483_648L, entry.Quantity);
    }

    // ---- Darstellung: nie Nullbestand bei unvollständigen Daten ----

    [Fact]
    public void Display_NullValues_NeverRenderAsZero()
    {
        Assert.Equal("—", StockpileValueDisplay.Format(null));
        Assert.NotEqual("0", StockpileValueDisplay.Format(null));
        Assert.False(StockpileValueDisplay.IsTrustworthy(null));
        Assert.True(StockpileValueDisplay.IsTrustworthy(0));
        Assert.Equal("1.234", StockpileValueDisplay.Format(1234));
    }

    [Fact]
    public void Display_PartialReason_IsHumanReadable()
    {
        Assert.Contains("Bestandsquelle fehlt", StockpileValueDisplay.DescribePartialReason("physical-source-missing"));
        Assert.Contains("nicht abgedeckt", StockpileValueDisplay.DescribePartialReason("scope-not-covered"));
        Assert.Contains("Order-Quelle", StockpileValueDisplay.DescribePartialReason("orders-source-missing"));
        Assert.Contains("Unvollständige Datenquelle", StockpileValueDisplay.DescribePartialReason("irgendwas"));
    }

    // ---- Übersichts-Service: Owner-Isolation, Archiv, Freshness ----

    private static async Task SeedSnapshotAsync(WalletDbContext db, int ownerId, int typeId, int quantity, DateTime syncedAt)
    {
        var run = new HoldingSyncRun
        {
            OwnerType = OwnerType.Character,
            OwnerId = ownerId,
            StartedAt = syncedAt.AddMinutes(-5),
            Status = "completed",
            CompletedAt = syncedAt
        };
        var snapshot = new HoldingSnapshot
        {
            OwnerType = OwnerType.Character,
            OwnerId = ownerId,
            SyncedAt = syncedAt,
            Source = "esi"
        };
        snapshot.Items.Add(new HoldingItem
        {
            ItemId = ownerId * 1000 + typeId, TypeId = typeId, Quantity = quantity, LocationId = 60003760, LocationFlag = "Hangar"
        });
        run.Snapshots.Add(snapshot);
        db.HoldingSyncRuns.Add(run);
        await db.SaveChangesAsync();
    }

    private static async Task<StockpileTarget> CreateTargetAsync(WalletDbContext db, int ownerId, int typeId, int quantity, bool archived = false)
    {
        var service = new StockpileService(db);
        return await service.CreateAsync(new StockpileTarget
        {
            OwnerType = OwnerType.Character,
            OwnerId = ownerId,
            TypeId = typeId,
            Quantity = quantity,
            IsArchived = archived
        });
    }

    [Fact]
    public async Task Overview_OwnerSwitch_ShowsOnlyOwnTargets()
    {
        await using var db = TestDb.Create();
        await SeedSnapshotAsync(db, OwnerA, 34, 120, DateTime.UtcNow.AddMinutes(-10));
        await SeedSnapshotAsync(db, OwnerB, 34, 9999, DateTime.UtcNow);

        await CreateTargetAsync(db, OwnerA, 34, 300);
        await CreateTargetAsync(db, OwnerA, 35, 50);
        await CreateTargetAsync(db, OwnerB, 34, 70);

        var service = CreateOverviewService(db);

        var overviewA = await service.GetOverviewAsync(OwnerType.Character, OwnerA, orders: new List<MarketOrder>(), ordersAvailable: true);
        var overviewB = await service.GetOverviewAsync(OwnerType.Character, OwnerB, orders: new List<MarketOrder>(), ordersAvailable: true);

        // Owner-Wechsel: B sieht NUR B's Ziele — kein fremdes Ziel, keine fremden Bestände.
        Assert.Equal(2, overviewA.Lines.Count);
        Assert.Equal(new HashSet<int> { 34, 35 }, overviewA.Lines.Select(l => l.TypeId).ToHashSet());
        Assert.Equal(120, overviewA.Lines.Single(l => l.TypeId == 34).Physical);
        var lineB = Assert.Single(overviewB.Lines);
        Assert.Equal(34, lineB.TypeId);
        Assert.Equal(70, lineB.TargetQuantity);
        Assert.Equal(9999, lineB.Physical); // fremder Bestand von A fließt nicht ein
        Assert.Equal(70, lineB.TargetQuantity);
    }

    [Fact]
    public async Task Overview_ArchivedTargets_HiddenByDefault_IncludedOnRequest()
    {
        await using var db = TestDb.Create();
        await SeedSnapshotAsync(db, OwnerA, 34, 120, DateTime.UtcNow);
        await CreateTargetAsync(db, OwnerA, 34, 300);
        await CreateTargetAsync(db, OwnerA, 35, 50, archived: true);

        var service = CreateOverviewService(db);

        var active = await service.GetOverviewAsync(OwnerType.Character, OwnerA, orders: new List<MarketOrder>(), ordersAvailable: true);
        var withArchived = await service.GetOverviewAsync(OwnerType.Character, OwnerA, includeArchived: true, orders: new List<MarketOrder>(), ordersAvailable: true);

        Assert.Single(active.Lines);
        Assert.Equal(2, withArchived.Lines.Count);
        Assert.True(withArchived.Lines.Single(l => l.TypeId == 35).IsArchived);
    }

    [Fact]
    public async Task Overview_MissingSnapshot_IsPartialAndNeverZero()
    {
        await using var db = TestDb.Create();
        await CreateTargetAsync(db, OwnerA, 34, 300);

        var service = CreateOverviewService(db);
        var overview = await service.GetOverviewAsync(OwnerType.Character, OwnerA, orders: null, ordersAvailable: false);

        Assert.False(overview.PhysicalSourceAvailable);
        Assert.Null(overview.PhysicalSourceSyncedAt);
        Assert.False(overview.OrdersSourceAvailable);

        var line = Assert.Single(overview.Lines);
        Assert.True(line.IsPartial);
        Assert.Null(line.Physical);      // kein 0-Bestand
        Assert.Null(line.Inbound);       // kein 0
        Assert.Null(line.Bound);         // kein 0
        Assert.Null(line.Shortage);      // keine Schein-Deckung
        Assert.Equal("physical-source-missing", line.PartialReason);
        Assert.True(overview.HasPartialLines);
    }

    [Fact]
    public async Task Overview_ExposesPhysicalFreshness()
    {
        await using var db = TestDb.Create();
        var syncedAt = new DateTime(2026, 9, 13, 10, 0, 0, DateTimeKind.Utc);
        await SeedSnapshotAsync(db, OwnerA, 34, 120, syncedAt);
        await CreateTargetAsync(db, OwnerA, 34, 300);

        var service = CreateOverviewService(db);
        var overview = await service.GetOverviewAsync(OwnerType.Character, OwnerA, orders: null, ordersAvailable: false);

        Assert.True(overview.PhysicalSourceAvailable);
        Assert.Equal(syncedAt, overview.PhysicalSourceSyncedAt);
        // Ohne Order-Quelle bleibt die Zeile partial, aber der Bestand selbst ist belastbar.
        var line = Assert.Single(overview.Lines);
        Assert.Equal(120, line.Physical);
        Assert.True(line.IsPartial);
        Assert.Equal("orders-source-missing", line.PartialReason);
    }

    private static StockpileOverviewService CreateOverviewService(WalletDbContext db)
        => new(
            db,
            new StockpileCalculationService(db, new StockpileService(db)),
            new FakeHubSelectionService(),
            new FakeSdeUniverseService());

    private sealed class FakeHubSelectionService : IHubSelectionService
    {
        public MarketHubProfile? ComparisonMarket { get; set; }

        public Task<List<MarketHubProfile>> GetProfilesAsync(CancellationToken ct = default)
            => Task.FromResult(new List<MarketHubProfile>());
        public Task<MarketHubProfile?> GetComparisonMarketAsync(CancellationToken ct = default)
            => Task.FromResult(ComparisonMarket);
        public Task SaveProfileAsync(MarketHubProfile profile, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteProfileAsync(int profileId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<HubSelectionResult> SelectNearestActiveHubAsync(int fromSystemId, CancellationToken ct = default)
            => Task.FromResult(new HubSelectionResult { GraphAvailable = true });
    }

    private sealed class FakeSdeUniverseService : ISdeUniverseService
    {
        public Task<bool> IsDatabaseAvailableAsync() => Task.FromResult(true);
        public Task<string?> GetTypeNameAsync(int typeId) => Task.FromResult<string?>(null);
        public Task<string?> GetTypeGroupAsync(int typeId) => Task.FromResult<string?>(null);
        public Task<Dictionary<int, string?>> GetTypeGroupsAsync(IReadOnlyCollection<int> typeIds) => Task.FromResult(new Dictionary<int, string?>());
        public Task<Dictionary<int, string?>> GetTypeNamesAsync(IReadOnlyCollection<int> typeIds) => Task.FromResult(new Dictionary<int, string?>());
        public Task<Dictionary<int, SolarSystemInfo?>> GetSolarSystemsAsync(IReadOnlyCollection<int> solarSystemIds) => Task.FromResult(new Dictionary<int, SolarSystemInfo?>());
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
}