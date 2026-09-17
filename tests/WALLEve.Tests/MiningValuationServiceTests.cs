using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WALLEve.Data;
using WALLEve.Models.Database;
using WALLEve.Models.Mining;
using WALLEve.Models.Sde;
using WALLEve.Services.Mining;
using WALLEve.Services.Sde.Interfaces;

namespace WALLEve.Tests;

/// <summary>
/// Regression-Tests für die Mining-Auswertung (#48): Gruppierung nach Zeitraum/
/// Erztyp/System, Marktbewertung am neuesten Snapshot der Region (Quelle/Alter),
/// Erhalt unbekannter Preise/Orte, Datumsgrenzen, Owner-Isolation und die
/// Zusicherung, dass die Auswertung strikt lesend ist (keine doppelte Übernahme
/// in Bestand oder Kostenbasis).
/// </summary>
public class MiningValuationServiceTests
{
    private const int CharacterA = 90073315;
    private const int CharacterB = 90073316;

    private const int RegionJita = 10000002;

    private const int VeldsparId = 1230;
    private const int ScorditeId = 1228;

    private const int SystemA = 30000001;
    private const int SystemB = 30000002;

    private sealed class FakeSde : ISdeUniverseService
    {
        public Dictionary<int, string?> TypeNames { get; } = new();
        public Dictionary<int, string?> SystemNames { get; } = new();
        public Dictionary<int, string?> RegionNames { get; } = new();

        public Task<bool> IsDatabaseAvailableAsync() => Task.FromResult(true);
        public Task<string?> GetTypeNameAsync(int typeId)
            => Task.FromResult(TypeNames.TryGetValue(typeId, out var n) ? n : null);
        public Task<string?> GetTypeGroupAsync(int typeId) => Task.FromResult<string?>(null);
        public Task<Dictionary<int, string?>> GetTypeGroupsAsync(IReadOnlyCollection<int> typeIds)
            => Task.FromResult(typeIds.Distinct().Where(t => TypeNames.ContainsKey(t))
                .ToDictionary(t => t, t => TypeNames[t]));
        public Task<SolarSystemInfo?> GetSolarSystemAsync(int solarSystemId)
            => Task.FromResult(SystemNames.TryGetValue(solarSystemId, out var n)
                ? new SolarSystemInfo { SolarSystemId = solarSystemId, Name = n ?? string.Empty }
                : null);
        public Task<StationInfo?> GetStationAsync(long stationId) => Task.FromResult<StationInfo?>(null);
        public Task<string?> GetRegionNameAsync(int regionId)
            => Task.FromResult(RegionNames.TryGetValue(regionId, out var n) ? n : null);
        public Task<string?> GetLocationNameAsync(long locationId) => Task.FromResult<string?>(null);
        public Task<int?> GetRegionIdForLocationAsync(long locationId) => Task.FromResult<int?>(null);
        public Task<int?> GetSolarSystemIdForLocationAsync(long locationId) => Task.FromResult<int?>(null);
        public Task<Dictionary<int, string>> GetAllMarketItemsAsync() => Task.FromResult(new Dictionary<int, string>());
        public Task<Dictionary<int, string>> GetAllRegionsAsync() => Task.FromResult(new Dictionary<int, string>());
        public Task<Dictionary<int, string>> SearchSolarSystemsAsync(string searchQuery, int maxResults = 10)
            => Task.FromResult(new Dictionary<int, string>());
    }

    private static MiningValuationService CreateService(WalletDbContext db, FakeSde? sde = null)
        => new(db, sde ?? new FakeSde(), NullLogger<MiningValuationService>.Instance);

    private static void AddLedgerEntry(WalletDbContext db, int characterId, DateTime date, int typeId, int systemId, long quantity)
    {
        db.MiningLedgerEntries.Add(new Models.Mining.MiningLedgerEntry
        {
            CharacterId = characterId,
            Date = date,
            TypeId = typeId,
            SolarSystemId = systemId,
            Quantity = quantity,
            UpdatedAt = DateTime.UtcNow
        });
    }

    private static MiningValuationFilter Filter(DateTime from, DateTime to, bool groupBySystem = false, int regionId = RegionJita)
        => new() { From = from, To = to, RegionId = regionId, GroupBySolarSystem = groupBySystem };

    [Fact]
    public async Task GetReport_GroupsByType_SumsQuantitiesAcrossDays()
    {
        var db = TestDb.Create();
        AddLedgerEntry(db, CharacterA, new DateTime(2026, 9, 10), VeldsparId, SystemA, 1000);
        AddLedgerEntry(db, CharacterA, new DateTime(2026, 9, 11), VeldsparId, SystemA, 500);
        AddLedgerEntry(db, CharacterA, new DateTime(2026, 9, 11), ScorditeId, SystemB, 700);
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var report = await service.GetReportAsync(CharacterA,
            Filter(new DateTime(2026, 9, 1), new DateTime(2026, 10, 1)));

        Assert.Equal(2, report.Rows.Count);
        var veldspar = report.Rows.Single(r => r.TypeId == VeldsparId);
        Assert.Equal(1500, veldspar.Quantity); // 1000 + 500 über zwei Tage gruppiert
        Assert.Equal(2200, report.TotalQuantity);
    }

    [Fact]
    public async Task GetReport_DateBoundaries_FromInclusiveToExclusive()
    {
        var db = TestDb.Create();
        AddLedgerEntry(db, CharacterA, new DateTime(2026, 9, 15), VeldsparId, SystemA, 100); // From
        AddLedgerEntry(db, CharacterA, new DateTime(2026, 9, 16), ScorditeId, SystemB, 200); // letzter Tag
        AddLedgerEntry(db, CharacterA, new DateTime(2026, 9, 17), VeldsparId, SystemA, 300); // To (exklusiv)
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var report = await service.GetReportAsync(CharacterA,
            Filter(new DateTime(2026, 9, 15), new DateTime(2026, 9, 17)));

        // 15. (inkl.) und 16. (vor To) enthalten; 17. (To exklusiv) nicht.
        Assert.Equal(2, report.Rows.Count);
        Assert.Equal(300, report.TotalQuantity);
        Assert.DoesNotContain(report.Rows, r => r.Quantity == 300);
    }

    [Fact]
    public async Task GetReport_OwnerIsolation_OnlyCharactersOwnLedger()
    {
        var db = TestDb.Create();
        AddLedgerEntry(db, CharacterA, new DateTime(2026, 9, 10), VeldsparId, SystemA, 1000);
        AddLedgerEntry(db, CharacterB, new DateTime(2026, 9, 10), VeldsparId, SystemB, 9999);
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var reportA = await service.GetReportAsync(CharacterA,
            Filter(new DateTime(2026, 9, 1), new DateTime(2026, 10, 1)));

        Assert.Single(reportA.Rows);
        Assert.Equal(1000, reportA.TotalQuantity);
        // B-Zeile (9999) taucht in A-Auswertung nie auf.
        Assert.DoesNotContain(reportA.Rows, r => r.Quantity == 9999);
    }

    [Fact]
    public async Task GetReport_UnknownPrice_PreservedAsUnknownNeverZero()
    {
        var db = TestDb.Create();
        AddLedgerEntry(db, CharacterA, new DateTime(2026, 9, 10), VeldsparId, SystemA, 1000);
        await db.SaveChangesAsync();
        // Kein MarketSnapshot in der Region -> Preis unbekannt.

        var service = CreateService(db);
        var report = await service.GetReportAsync(CharacterA,
            Filter(new DateTime(2026, 9, 1), new DateTime(2026, 10, 1)));

        var row = Assert.Single(report.Rows);
        Assert.False(row.HasValuation);
        Assert.Null(row.UnitPrice);
        Assert.Null(row.Value);
        Assert.Null(row.QuoteTimestamp);
        Assert.Equal(1000, row.Quantity); // Menge bleibt trotzdem sichtbar
        Assert.Equal(1, report.UnknownCount);
        Assert.Equal(0, report.KnownCount);
        Assert.Null(report.TotalValue);
    }

    [Fact]
    public async Task GetReport_UnknownNames_PreservedAsNull()
    {
        var db = TestDb.Create();
        AddLedgerEntry(db, CharacterA, new DateTime(2026, 9, 10), VeldsparId, SystemA, 1000);
        await db.SaveChangesAsync();
        // FakeSde ohne Namen -> Typ/System/Region bleiben unbekannt (null), kein Crash.

        var service = CreateService(db);
        var report = await service.GetReportAsync(CharacterA,
            Filter(new DateTime(2026, 9, 1), new DateTime(2026, 10, 1), groupBySystem: true));

        var row = Assert.Single(report.Rows);
        Assert.Null(row.TypeName);
        Assert.NotNull(row.SolarSystemId); // Systemdimension bleibt (Id) erhalten
        Assert.Null(row.SystemName);
        Assert.Null(report.RegionName);
        Assert.Equal(1000, row.Quantity);
    }

    [Fact]
    public async Task GetReport_Valuation_UsesNewestSnapshotOfRegion()
    {
        var db = TestDb.Create();
        AddLedgerEntry(db, CharacterA, new DateTime(2026, 9, 10), VeldsparId, SystemA, 1000);
        await db.SaveChangesAsync();

        db.MarketSnapshots.AddRange(
            new MarketSnapshot { RegionId = RegionJita, TypeId = VeldsparId, Timestamp = new DateTime(2026, 9, 1, 8, 0, 0), BestSellPrice = 10.0 },
            new MarketSnapshot { RegionId = RegionJita, TypeId = VeldsparId, Timestamp = new DateTime(2026, 9, 12, 8, 0, 0), BestSellPrice = 12.5 },
            new MarketSnapshot { RegionId = 10000043, TypeId = VeldsparId, Timestamp = new DateTime(2026, 9, 13, 8, 0, 0), BestSellPrice = 99.0 }); // fremde Region
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var report = await service.GetReportAsync(CharacterA,
            Filter(new DateTime(2026, 9, 1), new DateTime(2026, 10, 1)));

        var row = Assert.Single(report.Rows);
        Assert.True(row.HasValuation);
        Assert.Equal(12.5, row.UnitPrice); // neuester Snapshot der Region, nicht der ältere
        Assert.Equal(12500, row.Value); // 1000 × 12.5
        Assert.Equal(new DateTime(2026, 9, 12, 8, 0, 0), row.QuoteTimestamp); // Quelle/Alter
        Assert.Equal(1, report.KnownCount);
        Assert.Equal(0, report.UnknownCount);
        Assert.Equal(12500, report.TotalValue);
    }

    [Fact]
    public async Task GetReport_GroupBySolarSystem_SplitsRowsPerSystem()
    {
        var db = TestDb.Create();
        // Gleicher Typ an zwei Systemen -> zwei Zeilen bei aktivierter Systemdimension.
        AddLedgerEntry(db, CharacterA, new DateTime(2026, 9, 10), VeldsparId, SystemA, 1000);
        AddLedgerEntry(db, CharacterA, new DateTime(2026, 9, 10), VeldsparId, SystemB, 400);
        AddLedgerEntry(db, CharacterA, new DateTime(2026, 9, 10), ScorditeId, SystemA, 300);
        await db.SaveChangesAsync();

        var service = CreateService(db);

        var grouped = await service.GetReportAsync(CharacterA,
            Filter(new DateTime(2026, 9, 1), new DateTime(2026, 10, 1), groupBySystem: true));
        Assert.Equal(3, grouped.Rows.Count);
        Assert.Equal(2, grouped.Rows.Count(r => r.TypeId == VeldsparId));

        // Ohne Systemdimension wird über Systeme aggregiert -> 2 Zeilen.
        var ungrouped = await service.GetReportAsync(CharacterA,
            Filter(new DateTime(2026, 9, 1), new DateTime(2026, 10, 1), groupBySystem: false));
        Assert.Equal(2, ungrouped.Rows.Count);
        Assert.Equal(1400, ungrouped.Rows.Single(r => r.TypeId == VeldsparId).Quantity);
    }

    [Fact]
    public async Task GetReport_IsReadOnly_NeverBooksInventoryOrCostBasis()
    {
        var db = TestDb.Create();
        AddLedgerEntry(db, CharacterA, new DateTime(2026, 9, 10), VeldsparId, SystemA, 1000);
        db.MarketSnapshots.Add(new MarketSnapshot
        {
            RegionId = RegionJita, TypeId = VeldsparId, Timestamp = DateTime.UtcNow, BestSellPrice = 12.5
        });
        await db.SaveChangesAsync();

        var beforeEntries = await db.MiningLedgerEntries.CountAsync();
        var beforeCostBasis = await db.CostBasisEntries.CountAsync();
        var beforeSnapshots = await db.MarketSnapshots.CountAsync();

        var service = CreateService(db);
        await service.GetReportAsync(CharacterA,
            Filter(new DateTime(2026, 9, 1), new DateTime(2026, 10, 1)));
        await service.GetReportAsync(CharacterA,
            Filter(new DateTime(2026, 9, 1), new DateTime(2026, 10, 1), groupBySystem: true));

        // AK1: Aktivitätsmenge wird nie in Bestand/Kostenbasis übernommen —
        // die Auswertung verändert keine einzige Tabelle (keine doppelte Übernahme).
        Assert.Equal(beforeEntries, await db.MiningLedgerEntries.CountAsync());
        Assert.Equal(beforeCostBasis, await db.CostBasisEntries.CountAsync());
        Assert.Equal(beforeSnapshots, await db.MarketSnapshots.CountAsync());
    }

    [Fact]
    public async Task GetReport_EmptyLedgerOrRange_EmptyReportNoCrash()
    {
        var db = TestDb.Create();
        AddLedgerEntry(db, CharacterA, new DateTime(2026, 9, 10), VeldsparId, SystemA, 100);
        await db.SaveChangesAsync();

        var service = CreateService(db);

        var emptyRange = await service.GetReportAsync(CharacterA,
            Filter(new DateTime(2026, 1, 1), new DateTime(2026, 1, 31)));
        Assert.Empty(emptyRange.Rows);
        Assert.Equal(0, emptyRange.TotalQuantity);
        Assert.Null(emptyRange.TotalValue);

        var noData = await service.GetReportAsync(CharacterB,
            Filter(new DateTime(2026, 9, 1), new DateTime(2026, 10, 1)));
        Assert.Empty(noData.Rows);
        Assert.Equal(0, noData.TotalQuantity);
    }
}