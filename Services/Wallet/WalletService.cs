using Microsoft.Extensions.Options;
using WALLEve.Models.Configuration;
using WALLEve.Models.Database;
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
    private readonly IWalletLinkService _linkService;
    private readonly ILogger<WalletService> _logger;
    private readonly WalletOptions _walletOptions;

    public WalletService(
        IEsiApiService esiApi,
        ISdeUniverseService sdeUniverse,
        IEveAuthenticationService authService,
        IWalletLinkService linkService,
        ILogger<WalletService> logger,
        IOptions<WalletOptions> walletOptions)
    {
        _esiApi = esiApi;
        _sdeUniverse = sdeUniverse;
        _authService = authService;
        _linkService = linkService;
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

            // ===== Wallet Entry Linking =====
            // Apply persisted links from DB and calculate new ones
            await ApplyPersistedLinksAsync(characterId, combinedEntries, authState.CharacterName ?? "Unknown");

            // Build transaction chains (meta-structure based on links)
            BuildTransactionChains(combinedEntries);

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
                Tax = transaction.RelatedTransactions
                    .FirstOrDefault(t => t.Entry.RefType == "transaction_tax")
                    ?.Entry
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
                    .Where(o => o.Escrow.HasValue && Math.Abs(o.Escrow.Value - Math.Abs(entry.Amount)) < 0.01)
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
    /// Lädt persistierte Links aus DB, wendet sie an, und speichert neue Links
    /// </summary>
    private async Task ApplyPersistedLinksAsync(
        int characterId,
        List<WalletEntryViewModel> entries,
        string characterName)
    {
        try
        {
            // 1. Lade alle existierenden Links für diese Entries aus DB
            var entryIds = entries.Select(e => e.Id).ToList();
            var existingLinks = await _linkService.GetCharacterLinksByEntryIdsAsync(characterId, entryIds);

            _logger.LogInformation("Loaded {Count} existing links from database", existingLinks.Count);

            // 2. Wende existierende Links an
            ApplyLinksToEntries(entries, existingLinks);

            // 3. Berechne ALLE möglichen Links (nicht nur für Entries ohne Links!)
            // Entries können mehrere Link-Typen haben (Context + Tax + Escrow)
            // Duplikate werden in CalculateLinksForEntries() und SaveCharacterLinksAsync() gefiltert
            _logger.LogInformation("Calculating links for all entries to find missing links");
            var newLinks = CalculateLinksForEntries(characterId, entries, entries);

            if (newLinks.Any())
            {
                // 4. Speichere neue Links in DB (existierende werden automatisch gefiltert)
                await _linkService.SaveCharacterLinksAsync(characterId, characterName, newLinks);
                _logger.LogInformation("Saved {Count} new links to database", newLinks.Count);

                // 5. Wende neue Links an
                ApplyLinksToEntries(entries, newLinks);
            }
            else
            {
                _logger.LogInformation("No new links calculated - all links already exist");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying persisted links for character {CharacterId}", characterId);
            throw; // Re-throw to let caller handle the error
        }
    }

    /// <summary>
    /// Wendet Links auf Entries an (bidirectional)
    /// </summary>
    private void ApplyLinksToEntries(List<WalletEntryViewModel> entries, List<WalletEntryLink> links)
    {
        var entryDict = entries.ToDictionary(e => e.Id);

        foreach (var link in links)
        {
            if (link.IsManuallyRejected)
                continue; // Skip rejected links

            if (entryDict.TryGetValue(link.SourceEntryId, out var source) &&
                entryDict.TryGetValue(link.TargetEntryId, out var target))
            {
                // Create TransactionLink objects for bidirectional linking
                var sourceLink = new TransactionLink
                {
                    Type = link.Type,
                    Entry = target,
                    Confidence = ConvertConfidenceToPercentage(link.Confidence)
                };

                var targetLink = new TransactionLink
                {
                    Type = link.Type,
                    Entry = source,
                    Confidence = ConvertConfidenceToPercentage(link.Confidence)
                };

                // Add if not already present
                if (!source.RelatedTransactions.Any(t => t.Entry.Id == target.Id))
                    source.RelatedTransactions.Add(sourceLink);

                if (!target.RelatedTransactions.Any(t => t.Entry.Id == source.Id))
                    target.RelatedTransactions.Add(targetLink);
            }
        }
    }

    /// <summary>
    /// Konvertiert LinkConfidence enum zu Prozent-Wert
    /// </summary>
    private int ConvertConfidenceToPercentage(LinkConfidence confidence)
    {
        return confidence switch
        {
            LinkConfidence.Direct => 100,
            LinkConfidence.HeuristicHigh => 90,
            LinkConfidence.HeuristicMedium => 70,
            LinkConfidence.HeuristicLow => 50,
            LinkConfidence.Manual => 100,
            _ => 50
        };
    }

    /// <summary>
    /// Berechnet Links für neue Entries (Heuristik)
    /// </summary>
    private List<WalletEntryLink> CalculateLinksForEntries(
        int characterId,
        List<WalletEntryViewModel> newEntries,
        List<WalletEntryViewModel> allEntries)
    {
        var newLinks = new List<WalletEntryLink>();
        var marketRefTypes = new[] { "player_trading", "market_transaction", "market_escrow", "market_escrow_release" };

        // PHASE 1: Context-Based Linking
        foreach (var entry in newEntries)
        {
            if (entry.ContextId.HasValue && !string.IsNullOrEmpty(entry.ContextIdType))
            {
                var matchingEntry = allEntries.FirstOrDefault(e =>
                    e.Id != entry.Id &&
                    e.ContextId == entry.ContextId &&
                    e.ContextIdType == entry.ContextIdType);

                if (matchingEntry != null)
                {
                    // Only create link if not already exists (avoid bidirectional duplicates)
                    // Always create link with smaller ID as source to ensure consistency
                    var sourceId = Math.Min(entry.Id, matchingEntry.Id);
                    var targetId = Math.Max(entry.Id, matchingEntry.Id);

                    if (!newLinks.Any(l => l.SourceEntryId == sourceId && l.TargetEntryId == targetId))
                    {
                        newLinks.Add(new WalletEntryLink
                        {
                            SourceEntryId = sourceId,
                            TargetEntryId = targetId,
                            CharacterId = characterId,
                            Type = LinkType.DirectContextId,
                            Confidence = LinkConfidence.Direct,
                            CreatedBy = "Heuristic-Context"
                        });
                    }
                }
            }
        }

        // PHASE 2: Tax-Linking (transaction_tax)
        foreach (var entry in newEntries.Where(e => e.RefType == "transaction_tax"))
        {
            // Skip if already linked via ContextId
            if (entry.ContextId.HasValue && allEntries.Any(e =>
                e.Id != entry.Id &&
                e.ContextId == entry.ContextId &&
                e.ContextIdType == entry.ContextIdType))
            {
                continue; // Already handled in Phase 1
            }

            const int SCC_CORPORATION_ID = 1000132;
            if (entry.SecondPartyId != SCC_CORPORATION_ID)
                continue;

            var taxAmount = Math.Abs(entry.Amount);

            var matchingTransaction = allEntries
                .Where(e => e.RefType == "market_transaction")
                .Where(e => Math.Abs((e.Date - entry.Date).TotalSeconds) <= _walletOptions.TaxLinkingTimeWindowSeconds)
                .Where(e =>
                {
                    var transactionTotal = e.TransactionDetails?.TotalPrice ?? Math.Abs(e.Amount);
                    var expectedTax = transactionTotal * _walletOptions.TaxMaxPercentage;
                    var minTax = transactionTotal * _walletOptions.TaxMinPercentage;
                    return taxAmount >= minTax && taxAmount <= expectedTax * (1 + _walletOptions.TaxTolerancePercentage);
                })
                .OrderBy(e => Math.Abs((e.Date - entry.Date).TotalSeconds))
                .FirstOrDefault();

            if (matchingTransaction != null)
            {
                // Calculate confidence based on time difference
                var timeDiff = Math.Abs((entry.Date - matchingTransaction.Date).TotalSeconds);
                var confidence = timeDiff < 5 ? LinkConfidence.HeuristicHigh :
                                timeDiff < 30 ? LinkConfidence.HeuristicMedium :
                                LinkConfidence.HeuristicLow;

                // For tax links, source is always the tax entry, target is the transaction
                // Check if link already exists (in either direction)
                if (!newLinks.Any(l =>
                    (l.SourceEntryId == entry.Id && l.TargetEntryId == matchingTransaction.Id) ||
                    (l.SourceEntryId == matchingTransaction.Id && l.TargetEntryId == entry.Id)))
                {
                    newLinks.Add(new WalletEntryLink
                    {
                        SourceEntryId = entry.Id,
                        TargetEntryId = matchingTransaction.Id,
                        CharacterId = characterId,
                        Type = LinkType.HeuristicTax,
                        Confidence = confidence,
                        CreatedBy = "Heuristic-Tax"
                    });
                }
            }
        }

        // PHASE 3: Escrow-Pair Matching
        // NOTE: We need to check ALL escrow entries, not just newEntries
        // because escrow entries might already have Context-Links from Phase 1
        var allEscrowEntries = allEntries.Where(e => e.RefType == "market_escrow").ToList();
        var allEscrowReleases = allEntries.Where(e => e.RefType == "market_escrow_release").ToList();

        _logger.LogDebug("Escrow matching: {EscrowCount} escrow entries, {ReleaseCount} release entries",
            allEscrowEntries.Count, allEscrowReleases.Count);

        foreach (var escrow in allEscrowEntries)
        {
            var escrowAmount = Math.Abs(escrow.Amount);

            var matchingRelease = allEscrowReleases
                .Where(r => Math.Abs(Math.Abs(r.Amount) - escrowAmount) < 0.01) // Same amount (±1 cent tolerance)
                .Where(r => r.Date >= escrow.Date) // Release after Escrow
                .Where(r => (r.Date - escrow.Date).TotalDays <= 90) // Max 90 days (order lifetime)
                .OrderBy(r => Math.Abs((r.Date - escrow.Date).TotalSeconds)) // Closest in time
                .FirstOrDefault();

            if (matchingRelease != null)
            {
                // Use consistent ordering
                var sourceId = Math.Min(escrow.Id, matchingRelease.Id);
                var targetId = Math.Max(escrow.Id, matchingRelease.Id);

                // Check if this link already exists (both in newLinks and potentially in DB)
                // The SaveCharacterLinksAsync will also check DB, but we avoid creating duplicate objects
                if (!newLinks.Any(l =>
                    (l.SourceEntryId == sourceId && l.TargetEntryId == targetId) ||
                    (l.SourceEntryId == targetId && l.TargetEntryId == sourceId)))
                {
                    newLinks.Add(new WalletEntryLink
                    {
                        SourceEntryId = sourceId,
                        TargetEntryId = targetId,
                        CharacterId = characterId,
                        Type = LinkType.EscrowPair,
                        Confidence = LinkConfidence.HeuristicHigh,
                        CreatedBy = "Heuristic-Escrow"
                    });
                }
            }
        }

        return newLinks;
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
