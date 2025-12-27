namespace WALLEve.Models.Wallet;

public class WalletFilterOptions
{
    public WalletTimeFilter TimePeriod { get; set; } = WalletTimeFilter.Week;
    public List<string> RefTypes { get; set; } = new();
    public string SearchQuery { get; set; } = string.Empty;
    public bool ShowOnlyMarketTransactions { get; set; }
}

public enum WalletTimeFilter
{
    Today,
    Week,
    Month,
    All
}
