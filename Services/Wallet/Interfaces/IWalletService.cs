using WALLEve.Models.Wallet;

namespace WALLEve.Services.Wallet.Interfaces;

public interface IWalletService
{
    /// <summary>
    /// Holte alle Wallet-Daten (Journal, Transaktionen, Orders, Historie) und
    /// liefert sie samt Datenqualität: ein fehlgeschlagener ESI-Abruf ist kein
    /// „leeres Konto".
    /// </summary>
    Task<WalletDataResult> GetCombinedWalletDataAsync();
    Task<List<WalletEntryViewModel>> ApplyFiltersAsync(List<WalletEntryViewModel> entries, WalletFilterOptions filters);
    List<GroupedMarketTransaction> GroupMarketTransactions(List<WalletEntryViewModel> entries);

    /// <summary>
    /// Holt den aktuellen Wallet-Balance direkt von ESI.
    /// </summary>
    Task<double?> GetWalletBalanceAsync();
}
