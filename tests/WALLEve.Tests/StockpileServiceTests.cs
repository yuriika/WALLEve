using Microsoft.EntityFrameworkCore;
using WALLEve.Data;
using WALLEve.Models.Holdings;
using WALLEve.Models.Stockpiles;
using WALLEve.Services.Stockpiles;

namespace WALLEve.Tests;

/// <summary>
/// Service-Tests für Issue #36 (Stockpile-Ziele persistieren): CRUD,
/// Validierung (negative Ziele), Duplikat-Schutz (identische Type-Ziele),
/// Owner-Isolation und Archiv-Wiederlesen — ohne Bestandsberechnung.
/// </summary>
public class StockpileServiceTests
{
    private static StockpileService CreateService(WalletDbContext db) => new(db);

    private static StockpileTarget Target(OwnerType ownerType = OwnerType.Character, int ownerId = 90073315,
        int typeId = 34, int quantity = 100, long? locationId = null, string? note = null)
        => new()
        {
            OwnerType = ownerType,
            OwnerId = ownerId,
            TypeId = typeId,
            Quantity = quantity,
            LocationId = locationId,
            Note = note
        };

    [Fact]
    public async Task CreateAsync_RejectsNegativeAndZeroQuantity()
    {
        await using var db = TestDb.Create();
        var service = CreateService(db);

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(Target(quantity: -5)));
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(Target(quantity: 0)));

        Assert.Empty(await db.StockpileTargets.ToListAsync());
    }

    [Fact]
    public async Task CreateAsync_RejectsDuplicateActiveTypeTarget()
    {
        await using var db = TestDb.Create();
        var service = CreateService(db);

        await service.CreateAsync(Target(typeId: 34));

        // Identisches Type-Ziel desselben Owners: kein versehentliches Duplikat.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateAsync(Target(typeId: 34, quantity: 500)));
    }

    [Fact]
    public async Task CreateAsync_AllowsSameTypeForDifferentOwner()
    {
        await using var db = TestDb.Create();
        var service = CreateService(db);

        await service.CreateAsync(Target(ownerType: OwnerType.Character, ownerId: 90073315, typeId: 34));
        var second = await service.CreateAsync(Target(ownerType: OwnerType.Corporation, ownerId: 1234, typeId: 34));

        Assert.Equal(OwnerType.Corporation, second.OwnerType);
        Assert.Equal(2, await db.StockpileTargets.CountAsync());
    }

    [Fact]
    public async Task CreateAsync_ArchiveFreesTheTypeSlot()
    {
        await using var db = TestDb.Create();
        var service = CreateService(db);

        var created = await service.CreateAsync(Target(typeId: 34));
        Assert.True(await service.SetArchivedAsync(created.Id, archived: true));

        // Archiviertes Ziel ist kein aktives Duplikat mehr → neues Ziel möglich.
        var replacement = await service.CreateAsync(Target(typeId: 34, quantity: 250));
        Assert.Equal(250, replacement.Quantity);

        var all = await service.GetAllAsync(OwnerType.Character, 90073315, includeArchived: true);
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task GetAllAsync_IsolatesOwners_AndExcludesArchivedByDefault()
    {
        await using var db = TestDb.Create();
        var service = CreateService(db);

        var charTarget = await service.CreateAsync(Target(ownerType: OwnerType.Character, ownerId: 90073315, typeId: 34));
        await service.CreateAsync(Target(ownerType: OwnerType.Character, ownerId: 90073315, typeId: 35));
        await service.CreateAsync(Target(ownerType: OwnerType.Corporation, ownerId: 1234, typeId: 34));
        await service.SetArchivedAsync(charTarget.Id, archived: true);

        // Owner-Isolation: jeder sieht nur seine eigenen Ziele.
        var charAll = await service.GetAllAsync(OwnerType.Character, 90073315);
        Assert.Single(charAll);
        Assert.Equal(35, charAll[0].TypeId);

        var corpAll = await service.GetAllAsync(OwnerType.Corporation, 1234);
        Assert.Single(corpAll);

        // Archiv-Wiederlesen: mit includeArchived bleiben archivierte sichtbar.
        var charAllArchived = await service.GetAllAsync(OwnerType.Character, 90073315, includeArchived: true);
        Assert.Equal(2, charAllArchived.Count);
        Assert.Contains(charAllArchived, t => t.Id == charTarget.Id && t.IsArchived);
    }

    [Fact]
    public async Task UpdateAsync_ChangesQuantityLocationAndNote()
    {
        await using var db = TestDb.Create();
        var service = CreateService(db);

        var created = await service.CreateAsync(Target(typeId: 34, quantity: 100, note: "alt"));
        created.Quantity = 350;
        created.LocationId = 60003760;
        created.Note = "neu";

        var updated = await service.UpdateAsync(created);

        Assert.Equal(350, updated.Quantity);
        Assert.Equal(60003760, updated.LocationId);
        Assert.Equal("neu", updated.Note);
        Assert.True(updated.UpdatedAt >= created.UpdatedAt);

        var reread = await service.GetAsync(created.Id);
        Assert.Equal(350, reread!.Quantity);
    }

    [Fact]
    public async Task UpdateAsync_RejectsInvalidQuantity_AndUnknownTarget()
    {
        await using var db = TestDb.Create();
        var service = CreateService(db);

        var created = await service.CreateAsync(Target(typeId: 34));
        created.Quantity = -1;
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateAsync(created));

        var ghost = Target(typeId: 99);
        ghost.Id = 424242;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(ghost));
    }

    [Fact]
    public async Task UpdateAsync_RejectsMoveOntoAnotherActivesType()
    {
        await using var db = TestDb.Create();
        var service = CreateService(db);

        var first = await service.CreateAsync(Target(typeId: 34));
        await service.CreateAsync(Target(typeId: 35));

        first.TypeId = 35;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(first));
    }

    [Fact]
    public async Task DeleteAsync_RemovesTarget_AndReportsMissing()
    {
        await using var db = TestDb.Create();
        var service = CreateService(db);

        var created = await service.CreateAsync(Target(typeId: 34));
        Assert.True(await service.DeleteAsync(created.Id));
        Assert.Null(await service.GetAsync(created.Id));
        Assert.False(await service.DeleteAsync(created.Id));
        Assert.False(await service.SetArchivedAsync(created.Id, archived: true));
    }

    [Fact]
    public async Task SetArchivedAsync_TogglesArchiveState_AndAllowsReread()
    {
        await using var db = TestDb.Create();
        var service = CreateService(db);

        var created = await service.CreateAsync(Target(typeId: 34));
        Assert.False(created.IsArchived);

        Assert.True(await service.SetArchivedAsync(created.Id, archived: true));
        var archived = await service.GetAsync(created.Id);
        Assert.True(archived!.IsArchived);

        Assert.True(await service.SetArchivedAsync(created.Id, archived: false));
        Assert.False((await service.GetAsync(created.Id))!.IsArchived);
    }
}