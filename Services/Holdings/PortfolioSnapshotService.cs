using Microsoft.EntityFrameworkCore;
using WALLEve.Data;
using WALLEve.Models.Holdings;
using WALLEve.Models.Portfolio;
using WALLEve.Services.Holdings.Interfaces;

namespace WALLEve.Services.Holdings;

/// <summary>
/// Erfassung des historischen Portfolio-Punkts zu einem vollständigen
/// Holdings-Snapshot (Issue #51).
///
/// Die Qualitätszähler (bewertet / Cost-Basis bekannt) werden zum
/// Erfassungszeitpunkt als Kopie gespeichert und nie aktualisiert:
/// spätere Preise oder nachträgliche Cost-Basis-Buchungen überschreiben
/// die historische Provenienz nicht. Fehlende Daten sind explizit Unknown.
///
/// Deterministisch und ohne Live-ESI: alle Lookups laufen gebündelt über
/// die lokale Datenbank (kein N+1 pro Item).
/// </summary>
public class PortfolioSnapshotService : IPortfolioSnapshotService
{
    private readonly WalletDbContext _db;

    public PortfolioSnapshotService(WalletDbContext db)
    {
        _db = db;
    }

    public async Task<PortfolioSnapshot> CaptureAsync(long holdingSnapshotId, CancellationToken ct = default)
    {
        var source = await _db.HoldingSnapshots
            .Include(s => s.Items)
            .SingleOrDefaultAsync(s => s.Id == holdingSnapshotId, ct)
            ?? throw new InvalidOperationException(
                $"Kein Holdings-Snapshot mit Id {holdingSnapshotId} vorhanden.");

        // Idempotenz: dieselbe Quell-Snapshot-ID erzeugt keine doppelte Historie.
        var existing = await _db.PortfolioSnapshots
            .SingleOrDefaultAsync(p => p.HoldingSnapshotId == holdingSnapshotId, ct);
        if (existing is not null)
        {
            return existing;
        }

        var typeIds = source.Items.Select(i => i.TypeId).Distinct().ToList();

        // Bewertbarkeit: Typ hat mindestens einen Markt-Snapshot (ein Lookup).
        var valuedTypeIds = typeIds.Count == 0
            ? new HashSet<int>()
            : (await _db.MarketSnapshots
                .Where(s => typeIds.Contains(s.TypeId))
                .Select(s => s.TypeId)
                .Distinct()
                .ToListAsync(ct)).ToHashSet();

        // Cost-Basis-Verfügbarkeit je (Character, Typ) (ein Lookup; nur Character).
        var coveredTypeIds = new HashSet<int>();
        if (source.OwnerType == OwnerType.Character && typeIds.Count > 0)
        {
            var entries = await _db.CostBasisEntries
                .Where(e => e.CharacterId == source.OwnerId && typeIds.Contains(e.TypeId))
                .Select(e => e.TypeId)
                .Distinct()
                .ToListAsync(ct);
            coveredTypeIds = entries.ToHashSet();
        }

        var total = source.Items.Count;
        var valued = source.Items.Count(i => valuedTypeIds.Contains(i.TypeId));
        var costBasisKnown = source.Items.Count(i => coveredTypeIds.Contains(i.TypeId));

        var portfolio = new PortfolioSnapshot
        {
            HoldingSnapshotId = source.Id,
            OwnerType = source.OwnerType,
            OwnerId = source.OwnerId,
            CapturedAt = DateTime.UtcNow,
            TotalItems = total,
            ValuedItemCount = valued,
            UnknownValuationItemCount = total - valued,
            CostBasisKnownItemCount = costBasisKnown,
            UnknownCostBasisItemCount = total - costBasisKnown
        };

        _db.PortfolioSnapshots.Add(portfolio);
        await _db.SaveChangesAsync(ct);
        return portfolio;
    }
}