using Microsoft.EntityFrameworkCore;
using WALLEve.Data;
using WALLEve.Models.Database;
using WALLEve.Services.Authentication.Interfaces;
using WALLEve.Services.Esi.Interfaces;
using WALLEve.Services.Market.Interfaces;

namespace WALLEve.Services.Market;

/// <summary>
/// Market Analysis Service mit Heuristik statt LLM (KI-Anbindung folgt später separat).
/// Analysiert die BESTANDS-Items des Charakters: Verkaufssimulation pro Item mit
/// echten Skills (FeeCalculator), Vergleich Einkaufspreis (Cost Basis) vs. Marktpreis.
/// Der Service ist bewusst frei von jeder LLM-Abhängigkeit: weder DI-Konstruktion
/// noch Laufzeit benötigen einen erreichbaren Ollama-Server (Issue #32).
/// </summary>
public class MarketAnalysisService : IMarketAnalysisService
{
    /// <summary>Version der Bestands-Verkaufsanalyse — ändern, wenn sich die Heuristik ändert.</summary>
    public const string AlgorithmVersionInventorySell = "inventory-sell-v1";

    private readonly WalletDbContext _dbContext;
    private readonly IFeeCalculatorService _feeCalculator;
    private readonly IInventoryService _inventoryService;
    private readonly IEveAuthenticationService _authService;
    private readonly IEsiApiService _esiApi;
    private readonly ILogger<MarketAnalysisService> _logger;

    public MarketAnalysisService(
        WalletDbContext dbContext,
        IFeeCalculatorService feeCalculator,
        IInventoryService inventoryService,
        IEveAuthenticationService authService,
        IEsiApiService esiApi,
        ILogger<MarketAnalysisService> logger)
    {
        _dbContext = dbContext;
        _feeCalculator = feeCalculator;
        _inventoryService = inventoryService;
        _authService = authService;
        _esiApi = esiApi;
        _logger = logger;
    }

    /// <summary>
    /// Analysiert die BESTANDS-Items des Charakters und findet Verkaufs-Opportunities.
    /// Pro Item mit Cost Basis + Marktpreis: Netto-Gewinn nach Fees (echte Char-Skills),
    /// ROI, Break-even. Es entstehen KEINE Duplikate (eine aktive Opportunity pro TypeId).
    /// </summary>
    /// <returns>Aktive Trading Opportunities (inventory_sell), sortiert nach Netto-Gewinn</returns>
    public async Task<List<TradingOpportunity>> AnalyzeMarketDataAsync()
    {
        try
        {
            _logger.LogInformation("Starting inventory-based market analysis...");

            // Abgelaufene Opportunities entfernen (Hygiene, verhindert DB-Wachstum)
            var expired = await _dbContext.TradingOpportunities
                .Where(o => o.ExpiresAt < DateTime.UtcNow)
                .ToListAsync();
            if (expired.Count > 0)
            {
                _dbContext.TradingOpportunities.RemoveRange(expired);
                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("Removed {Count} expired opportunities", expired.Count);
            }

            var authState = await _authService.GetAuthStateAsync();
            if (authState?.IsValid != true)
            {
                _logger.LogWarning("No authenticated character — skipping inventory analysis");
                return new List<TradingOpportunity>();
            }

            // Aktive inventory_sell-Opportunities als Dedup-Basis
            var activeKeys = await _dbContext.TradingOpportunities
                .Where(o => o.ExpiresAt >= DateTime.UtcNow && o.Status == "active"
                         && o.OpportunityType == "inventory_sell"
                         && o.CharacterId == authState.CharacterId)
                .Select(o => o.TypeId)
                .ToListAsync();
            var dedupSet = activeKeys.ToHashSet();

            var items = await _inventoryService.GetInventoryAsync(authState.CharacterId);
            var skills = await _esiApi.GetCharacterSkillsAsync();

            // Bestehende AKTIVE inventory_sell-Opportunities EINMAL laden (statt
            // FirstOrDefaultAsync pro Item → N+1-Problem bei 500+ Bestands-Items).
            var existingByType = await _dbContext.TradingOpportunities
                .Where(o => o.OpportunityType == "inventory_sell"
                         && o.CharacterId == authState.CharacterId
                         && o.Status == "active")
                .ToListAsync();
            var existingMap = existingByType.ToDictionary(o => o.TypeId);

            // Analysierbar ist jedes Item mit Cost Basis. Ob ein ausführbarer
            // Verkaufs-Quote vorliegt, entscheidet der Lauf je Item: ein fehlender
            // Quote (fremde Region, veraltet, partial) entfernt eine nicht mehr
            // gültige Empfehlung, statt sie aktiv weiter auszuliefern.
            var analyzable = items
                .Where(i => i.CostBasisPerUnit.HasValue)
                .ToList();

            var opportunities = new List<TradingOpportunity>();
            var updated = 0;

            // Entfernt eine bestehende aktive Opportunity zu einem TypeId, wenn die
            // ortsgebundene Empfehlung wegfällt (blockierter Ort / kein Gewinn mehr).
            // Ohne das würde die alte, nicht mehr gültige Empfehlung aktiv bleiben und
            // über GetActiveOpportunitiesAsync weiter zurückgegeben werden.
            void RemoveStaleOpportunity(int typeId, string reason)
            {
                if (existingMap.Remove(typeId, out var stale))
                {
                    _dbContext.TradingOpportunities.Remove(stale);
                    _logger.LogInformation(
                        "Removed stale active opportunity for type {TypeId}: {Reason}", typeId, reason);
                }
            }

            foreach (var item in analyzable)
            {
                // Ohne ausführbaren Verkaufs-Quote (kein Quote in der Region des
                // Assets, veraltet oder unvollständig) gibt es keine Empfehlung.
                // Eine frühere, aktive Opportunity zu diesem Typ wird entfernt.
                if (!item.BestSellPrice.HasValue)
                {
                    _logger.LogDebug(
                        "Inventory item {TypeId} ({TypeName}) has no executable sell quote — stale opportunity removed",
                        item.TypeId, item.TypeName);
                    RemoveStaleOpportunity(item.TypeId, "no executable sell quote");
                    continue;
                }

                // Ortsgebundene Verkaufsprojektion (#28): NUR aufgelöste Handelsplätze
                // (Stations) sind verkaufbar. Mengen mehrerer Orte werden NICHT zu einem
                // Stapel verschmolzen; Orte ohne aufgelösten Handelsplatz (Container,
                // System, unbekannt) blockieren die ortsgebundene Empfehlung.
                var sellableContexts = item.SellContexts
                    .Where(c => !c.IsBlocked && c.Quantity > 0)
                    .OrderByDescending(c => c.Quantity)
                    .ToList();

                if (sellableContexts.Count == 0)
                {
                    _logger.LogDebug(
                        "Inventory item {TypeId} ({TypeName}) has no resolved market venue — location-bound opportunity blocked",
                        item.TypeId, item.TypeName);
                    RemoveStaleOpportunity(item.TypeId, "no resolved market venue");
                    continue;
                }

                // Invariante: eine aktive Opportunity pro TypeId (Dedup unverändert);
                // sie ist an den größten aufgelösten Handelsplatz gebunden und deckt
                // ausschließlich dessen Menge ab.
                var context = sellableContexts[0];

                // Verkaufssimulation mit echten Char-Skills.
                // Invariante (#4): Die gespeicherte Cost Basis enthält die verknüpften
                // Erwerbskosten bereits genau einmal — beim Verkauf darf KEINE erneute
                // Buy-Brokergebühr aufgeschlagen werden. Erwerbskosten = Basis × ortsgebundene Menge.
                var sellResult = _feeCalculator.CalculateSellProceeds(item.BestSellPrice!.Value, context.Quantity, skills);
                var acquisitionCost = item.CostBasisPerUnit!.Value * context.Quantity;

                var netProfit = sellResult.NetAmount - acquisitionCost;
                var roi = acquisitionCost > 0 ? (netProfit / acquisitionCost) * 100 : 0;
                var breakEven = _feeCalculator.CalculateBreakEvenSellPriceForStoredBasis(item.CostBasisPerUnit.Value, skills);

                // Nur echte Gewinn-Opportunitäten (Verkaufspreis über Break-even)
                if (netProfit <= 0)
                {
                    RemoveStaleOpportunity(item.TypeId, "no longer profitable");
                    continue;
                }

                var reasoning = sellableContexts.Count > 1
                    ? $"Ortsgebunden ({context.LocationLabel}): {context.Quantity:N0} von {item.TotalQuantity:N0} Einheiten — Verkauf bei {item.BestSellPrice.Value:N2} ISK bringt netto {netProfit:N0} ISK (ROI {roi:F1}%, Break-even {breakEven:N2} ISK). Übrige Orte separat prüfen."
                    : $"Ortsgebunden ({context.LocationLabel}): {context.Quantity:N0} × {item.TypeName} — Verkauf bei {item.BestSellPrice.Value:N2} ISK bringt netto {netProfit:N0} ISK (ROI {roi:F1}%, Break-even {breakEven:N2} ISK).";

                // Ehrliche Provenienz statt erfundener AI-Confidence (#33):
                // Score ist ein dokumentierter Heuristik-Wert, Evidenz nennt die konkreten
                // Zahlen, Datenqualität spiegelt die Cost-Basis-Herkunft (Echt/Manuell =
                // gesichert, Geschätzt = partial). Es gibt KEINE AI-Angabe.
                var score = Math.Clamp(55 + (roi * 1.5), 55, 95);
                var dataQuality = item.CostBasisSourceLabel is "Echt" or "Manuell" ? "complete" : "partial";

                existingMap.TryGetValue(item.TypeId, out var existing);

                if (existing == null)
                {
                    if (dedupSet.Contains(item.TypeId)) continue;

                    var opportunity = new TradingOpportunity
                    {
                        CharacterId = authState.CharacterId,
                        TypeId = item.TypeId,
                        OpportunityType = "inventory_sell",
                        BuyPrice = item.CostBasisPerUnit,
                        SellPrice = item.BestSellPrice,
                        SellLocationId = context.LocationId,
                        SellSystemId = null,
                        EstimatedProfit = netProfit,
                        RequiredCapital = acquisitionCost,
                        Score = score,
                        Provenance = TradingOpportunity.ProvenanceHeuristic,
                        AlgorithmVersion = AlgorithmVersionInventorySell,
                        DataQuality = dataQuality,
                        Evidence = reasoning,
                        DetectedAt = DateTime.UtcNow,
                        ExpiresAt = DateTime.UtcNow.AddHours(1),
                        Status = "active"
                    };
                    _dbContext.TradingOpportunities.Add(opportunity);
                    opportunities.Add(opportunity);
                    existingMap[item.TypeId] = opportunity; // für spätere Items im selben Lauf
                }
                else
                {
                    // Bestehende Opportunity mit aktuellen Zahlen aktualisieren
                    existing.SellPrice = item.BestSellPrice;
                    existing.SellLocationId = context.LocationId;
                    existing.EstimatedProfit = netProfit;
                    existing.RequiredCapital = acquisitionCost;
                    existing.Score = score;
                    existing.Provenance = TradingOpportunity.ProvenanceHeuristic;
                    existing.AlgorithmVersion = AlgorithmVersionInventorySell;
                    existing.DataQuality = dataQuality;
                    existing.Evidence = reasoning;
                    existing.ExpiresAt = DateTime.UtcNow.AddHours(1);
                    existing.DetectedAt = DateTime.UtcNow;
                    updated++;
                }
            }

            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("Inventory analysis done: {New} new opportunities, {Updated} updated",
                opportunities.Count, updated);

            return await GetActiveOpportunitiesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing market data");
            return new List<TradingOpportunity>();
        }
    }

    /// <summary>
    /// Liest die aktiven Trading Opportunities aus der DB (kein Schreiben!).
    /// </summary>
    public async Task<List<TradingOpportunity>> GetActiveOpportunitiesAsync(int? characterId = null)
    {
        try
        {
            var query = _dbContext.TradingOpportunities
                .Where(o => o.ExpiresAt >= DateTime.UtcNow && o.Status == "active");

            if (characterId.HasValue)
            {
                query = query.Where(o => o.CharacterId == characterId.Value);
            }

            return await query
                .OrderByDescending(o => o.Score)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading active opportunities");
            return new List<TradingOpportunity>();
        }
    }
}
