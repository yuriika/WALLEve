using WALLEve.Extensions;

namespace WALLEve.Models.Wallet;

public class WalletEntryViewModel
{
    // Core data
    public long Id { get; set; }
    public DateTime Date { get; set; }
    public double Amount { get; set; }
    public double? Balance { get; set; }
    public string RefType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    // Transaction-specific (null if not a market transaction)
    public WalletTransactionDetails? TransactionDetails { get; set; }

    // Tax information
    public double? Tax { get; set; }
    public int? TaxReceiverId { get; set; }

    // Party information
    public int? FirstPartyId { get; set; }
    public int? SecondPartyId { get; set; }

    // Context (for linking related transactions)
    public long? ContextId { get; set; }
    public string? ContextIdType { get; set; }

    // Related transaction (for tax entries) - DEPRECATED, use RelatedTransactions stattdessen
    [Obsolete("Verwende RelatedTransactions Liste stattdessen")]
    public WalletEntryViewModel? RelatedTransaction { get; set; }

    // NEW: Alle verknüpften Transaktionen (mit Metadaten)
    public List<TransactionLink> RelatedTransactions { get; set; } = new();

    // NEW: Zugehörige Transaktionskette (falls Teil einer Kette)
    public TransactionChain? Chain { get; set; }

    // NEW: Verknüpfte Market Order (falls vorhanden)
    public MarketOrderInfo? LinkedMarketOrder { get; set; }

    // Enriched data from SDE
    public string? ItemName { get; set; }
    public string? LocationName { get; set; }
    public string? FirstPartyName { get; set; }
    public string? SecondPartyName { get; set; }

    // UI helpers
    public bool IsExpanded { get; set; }
    public string FormattedAmount => Amount.FormatIsk();
    public string FormattedBalance => Balance?.FormatIsk() ?? "-";
    public string TransactionTypeDisplay => GetTransactionTypeDisplay();
    public string IconEmoji => GetIconForRefType();

    /// <summary>
    /// Net amount: what the player actually gained/lost
    /// For market transactions with linked tax: Amount + Tax.Amount
    /// For tax entries with linked transaction: Transaction.Amount + Amount
    /// For standalone entries: just Amount
    /// </summary>
    public double NetAmount
    {
        get
        {
            if (RelatedTransaction == null)
                return Amount;

            if (RefType == "transaction_tax" && RelatedTransaction.RefType != "transaction_tax")
            {
                // Tax entry: combine with related transaction
                return RelatedTransaction.Amount + Amount;
            }
            else if (RefType != "transaction_tax" && RelatedTransaction.RefType == "transaction_tax")
            {
                // Transaction entry: combine with related tax
                return Amount + RelatedTransaction.Amount;
            }

            return Amount;
        }
    }

    public string FormattedNetAmount => NetAmount.FormatIsk();

    private string GetTransactionTypeDisplay()
    {
        return RefType switch
        {
            "player_trading" => TransactionDetails?.IsBuy == true ? "Kauf" : "Verkauf",
            "market_escrow" => "Markt Escrow",
            "market_escrow_release" => "Escrow Freigabe",
            "bounty_prizes" => "Kopfgeld",
            "mission_reward" => "Missionsbelohnung",
            "insurance" => "Versicherung",
            "contract_deposit" => "Vertrag Kaution",
            "contract_reward" => "Vertrag Belohnung",
            _ => RefType.Replace("_", " ")
        };
    }

    private string GetIconForRefType()
    {
        return RefType switch
        {
            "player_trading" => "🛒",
            "market_escrow" => "💰",
            "bounty_prizes" => "🎯",
            "mission_reward" => "📜",
            "insurance" => "🛡️",
            "contract_deposit" => "📝",
            _ => "💸"
        };
    }
}

public class WalletTransactionDetails
{
    public bool IsBuy { get; set; }
    public int TypeId { get; set; }
    public int Quantity { get; set; }
    public double UnitPrice { get; set; }
    public long LocationId { get; set; }
    public int ClientId { get; set; }

    public double TotalPrice => Quantity * UnitPrice;
    public string FormattedUnitPrice => UnitPrice.FormatIsk();
    public string FormattedTotalPrice => TotalPrice.FormatIsk();
}
