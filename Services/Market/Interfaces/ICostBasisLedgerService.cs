using WALLEve.Models.Database;

namespace WALLEve.Services.Market.Interfaces;

/// <summary>
/// Spielt gespiegelte Wallet-Transaktionen chronologisch in die
/// Cost-Basis-Zustandsmaschine (<see cref="WALLEve.Models.Holdings.CostBasisPosition"/>)
/// ein und liefert die resultierenden Positionen je (Character, Type).
/// </summary>
public interface ICostBasisLedgerService
{
    /// <summary>Replay aller Transaktionen eines Charakters; eine Position je TypeId.</summary>
    Task<List<WALLEve.Models.Holdings.CostBasisPosition>> ReplayAsync(int characterId, CancellationToken ct = default);

    /// <summary>Position für einen einzelnen Type; null, wenn keine Transaktionen existieren.</summary>
    Task<WALLEve.Models.Holdings.CostBasisPosition?> GetPositionAsync(int characterId, int typeId, CancellationToken ct = default);
}