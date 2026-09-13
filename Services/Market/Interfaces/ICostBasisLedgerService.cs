using WALLEve.Models.Database;

namespace WALLEve.Services.Market.Interfaces;

/// <summary>
/// Idempotentes Buchungsledger für die Cost-Basis (Issue #52).
/// Persistiert Kauf-/Verkaufs-Ereignisse chronologisch mit stabilen Quell-IDs
/// pro Owner/Type, verhindert Duplicate-Imports auf Datenbankebene und spielt
/// die Ereignisfolge deterministisch in die Cost-Basis-Zustandsmaschine
/// (<see cref="WALLEve.Models.Holdings.CostBasisPosition"/>) ein.
/// </summary>
public interface ICostBasisLedgerService
{
    /// <summary>
    /// Importiert gespiegelte Wallet-Transaktionen idempotent ins Ledger:
    /// bereits vorhandene (CharacterId, TypeId, SourceTransactionId) werden
    /// übersprungen, nur neue Ereignisse werden angehängt.
    /// </summary>
    /// <returns>Anzahl der neu importierten Ereignisse.</returns>
    Task<int> ImportTransactionsAsync(int characterId, IReadOnlyCollection<WalletTransactionRecord> transactions, CancellationToken ct = default);

    /// <summary>
    /// Importiert frische ESI-Wallet-Transaktionen idempotent ins Ledger
    /// (dieselben Dedup-Regeln wie <see cref="ImportTransactionsAsync(int, IReadOnlyCollection{WalletTransactionRecord}, CancellationToken)"/>).
    /// </summary>
    /// <returns>Anzahl der neu importierten Ereignisse.</returns>
    Task<int> ImportTransactionsAsync(int characterId, IReadOnlyCollection<WALLEve.Models.Esi.Wallet.WalletTransaction> transactions, CancellationToken ct = default);

    /// <summary>Replay aller Ledger-Ereignisse eines Charakters; eine Position je TypeId.</summary>
    Task<List<WALLEve.Models.Holdings.CostBasisPosition>> ReplayAsync(int characterId, CancellationToken ct = default);

    /// <summary>Position für einen einzelnen Type; null, wenn keine Ereignisse existieren.</summary>
    Task<WALLEve.Models.Holdings.CostBasisPosition?> GetPositionAsync(int characterId, int typeId, CancellationToken ct = default);
}