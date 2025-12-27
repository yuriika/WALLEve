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
                ContextId = j.ContextId,
                ContextIdType = j.ContextIdType,
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

            // Link tax entries to their original transactions
            LinkRelatedTransactions(combinedEntries);

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

    private void LinkRelatedTransactions(List<WalletEntryViewModel> entries)
    {
        try
        {
            var taxEntriesCount = 0;
            var linkedCount = 0;
            var marketRefTypes = new[] { "player_trading", "market_transaction", "market_escrow", "market_escrow_release" };

            // Since ESI doesn't provide context_id for transaction_tax, we use heuristics
            // to link tax entries to their likely source transactions
            foreach (var entry in entries)
            {
                if (entry.RefType == "transaction_tax")
                {
                    taxEntriesCount++;

                    WalletEntryViewModel? matchingTransaction = null;

                    // First: Try direct linking via context_id if available
                    if (entry.ContextId.HasValue && entry.ContextIdType == "market_transaction_id")
                    {
                        // context_id directly references the transaction journal entry ID
                        matchingTransaction = entries.FirstOrDefault(e => e.Id == entry.ContextId.Value);
                    }

                    // Fallback: Use heuristics if direct linking didn't work
                    if (matchingTransaction == null)
                    {
                        // Find matching transaction using heuristics:
                        // 1. Market transaction that happened shortly before/after (within 10 seconds)
                        // 2. Tax amount is ~2-8% of transaction total (accounting skill reduces tax)
                        // Note: SecondPartyId for tax entries is 1000132 (SCC), not the trading partner!

                        var taxAmount = Math.Abs(entry.Amount);

                        matchingTransaction = entries
                            .Where(e => marketRefTypes.Contains(e.RefType))
                            .Where(e => Math.Abs((e.Date - entry.Date).TotalSeconds) <= 10) // Within 10 seconds (before or after)
                            .Where(e =>
                            {
                                // Use TransactionDetails.TotalPrice if available, otherwise use journal Amount
                                var transactionTotal = e.TransactionDetails?.TotalPrice ?? Math.Abs(e.Amount);
                                var expectedTax = transactionTotal * 0.08; // Max tax is 8%
                                var minTax = transactionTotal * 0.02; // Min tax with max skills
                                return taxAmount >= minTax && taxAmount <= expectedTax * 1.1; // 10% tolerance
                            })
                            .OrderBy(e => Math.Abs((e.Date - entry.Date).TotalSeconds)) // Closest in time
                            .FirstOrDefault();
                    }

                    if (matchingTransaction != null)
                    {
                        // Bidirectional linking: Tax -> Transaction and Transaction -> Tax
                        entry.RelatedTransaction = matchingTransaction;
                        matchingTransaction.RelatedTransaction = entry;
                        linkedCount++;
                        Console.WriteLine($"Tax Entry {entry.Id} ({entry.Amount:N2} ISK) linked to transaction {matchingTransaction.Id} ({matchingTransaction.TransactionDetails?.TotalPrice:N2} ISK)");
                    }
                    else
                    {
                        Console.WriteLine($"Tax Entry {entry.Id} ({entry.Amount:N2} ISK) - no matching transaction found");
                    }
                }
            }

            Console.WriteLine($"Transaction linking: Found {taxEntriesCount} tax entries, linked {linkedCount}");
            _logger.LogInformation("Transaction linking: Found {TaxCount} tax entries, linked {LinkedCount}",
                taxEntriesCount, linkedCount);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error linking transactions: {ex.Message}");
            _logger.LogWarning(ex, "Error linking related transactions (continuing without)");
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
            var marketRefTypes = new[] { "player_trading", "market_transaction", "market_escrow", "market_escrow_release" };
            filtered = filtered.Where(e => marketRefTypes.Contains(e.RefType));
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

    public List<GroupedMarketTransaction> GroupMarketTransactions(List<WalletEntryViewModel> entries)
    {
        var marketRefTypes = new[] { "player_trading", "market_transaction", "market_escrow", "market_escrow_release" };

        // Get all market transactions that have already been linked with taxes
        var marketTransactions = entries
            .Where(e => marketRefTypes.Contains(e.RefType))
            .OrderByDescending(e => e.Date)
            .ToList();

        var groupedTransactions = new List<GroupedMarketTransaction>();

        foreach (var transaction in marketTransactions)
        {
            // Skip if this transaction is already part of a group (as a tax entry)
            if (groupedTransactions.Any(g => g.Tax?.Id == transaction.Id))
                continue;

            var grouped = new GroupedMarketTransaction
            {
                Transaction = transaction,
                Tax = transaction.RelatedTransaction?.RefType == "transaction_tax"
                    ? transaction.RelatedTransaction
                    : null
            };

            groupedTransactions.Add(grouped);
        }

        return groupedTransactions;
    }
}
