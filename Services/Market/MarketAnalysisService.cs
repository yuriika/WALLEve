using Microsoft.EntityFrameworkCore;
using WALLEve.Data;
using WALLEve.Models.Database;
using WALLEve.Services.AI.Interfaces;
using WALLEve.Services.Authentication.Interfaces;
using WALLEve.Services.Esi.Interfaces;
using WALLEve.Services.Market.Interfaces;

namespace WALLEve.Services.Market;

/// <summary>
/// Market Analysis Service mit Heuristik statt LLM (KI-Anbindung folgt später separat).
/// Analysiert die BESTANDS-Items des Charakters: Verkaufssimulation pro Item mit
/// echten Skills (FeeCalculator), Vergleich Einkaufspreis (Cost Basis) vs. Marktpreis.
/// </summary>
public class MarketAnalysisService : IMarketAnalysisService
{
    private readonly IOllamaService _ollama;
    private readonly WalletDbContext _dbContext;
    private readonly IFeeCalculatorService _feeCalculator;
    private readonly IInventoryService _inventoryService;
    private readonly IEveAuthenticationService _authService;
    private readonly IEsiApiService _esiApi;
    private readonly ILogger<MarketAnalysisService> _logger;

    public MarketAnalysisService(
        IOllamaService ollama,
        WalletDbContext dbContext,
        IFeeCalculatorService feeCalculator,
        IInventoryService inventoryService,
        IEveAuthenticationService authService,
        IEsiApiService esiApi,
        ILogger<MarketAnalysisService> logger)
    {
        _ollama = ollama;
        _dbContext = dbContext;
        _feeCalculator = feeCalculator;
        _inventoryService = inventoryService;
        _authService = authService;
        _esiApi = esiApi;
        _logger = logger;
    }

    /// <summary>
    /// Testet die Ollama-Verbindung mit einem EVE-spezifischen Prompt
    /// </summary>
    /// <returns>Formatierte Test-Ergebnisse mit verfügbaren Modellen und Test-Response</returns>
    public async Task<string> TestOllamaConnectionAsync()
    {
        try
        {
            _logger.LogInformation("Testing Ollama connection...");

            // Check if Ollama is available
            var isAvailable = await _ollama.IsAvailableAsync();
            if (!isAvailable)
            {
                _logger.LogWarning("Ollama is not available at configured endpoint");
                return "ERROR: Ollama not available. Make sure Ollama is running on localhost:11434";
            }

            // Get available models
            var models = await _ollama.GetAvailableModelsAsync();
            if (models == null || !models.Any())
            {
                _logger.LogWarning("No Ollama models available");
                return "ERROR: No Ollama models found. Run 'ollama pull llama3.1:8b' to download a model.";
            }

            _logger.LogInformation("Ollama is available with {Count} models: {Models}",
                models.Count, string.Join(", ", models));

            // Test simple prompt
            var testPrompt = "Explain arbitrage trading in EVE Online in exactly one sentence.";
            var response = await _ollama.GenerateAsync(testPrompt);

            _logger.LogInformation("Ollama test successful. Response length: {Length} characters", response.Length);

            return $"✅ Ollama Connection Successful!\n\nAvailable Models: {string.Join(", ", models)}\n\nTest Response:\n{response}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error testing Ollama connection");
            return $"❌ ERROR: {ex.Message}\n\nMake sure Ollama is installed and running:\n" +
                   "1. Install: curl -fsSL https://ollama.com/install.sh | sh\n" +
                   "2. Pull model: ollama pull llama3.1:8b\n" +
                   "3. Verify: ollama list";
        }
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

            // Nur Items mit Cost Basis UND Marktpreis sind analysierbar
            var analyzable = items
                .Where(i => i.CostBasisPerUnit.HasValue && i.BestSellPrice.HasValue)
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
                        Confidence = Math.Clamp(55 + (roi * 1.5), 55, 95),
                        AIModel = "heuristic",
                        Reasoning = reasoning,
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
                    existing.Reasoning = reasoning;
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
                .OrderByDescending(o => o.Confidence)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading active opportunities");
            return new List<TradingOpportunity>();
        }
    }
}
