namespace WALLEve.Models.Wallet;

/// <summary>
/// Art der Verknüpfung zwischen Wallet-Einträgen
/// </summary>
public enum LinkType
{
    /// <summary>
    /// Direkt über ContextId verlinkt (100% Sicherheit)
    /// </summary>
    DirectContextId,

    /// <summary>
    /// Tax-Linking über Heuristik (Zeit + Betrag)
    /// </summary>
    HeuristicTax,

    /// <summary>
    /// market_escrow ↔ market_escrow_release Paar
    /// </summary>
    EscrowPair,

    /// <summary>
    /// Verknüpfung zu Market Order
    /// </summary>
    MarketOrder,

    /// <summary>
    /// Teil einer Transaktionskette
    /// </summary>
    TransactionChain,

    /// <summary>
    /// Broker Fee für Order-Änderung
    /// </summary>
    BrokerFeeModification
}
