namespace WALLEve.Models.Wallet;

/// <summary>
/// Verknüpfung zwischen zwei Wallet-Einträgen
/// </summary>
public class TransactionLink
{
    /// <summary>
    /// Art der Verknüpfung
    /// </summary>
    public LinkType Type { get; set; }

    /// <summary>
    /// Verknüpfter Entry
    /// </summary>
    public WalletEntryViewModel Entry { get; set; } = null!;

    /// <summary>
    /// Konfidenz der Verknüpfung (0-100%)
    /// </summary>
    public int Confidence { get; set; }

    /// <summary>
    /// Zusätzliche Metadaten (z.B. Status, Begründung)
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }

    /// <summary>
    /// Beschreibung des Links für UI
    /// </summary>
    public string GetDescription()
    {
        return Type switch
        {
            LinkType.DirectContextId => "Direkt verknüpft via ESI",
            LinkType.HeuristicTax => $"Steuer-Zuordnung ({Confidence}% Konfidenz)",
            LinkType.EscrowPair => Metadata?.ContainsKey("Status") == true
                ? $"Escrow {Metadata["Status"]}"
                : "Escrow-Paar",
            LinkType.TransactionChain => "Teil der Transaktionskette",
            LinkType.BrokerFeeModification => "Order-Änderungsgebühr",
            LinkType.MarketOrder => "Market Order",
            _ => "Verknüpft"
        };
    }
}
