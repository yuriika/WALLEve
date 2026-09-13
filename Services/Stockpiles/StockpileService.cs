using Microsoft.EntityFrameworkCore;
using WALLEve.Data;
using WALLEve.Models.Holdings;
using WALLEve.Models.Stockpiles;
using WALLEve.Services.Stockpiles.Interfaces;

namespace WALLEve.Services.Stockpiles;

/// <summary>
/// CRUD für Stockpile-Ziele (Issue #36). Validierung ohne Bestandsberechnung:
/// negative Mengen werden abgelehnt, identische aktive Type-Ziele je Owner/Type
/// werden nicht versehentlich dupliziert (partieller Unique-Index plus
/// Service-Guard), Owner-Zugriffe sind isoliert, archivierte Ziele bleiben
/// nachvollziehbar abrufbar.
/// </summary>
public class StockpileService : IStockpileService
{
    private readonly WalletDbContext _db;

    public StockpileService(WalletDbContext db)
    {
        _db = db;
    }

    private static void Validate(StockpileTarget target)
    {
        if (target.OwnerId <= 0)
        {
            throw new ArgumentException("OwnerId muss positiv sein.", nameof(target));
        }

        if (target.TypeId <= 0)
        {
            throw new ArgumentException("TypeId muss positiv sein.", nameof(target));
        }

        // Akzeptanzkriterium #36: negative Ziele ablehnen.
        if (target.Quantity <= 0)
        {
            throw new ArgumentException("Die Zielmenge muss größer als 0 sein — negative Ziele sind nicht erlaubt.", nameof(target));
        }
    }

    public async Task<List<StockpileTarget>> GetAllAsync(OwnerType ownerType, int ownerId,
        bool includeArchived = false, CancellationToken ct = default)
    {
        var query = _db.StockpileTargets
            .AsNoTracking()
            .Where(t => t.OwnerType == ownerType && t.OwnerId == ownerId);

        if (!includeArchived)
        {
            query = query.Where(t => !t.IsArchived);
        }

        return await query
            .OrderBy(t => t.TypeId)
            .ThenBy(t => t.Id)
            .ToListAsync(ct);
    }

    public async Task<StockpileTarget?> GetAsync(long id, CancellationToken ct = default)
        => await _db.StockpileTargets
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task<StockpileTarget> CreateAsync(StockpileTarget target, CancellationToken ct = default)
    {
        Validate(target);

        var duplicate = await _db.StockpileTargets.AnyAsync(
            t => t.OwnerType == target.OwnerType
                 && t.OwnerId == target.OwnerId
                 && t.TypeId == target.TypeId
                 && !t.IsArchived, ct);

        if (duplicate)
        {
            throw new InvalidOperationException(
                $"Für Type {target.TypeId} existiert bereits ein aktives Stockpile-Ziel dieses Owners.");
        }

        var now = DateTime.UtcNow;
        target.Id = 0;
        target.CreatedAt = now;
        target.UpdatedAt = now;
        _db.StockpileTargets.Add(target);
        await _db.SaveChangesAsync(ct);
        return target;
    }

    public async Task<StockpileTarget> UpdateAsync(StockpileTarget target, CancellationToken ct = default)
    {
        Validate(target);

        var existing = await _db.StockpileTargets
            .FirstOrDefaultAsync(t => t.Id == target.Id, ct)
            ?? throw new InvalidOperationException("Das Stockpile-Ziel existiert nicht mehr.");

        var duplicate = await _db.StockpileTargets.AnyAsync(
            t => t.OwnerType == existing.OwnerType
                 && t.OwnerId == existing.OwnerId
                 && t.TypeId == target.TypeId
                 && !t.IsArchived
                 && t.Id != target.Id, ct);

        if (duplicate)
        {
            throw new InvalidOperationException(
                $"Für Type {target.TypeId} existiert bereits ein aktives Stockpile-Ziel dieses Owners.");
        }

        existing.Quantity = target.Quantity;
        existing.LocationId = target.LocationId;
        existing.Note = target.Note;
        existing.IsArchived = target.IsArchived;
        existing.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return existing;
    }

    public async Task<bool> DeleteAsync(long id, CancellationToken ct = default)
    {
        var existing = await _db.StockpileTargets
            .FirstOrDefaultAsync(t => t.Id == id, ct);
        if (existing is null)
        {
            return false;
        }

        _db.StockpileTargets.Remove(existing);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> SetArchivedAsync(long id, bool archived, CancellationToken ct = default)
    {
        var existing = await _db.StockpileTargets
            .FirstOrDefaultAsync(t => t.Id == id, ct);
        if (existing is null)
        {
            return false;
        }

        if (existing.IsArchived == archived)
        {
            return true;
        }

        existing.IsArchived = archived;
        existing.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}