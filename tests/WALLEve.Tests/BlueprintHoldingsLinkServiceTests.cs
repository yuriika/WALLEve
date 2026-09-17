using Microsoft.EntityFrameworkCore;
using WALLEve.Data;
using WALLEve.Models.Holdings;
using WALLEve.Models.Industry;
using WALLEve.Services.Industry;

namespace WALLEve.Tests;

/// <summary>
/// Regression-Tests für die Blueprint→Holdings-Verknüpfung (#49).
/// Deckt die Akzeptanzkriterien ab: Zuordnung ausschließlich über die
/// Item-Identität innerhalb derselben belegten Identität (CharacterId =
/// Snapshot-Owner), KEIN TypeId-only-Match auf ein einzelnes Exemplar,
/// fehlendes Asset/fehlender Snapshot = Unknown statt erfundener Zuordnung,
/// Owner-Isolation und „neuester Snapshot gewinnt".
/// </summary>
public class BlueprintHoldingsLinkServiceTests
{
    private const int CharacterA = 90073315;
    private const int CharacterB = 90073316;

    private const int BpoTypeId = 1030;
    private const int BpcTypeId = 1031;

    private static BlueprintHoldingsLinkService CreateService(WalletDbContext db)
        => new(db);

    /// <summary>Legt einen Holdings-Snapshot mit einem Item an und liefert die Snapshot-Id.</summary>
    private static async Task<long> AddSnapshotAsync(WalletDbContext db, int characterId, params HoldingItem[] items)
    {
        var run = new HoldingSyncRun
        {
            OwnerType = OwnerType.Character,
            OwnerId = characterId,
            StartedAt = DateTime.UtcNow,
            Status = "completed"
        };
        db.HoldingSyncRuns.Add(run);
        await db.SaveChangesAsync();

        var snapshot = new HoldingSnapshot
        {
            SyncRunId = run.Id,
            OwnerType = OwnerType.Character,
            OwnerId = characterId,
            SyncedAt = DateTime.UtcNow,
            Source = "esi/characters/90073315/assets"
        };
        db.HoldingSnapshots.Add(snapshot);
        await db.SaveChangesAsync();

        foreach (var item in items)
        {
            item.SnapshotId = snapshot.Id;
            db.HoldingItems.Add(item);
        }

        await db.SaveChangesAsync();
        return snapshot.Id;
    }

    private static HoldingItem Asset(long itemId, int typeId, long locationId, string locationFlag, int quantity = 1)
        => new()
        {
            ItemId = itemId,
            TypeId = typeId,
            Quantity = quantity,
            IsSingleton = true,
            LocationId = locationId,
            LocationFlag = locationFlag
        };

    private static BlueprintEntry Blueprint(long itemId, int typeId, int characterId)
        => new()
        {
            CharacterId = characterId,
            ItemId = itemId,
            TypeId = typeId,
            LocationId = 1,
            LocationFlag = "Hangar",
            Runs = -1,
            IsBlueprintCopy = false,
            UpdatedAt = DateTime.UtcNow
        };

    [Fact]
    public async Task Link_Known_MatchByItemIdWithinSameCharacter()
    {
        var db = TestDb.Create();
        await AddSnapshotAsync(db, CharacterA, Asset(1000, BpoTypeId, 60003760, "Hangar", quantity: 1));
        db.BlueprintEntries.Add(Blueprint(1000, BpoTypeId, CharacterA));
        await db.SaveChangesAsync();

        var links = await CreateService(db).LinkToHoldingsAsync(CharacterA);

        var link = Assert.Single(links);
        Assert.Equal(BlueprintLinkState.Known, link.State);
        Assert.Equal(1000, link.ItemId);
        Assert.Equal(BpoTypeId, link.TypeId);
        Assert.Equal(CharacterA, link.CharacterId);
        // Asset-Rohwerte aus dem Snapshot werden durchgereicht.
        Assert.Equal(60003760, link.LinkedLocationId);
        Assert.Equal("Hangar", link.LinkedLocationFlag);
        Assert.Equal(1, link.LinkedQuantity);
        Assert.False(link.IsUnknown);
    }

    [Fact]
    public async Task Link_MissingAsset_IsUnknown()
    {
        var db = TestDb.Create();
        await AddSnapshotAsync(db, CharacterA, Asset(9999, BpcTypeId, 60003760, "Hangar"));
        db.BlueprintEntries.Add(Blueprint(1000, BpoTypeId, CharacterA));
        await db.SaveChangesAsync();

        var links = await CreateService(db).LinkToHoldingsAsync(CharacterA);

        var link = Assert.Single(links);
        Assert.Equal(BlueprintLinkState.Unknown, link.State);
        Assert.Null(link.LinkedLocationId);
        Assert.Null(link.LinkedLocationFlag);
        Assert.Null(link.LinkedQuantity);
        Assert.True(link.IsUnknown);
    }

    [Fact]
    public async Task Link_SameTypeDifferentItem_DoesNotMatchByTypeId()
    {
        var db = TestDb.Create();
        // Asset mit IDENTISCHEM TypeId, aber anderer ItemId — ein TypeId-only-Match
        // würde den Blueprint still dem falschen Exemplar zuordnen (verboten).
        await AddSnapshotAsync(db, CharacterA, Asset(9999, BpoTypeId, 60003760, "Hangar"));
        db.BlueprintEntries.Add(Blueprint(1000, BpoTypeId, CharacterA));
        await db.SaveChangesAsync();

        var links = await CreateService(db).LinkToHoldingsAsync(CharacterA);

        Assert.Equal(BlueprintLinkState.Unknown, Assert.Single(links).State);
    }

    [Fact]
    public async Task Link_AssetOfAnotherCharacter_DoesNotLink()
    {
        var db = TestDb.Create();
        // CharacterB besitzt das Asset — CharacterAs Blueprint darf nicht daran
        // verknüpft werden (Owner-Isolation über belegte Identitäten).
        await AddSnapshotAsync(db, CharacterB, Asset(1000, BpoTypeId, 60003760, "Hangar"));
        db.BlueprintEntries.Add(Blueprint(1000, BpoTypeId, CharacterA));
        await db.SaveChangesAsync();

        var links = await CreateService(db).LinkToHoldingsAsync(CharacterA);

        Assert.Equal(BlueprintLinkState.Unknown, Assert.Single(links).State);
    }

    [Fact]
    public async Task Link_NoSnapshotAtAll_AllUnknown()
    {
        var db = TestDb.Create();
        db.BlueprintEntries.Add(Blueprint(1000, BpoTypeId, CharacterA));
        await db.SaveChangesAsync();

        var links = await CreateService(db).LinkToHoldingsAsync(CharacterA);

        Assert.Equal(BlueprintLinkState.Unknown, Assert.Single(links).State);
    }

    [Fact]
    public async Task Link_LatestSnapshotWins()
    {
        var db = TestDb.Create();
        // Älterer Snapshot enthält das Asset, der neueste nicht mehr (Asset verkauft).
        var older = await AddSnapshotAsync(db, CharacterA, Asset(1000, BpoTypeId, 60003760, "Hangar"));
        var olderSnapshot = await db.HoldingSnapshots.SingleAsync(s => s.Id == older);
        olderSnapshot.SyncedAt = DateTime.UtcNow.AddDays(-2);

        await AddSnapshotAsync(db, CharacterA);
        db.BlueprintEntries.Add(Blueprint(1000, BpoTypeId, CharacterA));
        await db.SaveChangesAsync();

        var links = await CreateService(db).LinkToHoldingsAsync(CharacterA);

        // Der aktuelle Bestand kennt das Asset nicht mehr → Unknown, keine Alt-Zuordnung.
        Assert.Equal(BlueprintLinkState.Unknown, Assert.Single(links).State);
    }

    [Fact]
    public async Task Link_MixedKnownAndUnknown_ListReflectsBoth()
    {
        var db = TestDb.Create();
        await AddSnapshotAsync(db, CharacterA, Asset(1000, BpoTypeId, 60003760, "Hangar"));
        db.BlueprintEntries.Add(Blueprint(1000, BpoTypeId, CharacterA));
        db.BlueprintEntries.Add(Blueprint(2000, BpcTypeId, CharacterA));
        await db.SaveChangesAsync();

        var links = await CreateService(db).LinkToHoldingsAsync(CharacterA);

        Assert.Equal(2, links.Count);
        Assert.Equal(BlueprintLinkState.Known, links.Single(l => l.ItemId == 1000).State);
        Assert.Equal(BlueprintLinkState.Unknown, links.Single(l => l.ItemId == 2000).State);
    }
}