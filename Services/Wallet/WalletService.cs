using WALLEve.Models.Esi.Wallet;
using WALLEve.Models.Wallet;
using WALLEve.Services.Authentication.Interfaces;
using WALLEve.Services.Esi.Interfaces;
using WALLEve.Services.Sde.Interfaces;
using WALLEve.Services.Wallet.Interfaces;

namespace WALLEve.Services.Wallet;

public class WalletService : IWalletService
{
    private readonly IEsiApiService _esiApi;
    private readonly ISdeUniverseService _sdeUniverse;
    private readonly IEveAuthenticationService _authService;
    private readonly ILogger<WalletService> _logger;

    public WalletService(
        IEsiApiService esiApi,
        ISdeUniverseService sdeUniverse,
        IEveAuthenticationService authService,
        ILogger<WalletService> logger)
    {
        _esiApi = esiApi;
        _sdeUniverse = sdeUniverse;
        _authService = authService;
        _logger = logger;
    }

    public async Task<List<WalletEntryViewModel>> GetCombinedWalletDataAsync()
    {
        var authState = await _authService.GetAuthStateAsync();
        if (authState?.CharacterId == null)
        {
            _logger.LogWarning("No authenticated character");
            return new List<WalletEntryViewModel>();
        }

        var characterId = authState.CharacterId;

        try
        {
            // Fetch journal and transactions in parallel
            var journalTask = _esiApi.GetWalletJournalAsync(characterId);
            var transactionsTask = _esiApi.GetWalletTransactionsAsync(characterId);

            await Task.WhenAll(journalTask, transactionsTask);

            var journal = await journalTask ?? new List<WalletJournalEntry>();
            var transactions = await transactionsTask ?? new List<WalletTransaction>();

            // Create lookup dictionary for transactions by journal_ref_id
            var transactionDict = transactions
                .Where(t => t.JournalRefId > 0)
                .ToDictionary(t => t.JournalRefId, t => t);

            // Combine data
            var combinedEntries = journal.Select(j => new WalletEntryViewModel
            {
                Id = j.Id,
                Date = j.Date,
                Amount = j.Amount ?? 0,
                Balance = j.Balance,
                RefType = j.RefType,
                Description = j.Description,
                Tax = j.Tax,
                TaxReceiverId = j.TaxReceiverId,
                FirstPartyId = j.FirstPartyId,
                SecondPartyId = j.SecondPartyId,
                TransactionDetails = transactionDict.TryGetValue(j.Id, out var trans)
                    ? new WalletTransactionDetails
                    {
                        IsBuy = trans.IsBuy,
                        TypeId = trans.TypeId,
                        Quantity = trans.Quantity,
                        UnitPrice = trans.UnitPrice,
                        LocationId = trans.LocationId,
                        ClientId = trans.ClientId
                    }
                    : null
            }).ToList();

            // Enrich with SDE data
            await EnrichWithSdeDataAsync(combinedEntries);

            return combinedEntries.OrderByDescending(e => e.Date).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting combined wallet data");
            return new List<WalletEntryViewModel>();
        }
    }

    private async Task EnrichWithSdeDataAsync(List<WalletEntryViewModel> entries)
    {
        try
        {
            var sdeAvailable = await _sdeUniverse.IsDatabaseAvailableAsync();
            if (!sdeAvailable)
            {
                _logger.LogWarning("SDE not available, skipping enrichment");
                return;
            }

            foreach (var entry in entries)
            {
                if (entry.TransactionDetails != null)
                {
                    entry.ItemName = await _sdeUniverse.GetTypeNameAsync(entry.TransactionDetails.TypeId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error enriching wallet entries with SDE data (continuing without)");
        }
    }

    public async Task<List<WalletEntryViewModel>> ApplyFiltersAsync(
        List<WalletEntryViewModel> entries,
        WalletFilterOptions filters)
    {
        var filtered = entries.AsEnumerable();

        // Time filter
        var cutoffDate = filters.TimePeriod switch
        {
            WalletTimeFilter.Today => DateTime.UtcNow.Date,
            WalletTimeFilter.Week => DateTime.UtcNow.AddDays(-7),
            WalletTimeFilter.Month => DateTime.UtcNow.AddMonths(-1),
            _ => DateTime.MinValue
        };

        if (cutoffDate != DateTime.MinValue)
        {
            filtered = filtered.Where(e => e.Date >= cutoffDate);
        }

        // RefType filter
        if (filters.RefTypes.Any())
        {
            filtered = filtered.Where(e => filters.RefTypes.Contains(e.RefType));
        }

        // Market-only filter
        if (filters.ShowOnlyMarketTransactions)
        {
            filtered = filtered.Where(e => e.TransactionDetails != null);
        }

        // Search filter
        if (!string.IsNullOrWhiteSpace(filters.SearchQuery))
        {
            var query = filters.SearchQuery.ToLower();
            filtered = filtered.Where(e =>
                e.ItemName?.ToLower().Contains(query) == true ||
                e.Description.ToLower().Contains(query) ||
                e.RefType.ToLower().Contains(query));
        }

        return await Task.FromResult(filtered.ToList());
    }
}
