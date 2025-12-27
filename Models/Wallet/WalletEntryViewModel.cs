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
