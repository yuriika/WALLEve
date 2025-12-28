using Microsoft.Extensions.Options;
using WALLEve.Models.Configuration;
using WALLEve.Models.Esi.Markets;
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
    private readonly WalletOptions _walletOptions;

    public WalletService(
        IEsiApiService esiApi,
        ISdeUniverseService sdeUniverse,
        IEveAuthenticationService authService,
        ILogger<WalletService> logger,
        IOptions<WalletOptions> walletOptions)
    {
        _esiApi = esiApi;
        _sdeUniverse = sdeUniverse;
        _authService = authService;
        _logger = logger;
        _walletOptions = walletOptions.Value;
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
            // Fetch ALL pages of journal, transactions, and market orders in parallel
            var journalTask = _esiApi.GetAllWalletJournalPagesAsync(characterId);
            var transactionsTask = _esiApi.GetAllWalletTransactionsPagesAsync(characterId);
            var marketOrdersTask = _esiApi.GetMarketOrdersAsync(characterId);
            var marketOrderHistoryTask = _esiApi.GetMarketOrderHistoryAsync(characterId);

            await Task.WhenAll(journalTask, transactionsTask, marketOrdersTask, marketOrderHistoryTask);

            var journal = await journalTask ?? new List<WalletJournalEntry>();
            var transactions = await transactionsTask ?? new List<WalletTransaction>();
            var marketOrders = await marketOrdersTask ?? new List<MarketOrder>();
            var marketOrderHistory = await marketOrderHistoryTask ?? new List<MarketOrderHistory>();

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

            // Link market orders to escrow entries
            LinkMarketOrders(combinedEntries, marketOrders, marketOrderHistory);

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

            // PHASE 1: Context-Based Direct Linking (für ALLE RefTypes)
            LinkViaContextId(entries);

            // PHASE 2: Heuristic Tax Linking (Fallback für transaction_tax ohne ContextId)
            foreach (var entry in entries)
            {
                if (entry.RefType == "transaction_tax")
                {
                    taxEntriesCount++;

                    // Prüfe ob bereits via ContextId verlinkt
                    var alreadyLinked = entry.RelatedTransactions.Any(l => l.Type == LinkType.DirectContextId);
                    if (alreadyLinked)
                    {
                        linkedCount++;
                        _logger.LogDebug("Tax Entry {TaxId} already linked via ContextId", entry.Id);
                        continue;
                    }

                    WalletEntryViewModel? matchingTransaction = null;

                    // Use heuristics:
                    // 1. Market transaction that happened shortly before/after (within configured time window)
                    // 2. Tax amount is ~2-8% of transaction total (accounting skill reduces tax)
                    // 3. SecondPartyId must be 1000132 (SCC - Secure Commerce Commission)

                    // Validate SecondPartyId for tax entries
                    const int SCC_CORPORATION_ID = 1000132;
                    if (entry.SecondPartyId != SCC_CORPORATION_ID)
                    {
                        _logger.LogWarning("Tax Entry {TaxId} has invalid SecondPartyId {SecondPartyId}, expected {Expected} (SCC)",
                            entry.Id, entry.SecondPartyId, SCC_CORPORATION_ID);
                        continue; // Skip this entry
                    }

                    var taxAmount = Math.Abs(entry.Amount);

                    matchingTransaction = entries
                        .Where(e => marketRefTypes.Contains(e.RefType))
                        .Where(e => Math.Abs((e.Date - entry.Date).TotalSeconds) <= _walletOptions.TaxLinkingTimeWindowSeconds)
                        .Where(e =>
                        {
                            // Use TransactionDetails.TotalPrice if available, otherwise use journal Amount
                            var transactionTotal = e.TransactionDetails?.TotalPrice ?? Math.Abs(e.Amount);

                            // Calculate expected tax range
                            var expectedTax = transactionTotal * _walletOptions.TaxMaxPercentage;
                            var minTax = transactionTotal * _walletOptions.TaxMinPercentage;

                            // Basic range check with tolerance
                            return taxAmount >= minTax && taxAmount <= expectedTax * (1 + _walletOptions.TaxTolerancePercentage);
                        })
                        .OrderBy(e => Math.Abs((e.Date - entry.Date).TotalSeconds)) // Closest in time
                        .FirstOrDefault();

                    if (matchingTransaction != null)
                    {
                        // Bidirectional linking via new RelatedTransactions list
                        entry.RelatedTransactions.Add(new TransactionLink
                        {
                            Type = LinkType.HeuristicTax,
                            Entry = matchingTransaction,
                            Confidence = 85
                        });

                        matchingTransaction.RelatedTransactions.Add(new TransactionLink
                        {
                            Type = LinkType.HeuristicTax,
                            Entry = entry,
                            Confidence = 85
                        });

                        // Backward compatibility: behalte alte RelatedTransaction
                        entry.RelatedTransaction = matchingTransaction;
                        matchingTransaction.RelatedTransaction = entry;

                        linkedCount++;
                        _logger.LogDebug("Tax Entry {TaxId} ({TaxAmount:N2} ISK) linked to transaction {TransactionId} (Gross: {Gross:N2} ISK)",
                            entry.Id, Math.Abs(entry.Amount), matchingTransaction.Id, matchingTransaction.TransactionDetails?.TotalPrice);
                    }
                    else
                    {
                        _logger.LogDebug("Tax Entry {TaxId} ({TaxAmount:N2} ISK) - no matching transaction found",
                            entry.Id, Math.Abs(entry.Amount));
                    }
                }
            }

            var unlinkedCount = taxEntriesCount - linkedCount;
            _logger.LogInformation("Transaction linking complete: {TaxCount} tax entries, {LinkedCount} linked, {UnlinkedCount} unlinked",
                taxEntriesCount, linkedCount, unlinkedCount);

            // PHASE 3: Escrow Pair Matching
            LinkEscrowPairs(entries);

            // PHASE 4: Build Transaction Chains
            BuildTransactionChains(entries);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error linking related transactions (continuing without): {Message}", ex.Message);
        }
    }

    /// <summary>
    /// Verknüpft Einträge über ContextId (direkte ESI-Verlinkung)
    /// </summary>
    private void LinkViaContextId(List<WalletEntryViewModel> entries)
    {
        // RefTypes die ContextId nutzen können
        var contextLinkableTypes = new[]
        {
            "transaction_tax",
            "market_escrow",
            "market_escrow_release",
            "brokers_fee"
        };

        var linkedCount = 0;

        foreach (var entry in entries)
        {
            if (!contextLinkableTypes.Contains(entry.RefType))
                continue;

            if (!entry.ContextId.HasValue || string.IsNullOrEmpty(entry.ContextIdType))
                continue;

            // Nur market_transaction_id ist für Journal Entry Verlinkung relevant
            if (entry.ContextIdType != "market_transaction_id")
                continue;

            // Finde Journal Entry mit dieser ID
            var relatedEntry = entries.FirstOrDefault(e => e.Id == entry.ContextId.Value);

            if (relatedEntry != null)
            {
                // Bidirectional linking
                entry.RelatedTransactions.Add(new TransactionLink
                {
                    Type = LinkType.DirectContextId,
                    Entry = relatedEntry,
                    Confidence = 100
                });

                relatedEntry.RelatedTransactions.Add(new TransactionLink
                {
                    Type = LinkType.DirectContextId,
                    Entry = entry,
                    Confidence = 100
                });

                linkedCount++;

                _logger.LogDebug("ContextId link: {RefType} {Id} → {RelatedRefType} {RelatedId}",
                    entry.RefType, entry.Id, relatedEntry.RefType, relatedEntry.Id);
            }
        }

        _logger.LogInformation("Context-based linking: {Count} entries linked via ContextId", linkedCount);
    }

    /// <summary>
    /// Baut vollständige Transaktionsketten (Order → Transaction → Tax → EscrowRelease)
    /// </summary>
    private void BuildTransactionChains(List<WalletEntryViewModel> entries)
    {
        try
        {
            var chainsBuilt = 0;

            // Starte mit market_transactions als Anker
            var marketTransactions = entries.Where(e => e.RefType == "market_transaction").ToList();

            foreach (var transaction in marketTransactions)
            {
                var chain = new TransactionChain
                {
                    Transaction = transaction
                };

                // Finde zugehörige Tax Entry
                chain.Tax = FindLinkedEntry(transaction, "transaction_tax");

                // Finde zugehörige Escrow Release
                chain.EscrowRelease = FindLinkedEntry(transaction, "market_escrow_release");

                // Finde ursprünglichen Escrow Entry
                if (chain.EscrowRelease != null)
                {
                    chain.Root = FindLinkedEntry(chain.EscrowRelease, "market_escrow");
                }

                // Finde Broker Fee Modifikationen
                chain.BrokerFeeModifications = entries
                    .Where(e => e.RefType == "brokers_fee")
                    .Where(e => IsRelatedToChain(e, chain))
                    .ToList();

                // Setze Chain in allen beteiligten Einträgen
                transaction.Chain = chain;
                if (chain.Tax != null) chain.Tax.Chain = chain;
                if (chain.EscrowRelease != null) chain.EscrowRelease.Chain = chain;
                if (chain.Root != null) chain.Root.Chain = chain;

                foreach (var fee in chain.BrokerFeeModifications)
                    fee.Chain = chain;

                chainsBuilt++;
            }

            _logger.LogInformation("Transaction chains: {Count} chains built", chainsBuilt);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error building transaction chains: {Message}", ex.Message);
        }
    }

    /// <summary>
    /// Findet verknüpften Entry eines bestimmten RefTypes
    /// </summary>
    private WalletEntryViewModel? FindLinkedEntry(WalletEntryViewModel entry, string refType)
    {
        return entry.RelatedTransactions
            .FirstOrDefault(l => l.Entry.RefType == refType)
            ?.Entry;
    }

    /// <summary>
    /// Prüft ob ein Entry zu einer Chain gehört (über zeitliche Nähe und ContextId)
    /// </summary>
    private bool IsRelatedToChain(WalletEntryViewModel entry, TransactionChain chain)
    {
        if (chain.Transaction == null)
            return false;

        // Zeitlich nah (innerhalb 1 Stunde)
        var timeDiff = Math.Abs((entry.Date - chain.Transaction.Date).TotalHours);
        if (timeDiff > 1)
            return false;

        // Gleiche ContextId oder ähnlicher Betrag
        if (entry.ContextId.HasValue && chain.Transaction.ContextId.HasValue)
        {
            return entry.ContextId.Value == chain.Transaction.ContextId.Value;
        }

        return false;
    }

    /// <summary>
    /// Verknüpft market_escrow mit zugehörigen market_escrow_release Einträgen
    /// </summary>
    private void LinkEscrowPairs(List<WalletEntryViewModel> entries)
    {
        try
        {
            var escrowEntries = entries.Where(e => e.RefType == "market_escrow").ToList();
            var escrowReleases = entries.Where(e => e.RefType == "market_escrow_release").ToList();

            var pairedCount = 0;

            foreach (var escrow in escrowEntries)
            {
                // Prüfe ob bereits via ContextId verlinkt
                var alreadyLinked = escrow.RelatedTransactions
                    .Any(l => l.Type == LinkType.DirectContextId && l.Entry.RefType == "market_escrow_release");

                if (alreadyLinked)
                {
                    pairedCount++;
                    continue;
                }

                // Finde matching Release Entry
                var escrowAmount = Math.Abs(escrow.Amount);

                var matchingRelease = escrowReleases
                    .Where(r => Math.Abs(Math.Abs(r.Amount) - escrowAmount) < 0.01) // Gleicher Betrag (±1 Cent Toleranz)
                    .Where(r => r.Date >= escrow.Date) // Release nach Escrow
                    .Where(r => (r.Date - escrow.Date).TotalDays <= 90) // Max 90 Tage (Order-Laufzeit)
                    .Where(r => !r.RelatedTransactions.Any(l => l.Type == LinkType.EscrowPair)) // Noch nicht gepaart
                    .OrderBy(r => Math.Abs((r.Date - escrow.Date).TotalSeconds)) // Zeitlich nächster
                    .FirstOrDefault();

                if (matchingRelease != null)
                {
                    // Bestimme Status: Fulfilled (negativ) oder Cancelled (positiv)
                    var status = matchingRelease.Amount < 0 ? "Fulfilled" : "Cancelled";

                    // Bidirectional linking
                    escrow.RelatedTransactions.Add(new TransactionLink
                    {
                        Type = LinkType.EscrowPair,
                        Entry = matchingRelease,
                        Confidence = 90,
                        Metadata = new Dictionary<string, object>
                        {
                            { "Status", status },
                            { "TimeDifference", (matchingRelease.Date - escrow.Date).TotalHours }
                        }
                    });

                    matchingRelease.RelatedTransactions.Add(new TransactionLink
                    {
                        Type = LinkType.EscrowPair,
                        Entry = escrow,
                        Confidence = 90,
                        Metadata = new Dictionary<string, object>
                        {
                            { "Status", status },
                            { "TimeDifference", (matchingRelease.Date - escrow.Date).TotalHours }
                        }
                    });

                    pairedCount++;

                    _logger.LogDebug("Escrow pair: {EscrowId} ({Amount:N2} ISK) ↔ {ReleaseId} ({Status})",
                        escrow.Id, escrowAmount, matchingRelease.Id, status);
                }
            }

            _logger.LogInformation("Escrow pair matching: {Count} pairs linked", pairedCount);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error linking escrow pairs: {Message}", ex.Message);
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

    /// <summary>
    /// Verknüpft Market Orders (aktiv und historisch) mit Journal Entries
    /// </summary>
    private void LinkMarketOrders(
        List<WalletEntryViewModel> entries,
        List<MarketOrder> activeOrders,
        List<MarketOrderHistory> orderHistory)
    {
        try
        {
            var linkedCount = 0;
            var escrowRefTypes = new[] { "market_escrow", "market_escrow_release" };

            foreach (var entry in entries.Where(e => escrowRefTypes.Contains(e.RefType)))
            {
                MarketOrderInfo? linkedOrder = null;

                // Match mit Active Orders (basierend auf Escrow-Amount und Zeitstempel)
                var matchingActiveOrder = activeOrders
                    .Where(o => o.Escrow.HasValue)
                    .Where(o => Math.Abs(o.Escrow.Value - Math.Abs(entry.Amount)) < 0.01)
                    .Where(o => Math.Abs((o.Issued - entry.Date).TotalSeconds) < 60)
                    .FirstOrDefault();

                if (matchingActiveOrder != null)
                {
                    linkedOrder = new MarketOrderInfo
                    {
                        OrderId = matchingActiveOrder.OrderId,
                        TypeId = matchingActiveOrder.TypeId,
                        Price = matchingActiveOrder.Price,
                        VolumeTotal = matchingActiveOrder.VolumeTotal,
                        VolumeRemain = matchingActiveOrder.VolumeRemain,
                        IsBuyOrder = matchingActiveOrder.IsBuyOrder,
                        Issued = matchingActiveOrder.Issued,
                        Status = "Active"
                    };
                }
                else
                {
                    // Fallback: Match mit Order History (basierend auf Zeitstempel und TypeId aus Context)
                    var matchingHistoricalOrder = orderHistory
                        .Where(o => Math.Abs((o.Issued - entry.Date).TotalSeconds) < 60)
                        .Where(o => entry.TransactionDetails?.TypeId == o.TypeId || entry.ContextId == o.OrderId)
                        .OrderBy(o => Math.Abs((o.Issued - entry.Date).TotalSeconds))
                        .FirstOrDefault();

                    if (matchingHistoricalOrder != null)
                    {
                        linkedOrder = new MarketOrderInfo
                        {
                            OrderId = matchingHistoricalOrder.OrderId,
                            TypeId = matchingHistoricalOrder.TypeId,
                            Price = matchingHistoricalOrder.Price,
                            VolumeTotal = matchingHistoricalOrder.VolumeTotal,
                            VolumeRemain = matchingHistoricalOrder.VolumeRemain,
                            IsBuyOrder = matchingHistoricalOrder.IsBuyOrder,
                            Issued = matchingHistoricalOrder.Issued,
                            Status = GetOrderStatus(matchingHistoricalOrder.State)
                        };
                    }
                }

                if (linkedOrder != null)
                {
                    entry.LinkedMarketOrder = linkedOrder;
                    linkedCount++;
                    _logger.LogDebug("Market Escrow Entry {EntryId} linked to Order {OrderId} (Status: {Status})",
                        entry.Id, linkedOrder.OrderId, linkedOrder.Status);
                }
            }

            _logger.LogInformation("Linked {Count} market escrow entries to market orders", linkedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error linking market orders to journal entries");
        }
    }

    /// <summary>
    /// Konvertiert ESI Order State zu benutzerfreundlichem Status
    /// </summary>
    private static string GetOrderStatus(string state)
    {
        return state switch
        {
            "cancelled" => "Cancelled",
            "expired" => "Expired",
            "fulfilled" => "Completed",
            _ => "Unknown"
        };
    }

    /// <summary>
    /// Berechnet die Tax-Rate basierend auf Accounting Skill Level
    /// Accounting Skill reduziert Tax um 0.4% pro Level (von 8% auf 6%)
    /// </summary>
    /// <param name="accountingSkillLevel">Accounting Skill Level (0-5)</param>
    /// <returns>Tax-Rate als Decimal (z.B. 0.08 für 8%)</returns>
    private static double CalculateTaxRateFromSkill(int accountingSkillLevel)
    {
        const double baseTaxRate = 0.08; // 8% without skills
        const double reductionPerLevel = 0.004; // 0.4% reduction per level

        var skillLevel = Math.Clamp(accountingSkillLevel, 0, 5);
        return baseTaxRate - (skillLevel * reductionPerLevel);
    }
}
