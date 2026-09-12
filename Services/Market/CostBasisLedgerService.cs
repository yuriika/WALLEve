using Microsoft.EntityFrameworkCore;
using WALLEve.Data;
using WALLEve.Models.Database;
using WALLEve.Models.Holdings;
using WALLEve.Services.Market.Interfaces;

namespace WALLEve.Services.Market;

/// <summary>
/// Replay der lokalen Wallet-Transaktionsspiegelung in die Cost-Basis-Zustandsmaschine
/// (Issue #42). Erzeugt pro (Character, Type) eine <see cref="CostBasisPosition"/> aus der
/// chronologischen Folge aller Käufe/Verkäufe dieses Charakters.
///
/// Unbekannte Eröffnungsbestände (Bestand vor Trackingbeginn, Mining/Loot/Production/Contract)
/// kennt der Replay NICHT — sie werden über die Maschine separat geführt
/// (<see cref="CostBasisPosition.ApplyOpeningBalance"/>, z. B. beim Abgleich mit einem
/// Bestands-Snapshot) und nie rückwirkend aus Käufen geschätzt.
/// </summary>
public class CostBasisLedgerService : ICostBasisLedgerService
{
    private readonly WalletDbContext _db;

    public CostBasisLedgerService(WalletDbContext db)
    {
        _db = db;
    }

    public async Task<List<CostBasisPosition>> ReplayAsync(int characterId, CancellationToken ct = default)
    {
        var transactions = await _db.WalletTransactionRecords
            .AsNoTracking()
            .Where(t => t.CharacterId == characterId && t.Quantity > 0)
            .OrderBy(t => t.Date)
            .ThenBy(t => t.TransactionId) // deterministische Reihenfolge bei identischem Zeitstempel
            .ToListAsync(ct);

        var positionsByType = new Dictionary<int, CostBasisPosition>();
        foreach (var t in transactions)
        {
            if (!positionsByType.TryGetValue(t.TypeId, out var position))
            {
                position = new CostBasisPosition(characterId, t.TypeId);
                positionsByType.Add(t.TypeId, position);
            }

            if (t.IsBuy)
            {
                position.ApplyBuy(t.Quantity, t.UnitPrice);
            }
            else
            {
                position.ApplySell(t.Quantity, t.UnitPrice);
            }
        }

        return positionsByType.Values
            .OrderBy(p => p.TypeId)
            .ToList();
    }

    public async Task<CostBasisPosition?> GetPositionAsync(int characterId, int typeId, CancellationToken ct = default)
    {
        var transactions = await _db.WalletTransactionRecords
            .AsNoTracking()
            .Where(t => t.CharacterId == characterId && t.TypeId == typeId && t.Quantity > 0)
            .OrderBy(t => t.Date)
            .ThenBy(t => t.TransactionId)
            .ToListAsync(ct);

        if (transactions.Count == 0) return null;

        var position = new CostBasisPosition(characterId, typeId);
        foreach (var t in transactions)
        {
            if (t.IsBuy)
            {
                position.ApplyBuy(t.Quantity, t.UnitPrice);
            }
            else
            {
                position.ApplySell(t.Quantity, t.UnitPrice);
            }
        }

        return position;
    }
}