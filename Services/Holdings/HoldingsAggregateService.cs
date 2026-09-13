using Microsoft.EntityFrameworkCore;
using WALLEve.Data;
using WALLEve.Models.Holdings;
using WALLEve.Services.Holdings.Interfaces;
using WALLEve.Services.Sde.Interfaces;

namespace WALLEve.Services.Holdings;

/// <summary>
/// Baut die aggregierte Sicht (Type-/Type-Location-Projektionen und Ortsbaum)
/// auf den neuesten vollständigen Holdings-Snapshot eines Owners (#57).
/// Owner werden strikt getrennt: Pro OwnerType/OwnerId wird ausschließlich der
/// neueste abgeschlossene Snapshot dieses Owners geladen — keine Summe, kein
/// Baum vermischt Owner. Unbekannte Locations bleiben separat sichtbar und
/// Freshness/Qualität (aufgelöst vs. unbekannt) werden explizit ausgewiesen.
/// Deterministisch: alle Ortsnamen kommen aus dem lokalen SDE/der reinen
/// Kettenauflösung, keine Live-ESI-Abhängigkeit außer der Strukturauflösung
/// des Resolvers (#50).
/// </summary>
public class HoldingsAggregateService : IHoldingsAggregateService
{
    private readonly WalletDbContext _db;
    private readonly IHoldingsLocationResolver _resolver;
    private readonly ISdeUniverseService _sde;

    public HoldingsAggregateService(
        WalletDbContext db,
        IHoldingsLocationResolver resolver,
        ISdeUniverseService sde)
    {
        _db = db;
        _resolver = resolver;
        _sde = sde;
    }

    public async Task<HoldingsTreeResult?> BuildTreeAsync(
        OwnerType ownerType,
        int ownerId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var snapshot = await _db.HoldingSnapshots
            .Include(s => s.Items)
            .Where(s => s.OwnerType == ownerType
                        && s.OwnerId == ownerId
                        && s.SyncRun.Status == "completed")
            .OrderByDescending(s => s.SyncedAt)
            .FirstOrDefaultAsync(ct);

        if (snapshot is null)
            return null;

        var items = snapshot.Items.ToList();

        // Locale Aggregate: erst aufgelöste Sicht erzeugen, dann projizieren.
        var resolved = await _resolver.ResolveSnapshotAsync(items, ct);

        // Typnamen EINMAL je Ladevorgang auflösen (kein N+1 pro Item). SDE
        // kann offline sein — dann bleiben Namen ehrlich null (UI zeigt Fallback).
        var typeNames = new Dictionary<int, string>();
        var distinctTypeIds = items.Select(i => i.TypeId).Distinct().ToList();
        foreach (var typeId in distinctTypeIds)
        {
            var name = await _sde.GetTypeNameAsync(typeId);
            if (name != null)
                typeNames[typeId] = name;
        }

        var itemsById = new Dictionary<long, HoldingItem>();
        foreach (var item in items)
            itemsById.TryAdd(item.ItemId, item);

        return HoldingsTreeBuilder.Build(
            ownerType: ownerType,
            ownerId: ownerId,
            snapshotId: snapshot.Id,
            syncedAt: snapshot.SyncedAt,
            now: DateTime.UtcNow,
            resolvedItems: resolved,
            typeNames: typeNames,
            itemsById: itemsById);
    }
}