using WALLEve.Services.Trading;
using WALLEve.Services.Trading.Interfaces;

namespace WALLEve.Tests;

/// <summary>
/// Tests der validierten Handelsprofil-Speicherung (Issue #44): Upsert je
/// Charakter, Bereichsvalidierung, Owner-Isolation über den Unique-Index.
/// </summary>
public class TradeProfileServiceTests
{
    private const int OwnerA = 1001;
    private const int OwnerB = 1002;

    private static TradeProfileCommand Command(int characterId = OwnerA, string? name = "Mein Profil") => new(
        CharacterId: characterId,
        Name: name ?? "Mein Profil",
        MaxCapital: 1_000_000m,
        MaxCargoVolume: 10_000m,
        MaxJumps: 20,
        AllowHighSec: true,
        AllowLowSec: true,
        AllowNullSec: false,
        MinVolumeM3: 1m,
        MinProfit: 5_000m,
        MinQualityScore: 40);

    [Fact]
    public void Save_And_GetForCharacter_Roundtrips()
    {
        using var db = TestDb.Create();
        var service = new TradeProfileService(db);

        var result = service.Save(Command());
        Assert.True(result.Success);
        Assert.NotNull(result.Profile);

        var loaded = service.GetForCharacter(OwnerA);
        Assert.NotNull(loaded);
        Assert.Equal("Mein Profil", loaded!.Name);
        Assert.Equal(1_000_000m, loaded.MaxCapital);
        Assert.Equal(10_000m, loaded.MaxCargoVolume);
        Assert.Equal(20, loaded.MaxJumps);
        Assert.True(loaded.AllowHighSec);
        Assert.True(loaded.AllowLowSec);
        Assert.False(loaded.AllowNullSec);
        Assert.Equal(1m, loaded.MinVolumeM3);
        Assert.Equal(5_000m, loaded.MinProfit);
        Assert.Equal(40, loaded.MinQualityScore);
    }

    [Fact]
    public void Save_TwiceForSameOwner_UpsertsWithoutDuplicate()
    {
        using var db = TestDb.Create();
        var service = new TradeProfileService(db);

        service.Save(Command(name: "Erst"));
        var second = service.Save(Command(name: "Zweit"));

        Assert.True(second.Success);
        Assert.Equal(1, db.TradeProfiles.Count());
        Assert.Equal("Zweit", db.TradeProfiles.Single().Name);
    }

    [Fact]
    public void Save_InvalidValues_RejectedWithoutPersistence()
    {
        using var db = TestDb.Create();
        var service = new TradeProfileService(db);

        var result = service.Save(Command(characterId: OwnerA, name: "  ") with
        {
            MaxCapital = -1m,
            MaxJumps = -3,
            MinProfit = -100m,
            MinQualityScore = 150,
            AllowHighSec = false,
            AllowLowSec = false,
            AllowNullSec = false
        });

        Assert.False(result.Success);
        Assert.Null(result.Profile);
        Assert.Empty(db.TradeProfiles);
        Assert.Contains(result.ValidationErrors, e => e.Contains("Profilname", StringComparison.Ordinal));
        Assert.Contains(result.ValidationErrors, e => e.Contains("MaxCapital", StringComparison.Ordinal));
        Assert.Contains(result.ValidationErrors, e => e.Contains("MaxJumps", StringComparison.Ordinal));
        Assert.Contains(result.ValidationErrors, e => e.Contains("MinProfit", StringComparison.Ordinal));
        Assert.Contains(result.ValidationErrors, e => e.Contains("MinQualityScore", StringComparison.Ordinal));
        Assert.Contains(result.ValidationErrors, e => e.Contains("Security-Zone", StringComparison.Ordinal));
    }

    [Fact]
    public void OwnerProfiles_AreIsolated()
    {
        using var db = TestDb.Create();
        var service = new TradeProfileService(db);

        service.Save(Command(characterId: OwnerA, name: "Profil A") with { MaxCapital = 500_000m });
        service.Save(Command(characterId: OwnerB, name: "Profil B") with { MaxCapital = 9_000_000m });

        // Jeder Owner liest NUR seine eigenen gespeicherten Werte.
        Assert.Equal(500_000m, service.GetForCharacter(OwnerA)!.MaxCapital);
        Assert.Equal(9_000_000m, service.GetForCharacter(OwnerB)!.MaxCapital);
        Assert.Equal(2, db.TradeProfiles.Count());
    }

    [Fact]
    public void GetForCharacter_UnknownOwner_ReturnsNull()
    {
        using var db = TestDb.Create();
        var service = new TradeProfileService(db);

        Assert.Null(service.GetForCharacter(12345));
    }
}