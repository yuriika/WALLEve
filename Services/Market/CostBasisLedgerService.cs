using Microsoft.EntityFrameworkCore;
using WALLEve.Data;
using WALLEve.Models.Database;
using WALLEve.Models.Holdings;
using WALLEve.Services.Market.Interfaces;

namespace WALLEve.Services.Market;

/// <summary>
/// Idempotentes Buchungsledger für die Cost-Basis (Issue #52).
///
/// Das Ledger (<see cref="CostBasisLedgerEntry"/>) ist die persistierte,
/// chronologisch geordnete Ereignisfolge aller lokalen Wallet-Transaktionen
/// pro (Character, Type). Der eindeutige Index auf
/// (CharacterId, TypeId, SourceTransactionId) macht jeden Duplicate-Import
/// unmöglich; ein Replay derselben Ereignisfolge führt deterministisch zum
/// selben Zustand — doppelte oder verspätete Transaktionen ändern das Ergebnis
/// nicht.
///
/// Unbekannte Eröffnungsbestände (Bestand vor Trackingbeginn, Mining/Loot/Production/Contract)
/// kennt das Ledger NICHT — sie werden über die Zustandsmaschine separat geführt
/// (<see cref="CostBasisPosition.ApplyOpeningBalance"/>, z. B. beim Abgleich mit einem
/// Bestands-Snapshot) und nie rückwirkend aus Käufen geschätzt. Transaktionen belegen
/// keine Item-Lot-Identität; Transfers werden nur bei belastbarer Evidenz verbunden.
/// </summary>
public class CostBasisLedgerService : ICostBasisLedgerService
{
    private readonly WalletDbContext _db;

    /// <summary>Batchgröße für idempotente Imports.</summary>
    private const int ImportBatchSize = 10_000;

    public CostBasisLedgerService(WalletDbContext db)
    {
        _db = db;
    }

    public async Task<int> ImportTransactionsAsync(int characterId,
        IReadOnlyCollection<WalletTransactionRecord> transactions, CancellationToken ct = default)
        => await ImportCoreAsync(characterId, transactions
            .Select(t => (TypeId: t.TypeId, SourceId: t.TransactionId, t.Date, t.IsBuy, t.Quantity, t.UnitPrice))
            .ToList(), ct);

    public async Task<int> ImportTransactionsAsync(int characterId,
        IReadOnlyCollection<WALLEve.Models.Esi.Wallet.WalletTransaction> transactions, CancellationToken ct = default)
        => await ImportCoreAsync(characterId, transactions
            .Select(t => (TypeId: t.TypeId, SourceId: t.TransactionId, t.Date, t.IsBuy, t.Quantity, t.UnitPrice))
            .ToList(), ct);

    private async Task<int> ImportCoreAsync(int characterId,
        IReadOnlyCollection<(int TypeId, long SourceId, DateTime Date, bool IsBuy, int Quantity, double UnitPrice)> transactions,
        CancellationToken ct)
    {
        if (transactions.Count == 0) return 0;

        // Bereits importierte Quell-IDs pro Type ermitteln (Dedup)
        var existing = await _db.CostBasisLedgerEntries
            .Where(e => e.CharacterId == characterId)
            .Select(e => new { e.TypeId, e.SourceTransactionId })
            .ToHashSetAsync(ct);

        var now = DateTime.UtcNow;
        var newEntries = new List<CostBasisLedgerEntry>(transactions.Count);
        var seenInBatch = new HashSet<(int TypeId, long SourceTransactionId)>();
        foreach (var t in transactions)
        {
            ct.ThrowIfCancellationRequested();
            if (existing.Contains(new { TypeId = t.TypeId, SourceTransactionId = t.SourceId })) continue;
            if (!seenInBatch.Add((t.TypeId, t.SourceId))) continue; // Duplikat im selben Import

            newEntries.Add(new CostBasisLedgerEntry
            {
                CharacterId = characterId,
                TypeId = t.TypeId,
                SourceTransactionId = t.SourceId,
                Date = t.Date,
                IsBuy = t.IsBuy,
                Quantity = t.Quantity,
                UnitPrice = t.UnitPrice,
                ImportedAt = now
            });
        }

        // In Blöcken speichern (analog zum Sink), um In-Memory-Druck klein zu halten.
        // Der Unique-Index bleibt zusätzlicher Schutz gegen parallele Doppel-Imports.
        for (var i = 0; i < newEntries.Count; i += ImportBatchSize)
        {
            ct.ThrowIfCancellationRequested();
            var batch = newEntries.Skip(i).Take(ImportBatchSize).ToList();
            _db.CostBasisLedgerEntries.AddRange(batch);
            await _db.SaveChangesAsync(ct);
        }

        return newEntries.Count;
    }

    public async Task<List<CostBasisPosition>> ReplayAsync(int characterId, CancellationToken ct = default)
    {
        var events = await _db.CostBasisLedgerEntries
            .AsNoTracking()
            .Where(e => e.CharacterId == characterId && e.Quantity > 0)
            .OrderBy(e => e.Date)
            .ThenBy(e => e.SourceTransactionId) // deterministische Reihenfolge bei identischem Zeitstempel
            .ToListAsync(ct);

        var positionsByType = new Dictionary<int, CostBasisPosition>();
        foreach (var e in events)
        {
            if (!positionsByType.TryGetValue(e.TypeId, out var position))
            {
                position = new CostBasisPosition(characterId, e.TypeId);
                positionsByType.Add(e.TypeId, position);
            }

            if (e.IsBuy)
            {
                position.ApplyBuy(e.Quantity, e.UnitPrice);
            }
            else
            {
                position.ApplySell(e.Quantity, e.UnitPrice);
            }
        }

        return positionsByType.Values
            .OrderBy(p => p.TypeId)
            .ToList();
    }

    public async Task<CostBasisPosition?> GetPositionAsync(int characterId, int typeId, CancellationToken ct = default)
    {
        var events = await _db.CostBasisLedgerEntries
            .AsNoTracking()
            .Where(e => e.CharacterId == characterId && e.TypeId == typeId && e.Quantity > 0)
            .OrderBy(e => e.Date)
            .ThenBy(e => e.SourceTransactionId)
            .ToListAsync(ct);

        if (events.Count == 0) return null;

        var position = new CostBasisPosition(characterId, typeId);
        foreach (var e in events)
        {
            if (e.IsBuy)
            {
                position.ApplyBuy(e.Quantity, e.UnitPrice);
            }
            else
            {
                position.ApplySell(e.Quantity, e.UnitPrice);
            }
        }

        return position;
    }
}