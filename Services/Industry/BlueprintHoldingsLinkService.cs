using Microsoft.EntityFrameworkCore;
using WALLEve.Data;
using WALLEve.Models.Holdings;
using WALLEve.Models.Industry;
using WALLEve.Services.Industry.Interfaces;

namespace WALLEve.Services.Industry;

/// <summary>
/// Verknüpft das Blueprint-Register mit den Holdings-Assets derselben belegten
/// Identität (#49). Die Zuordnung geschieht ausschließlich über die Item-Identität
/// (ItemId) gegen den neuesten Character-Snapshot — ein TypeId-only-Match auf ein
/// einzelnes Exemplar ist verboten, weil mehrere Blueprints desselben Typs (BPO
/// und BPC, mehrere Kopien) sonst still falsch zugeordnet würden. Fehlt das Asset
/// im Snapshot oder existiert kein Snapshot (fehlendes Asset, partieller Asset-
/// oder Blueprint-Sync), ist das Ergebnis Unknown statt einer erfundenen Zuordnung.
/// </summary>
public class BlueprintHoldingsLinkService : IBlueprintHoldingsLinkService
{
    private readonly WalletDbContext _db;

    public BlueprintHoldingsLinkService(WalletDbContext db)
    {
        _db = db;
    }

    public async Task<List<BlueprintHoldingsLink>> LinkToHoldingsAsync(int characterId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var blueprints = await _db.BlueprintEntries
            .Where(e => e.CharacterId == characterId)
            .OrderBy(e => e.ItemId)
            .ToListAsync(ct);

        // Neuester Holdings-Snapshot desselben Owners (Owner-Isolation: nur
        // Character-Snapshots mit identischer OwnerId kommen in Frage).
        var snapshot = await _db.HoldingSnapshots
            .Where(s => s.OwnerType == OwnerType.Character && s.OwnerId == characterId)
            .OrderByDescending(s => s.SyncedAt)
            .FirstOrDefaultAsync(ct);

        if (snapshot == null)
        {
            // Kein Asset-Snapshot: alle Blueprints sind Unknown — nie erfinden.
            return blueprints.Select(bp => ToUnknown(bp, characterId)).ToList();
        }

        var items = await _db.HoldingItems
            .Where(i => i.SnapshotId == snapshot.Id)
            .ToListAsync(ct);

        var byItemId = items.ToDictionary(i => i.ItemId);

        var links = new List<BlueprintHoldingsLink>(blueprints.Count);
        foreach (var bp in blueprints)
        {
            if (byItemId.TryGetValue(bp.ItemId, out var asset))
            {
                links.Add(new BlueprintHoldingsLink
                {
                    ItemId = bp.ItemId,
                    TypeId = bp.TypeId,
                    CharacterId = characterId,
                    State = BlueprintLinkState.Known,
                    LinkedLocationId = asset.LocationId,
                    LinkedLocationFlag = asset.LocationFlag,
                    LinkedQuantity = asset.Quantity
                });
            }
            else
            {
                links.Add(ToUnknown(bp, characterId));
            }
        }

        return links;
    }

    private static BlueprintHoldingsLink ToUnknown(BlueprintEntry bp, int characterId)
        => new()
        {
            ItemId = bp.ItemId,
            TypeId = bp.TypeId,
            CharacterId = characterId,
            State = BlueprintLinkState.Unknown
        };
}