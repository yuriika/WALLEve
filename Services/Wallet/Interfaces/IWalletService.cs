using WALLEve.Models.Wallet;

namespace WALLEve.Services.Wallet.Interfaces;

public interface IWalletService
{
    Task<List<WalletEntryViewModel>> GetCombinedWalletDataAsync();
    Task<List<WalletEntryViewModel>> ApplyFiltersAsync(List<WalletEntryViewModel> entries, WalletFilterOptions filters);
    List<GroupedMarketTransaction> GroupMarketTransactions(List<WalletEntryViewModel> entries);

    /// <summary>
    /// Holt den aktuellen Wallet-Balance direkt von ESI.
    /// </summary>
    Task<double?> GetWalletBalanceAsync();
}
