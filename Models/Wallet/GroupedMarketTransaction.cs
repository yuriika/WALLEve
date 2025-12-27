using WALLEve.Extensions;

namespace WALLEve.Models.Wallet;

/// <summary>
/// Represents a market transaction grouped with its associated tax entry
/// </summary>
public class GroupedMarketTransaction
{
    public WalletEntryViewModel Transaction { get; set; } = null!;
    public WalletEntryViewModel? Tax { get; set; }

    // Calculated properties
    public DateTime Date => Transaction.Date;
    public string? ItemName => Transaction.ItemName;
    public string Description => Transaction.Description;

    /// <summary>
    /// The gross transaction amount (what was paid/received for the items)
    /// </summary>
    public double GrossAmount => Transaction.Amount;

    /// <summary>
    /// The tax amount (always negative)
    /// </summary>
    public double TaxAmount => Tax?.Amount ?? 0;

    /// <summary>
    /// The net amount (what the player actually gained/lost)
    /// For sales: GrossAmount + TaxAmount (positive - tax = net gain)
    /// For purchases: GrossAmount + TaxAmount (negative - tax = net loss)
    /// </summary>
    public double NetAmount => GrossAmount + TaxAmount;

    public string FormattedGrossAmount => GrossAmount.FormatIsk();
    public string FormattedTaxAmount => TaxAmount.FormatIsk();
    public string FormattedNetAmount => NetAmount.FormatIsk();

    public WalletTransactionDetails? TransactionDetails => Transaction.TransactionDetails;

    public bool IsExpanded { get; set; }
}
