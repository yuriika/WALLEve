using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using WALLEve.Data;
using WALLEve.Models.Esi.Character;
using WALLEve.Services.Esi.Interfaces;
using WALLEve.Services.Market.Interfaces;
using WALLEve.Services.Sde.Interfaces;

namespace WALLEve.Services.Market;

public class InventoryService : IInventoryService
{
    private readonly IEsiApiService _esiApi;
    private readonly ISdeUniverseService _sde;
    private readonly IFeeCalculatorService _feeCalculator;
    private readonly IHubSelectionService _hubSelection;
    private readonly WalletDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly ILogger<InventoryService> _logger;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);
    private const string InventoryCachePrefix = "inv_";
    private const string OverviewCachePrefix = "ov_";

    /// <summary>
    /// Ausführbare Markt-Quotes müssen frisch sein: ein Snapshot, der älter als
    /// diese Spanne ist (z. B. nach einer App-Pause), ist keine Grundlage für eine
    /// Verkaufs-Empfehlung — er bleibt als Referenz sichtbar, aber nicht ausführbar.
    /// </summary>
    private static readonly TimeSpan MaxExecutableQuoteAge = TimeSpan.FromHours(6);

    /// <summary>
    /// Liquidität (#30): eine Markthistory, deren letzter Eintrag älter als diese Spanne
    /// ist, gilt als veraltet und wird nie als liquide interpretiert.
    /// </summary>
    private const int MaxLiquidityHistoryAgeDays = 30;

    public InventoryService(
        IEsiApiService esiApi,
        ISdeUniverseService sde,
        IFeeCalculatorService feeCalculator,
        IHubSelectionService hubSelection,
        WalletDbContext db,
        IMemoryCache cache,
        ILogger<InventoryService> logger)
    {
        _esiApi = esiApi;
        _sde = sde;
        _feeCalculator = feeCalculator;
        _hubSelection = hubSelection;
        _db = db;
        _cache = cache;
        _logger = logger;
    }

    public async Task<List<InventoryItem>> GetInventoryAsync(int characterId)
    {
        var key = InventoryCachePrefix + characterId + await ComparisonCacheSuffixAsync();
        if (_cache.TryGetValue<List<InventoryItem>>(key, out var cached))
            return cached!;

        var items = await LoadInventoryAsync(characterId);
        _cache.Set(key, items, CacheDuration);
        return items;
    }

    /// <summary>
    /// Cache-Suffix des konfigurierten Vergleichsmarkts: Ein Wechsel des
    /// Vergleichsmarkts invalidiert den Inventar-Cache, damit der neue
    /// Vergleichs-Quote sichtbar wird, während die Provenienz des
    /// automatischen Markt-Quotes unverändert bleibt (Issue #63).
    /// </summary>
    private async Task<string> ComparisonCacheSuffixAsync()
    {
        var comparison = await _hubSelection.GetComparisonMarketAsync();
        return "_cmp" + (comparison?.Id ?? 0);
    }

    public async Task<PortfolioOverview> GetPortfolioOverviewAsync(int characterId)
    {
        var key = OverviewCachePrefix + characterId + await ComparisonCacheSuffixAsync();
        if (_cache.TryGetValue<PortfolioOverview>(key, out var cached))
            return cached!;

        var items = await GetInventoryAsync(characterId);
        var overview = new PortfolioOverview
        {
            TotalItemTypes = items.Count, TotalQuantity = items.Sum(i => i.TotalQuantity),
            TotalMarketValue = items.Sum(i => i.CurrentMarketValue),
            TotalCostBasis = items.Any(i => i.TotalCostBasis.HasValue) ? items.Sum(i => i.TotalCostBasis ?? 0) : null,
            ItemCountWithCostBasis = items.Count(i => i.CostBasisPerUnit.HasValue),
            SellRecommendations = items.Count(i => i.Recommendation == "sell"),
            HoldRecommendations = items.Count(i => i.Recommendation == "hold"),
            WatchRecommendations = items.Count(i => i.Recommendation == "watch")
        };
        _cache.Set(key, overview, CacheDuration);
        return overview;
    }

    public async Task<List<InventoryItem>> GetPrioritizedItemsAsync(int characterId, InventorySortMode sortMode = InventorySortMode.Opportunity)
    {
        var items = await GetInventoryAsync(characterId);
        return sortMode switch
        {
            InventorySortMode.Opportunity => items.OrderByDescending(i => i.OpportunityScore ?? 0).ThenByDescending(i => i.CurrentMarketValue).ToList(),
            InventorySortMode.MarketValue => items.OrderByDescending(i => i.CurrentMarketValue).ToList(),
            InventorySortMode.Profit => items.OrderByDescending(i => i.NetProfitAfterFees ?? 0).ToList(),
            InventorySortMode.Roi => items.OrderByDescending(i => i.NetRoiAfterFees ?? 0).ToList(),
            InventorySortMode.Quantity => items.OrderByDescending(i => i.TotalQuantity).ToList(),
            InventorySortMode.Name => items.OrderBy(i => i.TypeName).ToList(),
            _ => items.OrderByDescending(i => i.OpportunityScore ?? 0).ToList()
        };
    }

    private async Task<List<InventoryItem>> LoadInventoryAsync(int characterId)
    {
        var assets = await _esiApi.GetCharacterAssetsAsync(characterId);
        if (assets == null || !assets.Any()) return new List<InventoryItem>();

        var skills = await _esiApi.GetCharacterSkillsAsync();
        var marketPrices = await _esiApi.GetMarketPricesAsync();
        var priceLookup = marketPrices?.ToDictionary(p => p.TypeId, p => p) ?? new();
        var sdeAvailable = await _sde.IsDatabaseAvailableAsync();

        // Cost-Basis-Einträge aus der lokalen DB (vom CostBasisCollectorService
        // befüllt: Echt aus Transaktionen, Geschätzt aus Marktdaten, Manuell vom Nutzer).
        // Vorher wurden hier pro Load alle ESI-Wallet-Transaktionen neu geladen.
        var costBasisLookup = await _db.CostBasisEntries
            .Where(e => e.CharacterId == characterId)
            .ToDictionaryAsync(e => e.TypeId);

        var grouped = assets.GroupBy(a => a.TypeId).ToList();
        var typeIds = grouped.Select(g => g.Key).ToList();

        // Ortsnamen UND Regionen (SDE) EINMAL je Ladevorgang auflösen statt pro Ort/Item
        // (kein N+1). Die Region jedes Asset-Ortes entscheidet, welcher Markt-Quote
        // für diesen Ort ausführbar ist — niemals ein fremder Regionspreis.
        Dictionary<long, string?> locationNames = new();
        Dictionary<long, int> locationRegions = new();
        if (sdeAvailable)
        {
            var venueIds = grouped.SelectMany(g => g.Select(a => a.LocationId)).Distinct().ToList();
            foreach (var locId in venueIds)
            {
                locationNames[locId] = await _sde.GetLocationNameAsync(locId);
                var region = await _sde.GetRegionIdForLocationAsync(locId);
                if (region.HasValue) locationRegions[locId] = region.Value;
            }
        }

        // Neuester Snapshot je (Type, Region): Quotes sind regionsgebunden, ein
        // regionsübergreifendes „Neuester je Typ" würde fremde Regionspreise anwenden.
        var latestSnapshots = await _db.MarketSnapshots
            .Where(s => typeIds.Contains(s.TypeId))
            .GroupBy(s => new { s.TypeId, s.RegionId })
            .Select(g => g.OrderByDescending(s => s.Timestamp).First())
            .ToDictionaryAsync(s => (s.TypeId, s.RegionId), s => s);

        // Historische Tagesvolumina (#30): EINMAL für alle TypeIds laden und je
        // (TypeId, Region) gruppieren (kein N+1). Die lokale Markthistory ist die
        // zweite Liquiditätsgrundlage neben der Orderbuchtiefe; fehlende oder
        // veraltete History wird nie als liquide interpretiert.
        var historyByKey = (await _db.MarketHistory
                .Where(h => typeIds.Contains(h.TypeId))
                .ToListAsync())
            .GroupBy(h => (h.TypeId, h.RegionId))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(h => h.Date).ToList());

        // Vergleichsmarkt (Issue #63): Bewertungs-Quote aus der Region GENAU
        // dieses Markts. Ein fremder Regionspreis wird nie wiederverwendet;
        // ohne Snapshot bleibt nur der ESI-Referenzpreis sichtbar
        // (Reference-only, niemals ein ausführbarer Buy-/Sell-Kurs).
        var comparisonMarket = await _hubSelection.GetComparisonMarketAsync();
        var comparisonSnapshots = new Dictionary<int, Models.Database.MarketSnapshot>();
        if (comparisonMarket != null && typeIds.Count > 0)
        {
            comparisonSnapshots = await _db.MarketSnapshots
                .Where(s => s.RegionId == comparisonMarket.RegionId && typeIds.Contains(s.TypeId))
                .GroupBy(s => s.TypeId)
                .Select(g => g.OrderByDescending(s => s.Timestamp).First())
                .ToDictionaryAsync(s => s.TypeId);
        }

        var items = new List<InventoryItem>();

        foreach (var group in grouped)
        {
            var typeId = group.Key;
            var totalQty = group.Sum(a => a.Quantity);

            // Explizites Owner/Type/Location-Aggregat: je Ort (LocationId + Typ + Flag)
            // eine Zeile. Container (location_type "item") bleiben als unaufgelöste
            // LocationId erhalten — keine erfundene Zuordnung, kein fiktiver Primary.
            var locations = group
                .GroupBy(a => new { a.LocationId, a.LocationType, a.LocationFlag })
                .Select(lg => new InventoryLocationAggregate
                {
                    LocationId = lg.Key.LocationId,
                    LocationType = lg.Key.LocationType,
                    LocationFlag = lg.Key.LocationFlag,
                    Quantity = lg.Sum(a => a.Quantity),
                    RawAssets = lg.ToList(),
                    LocationName = locationNames.TryGetValue(lg.Key.LocationId, out var name) ? name : null
                })
                .OrderByDescending(l => l.Quantity)
                .ToList();

            // Ortsgebundene Verkaufskontexte: ein Eintrag je Ort, mit Menge und
            // Netto-Erlös genau dieses Ortes (nie mehrere Orte zu einem Stapel).
            double? bestBuy = null, bestSell = null, avgPrice = null;
            string typeName = $"Type {typeId}";
            if (sdeAvailable)
            {
                var name = await _sde.GetTypeNameAsync(typeId);
                if (name != null) typeName = name;
            }

            string? buySource = null, sellSource = null;

            // ESI-MarketPrices (Adjusted/Average) sind NUR eine Bewertung: Sie werden
            // als AveragePrice geführt, aber NIE als ausführbarer Kauf- oder Verkaufspreis
            // verwendet (kein adjusted*0.95-Schätzkaufpreis, kein Referenz-Verkaufspreis).
            if (priceLookup.TryGetValue(typeId, out var mp))
                avgPrice = mp.AveragePrice ?? mp.AdjustedPrice;

            // Ausführbarer Quote je Asset-Ort: nur ein FRISCHER Snapshot der Region
            // DIESES Ortes zählt. Andere Regionen (fremder Regionspreis), veraltete
            // oder einseitige (partial) Quotes sind keine Empfehlungsgrundlage.
            var now = DateTime.UtcNow;
            var quotesByLocation = new Dictionary<long, Models.Database.MarketSnapshot>();
            foreach (var loc in locations)
            {
                if (!locationRegions.TryGetValue(loc.LocationId, out var region)) continue;
                if (!latestSnapshots.TryGetValue((typeId, region), out var snap)) continue;
                if (now - snap.Timestamp > MaxExecutableQuoteAge) continue; // stale
                quotesByLocation[loc.LocationId] = snap;
            }

            // Ortsgebundene Verkaufskontexte erst NACH der Quote-Auflösung bauen;
            // jeder Kontext trägt den ausführbaren Sell-Preis genau seines Ortes.
            var sellContexts = BuildSellContexts(locations, quotesByLocation, skills);
            var sellContextNote = BuildSellContextNote(locations);

            // Item-Preis: Quote des größten verkaufbaren Kontexts — die Analyse bindet
            // die Opportunity an genau diesen Ort, daher darf hier nie ein fremder
            // Regionspreis einfließen. Ohne Quote bleibt der Preis Unknown (null).
            var primaryContext = sellContexts
                .Where(c => !c.IsBlocked)
                .OrderByDescending(c => c.Quantity)
                .FirstOrDefault();
            if (primaryContext != null && quotesByLocation.TryGetValue(primaryContext.LocationId, out var primaryQuote))
            {
                bestBuy = primaryQuote.BestBuyPrice;
                bestSell = primaryQuote.BestSellPrice;
            }
            buySource = bestBuy.HasValue ? "snapshot" : null;
            sellSource = bestSell.HasValue ? "snapshot" : null;

            // 3. Keine ausführbaren Preise
            if (!bestSell.HasValue && !bestBuy.HasValue)
            {
                var hasStaleQuote = locations.Any(l =>
                    locationRegions.TryGetValue(l.LocationId, out var r)
                    && latestSnapshots.TryGetValue((typeId, r), out var stale)
                    && now - stale.Timestamp > MaxExecutableQuoteAge);
                var unknownReason = hasStaleQuote
                    ? $"Markt-Quote veraltet (älter als {MaxExecutableQuoteAge.TotalHours:0} Stunden) — Preis unbekannt, keine ausführbare Empfehlung."
                    : "Kein Markt-Quote in der Region des Assets — Preis unbekannt (Referenzpreise sind keine ausführbaren Quotes).";

                items.Add(new InventoryItem
                {
                    OwnerCharacterId = characterId, TypeId = typeId, TypeName = typeName, TotalQuantity = totalQty,
                    Locations = locations, SellContexts = sellContexts, SellContextNote = sellContextNote,
                    BestBuyPrice = null, BestSellPrice = null, AveragePrice = avgPrice,
                    ComparisonQuote = BuildComparisonQuote(typeId, comparisonMarket, comparisonSnapshots, priceLookup, now),
                    OpportunityScore = 0, Recommendation = "watch",
                    RecommendationReason = unknownReason,
                    RawAssets = group.ToList()
                });
                continue;
            }

            // Einseitiger (partial) Quote: nur eine Seite vorhanden — der Spread ist
            // nicht bestimmbar und eine ausführbare Empfehlung entfällt.
            double? spread = null;
            if (bestBuy.HasValue && bestSell.HasValue && bestBuy.Value > 0)
                spread = ((bestSell.Value - bestBuy.Value) / bestBuy.Value) * 100;

            var costBasisEntry = costBasisLookup.TryGetValue(typeId, out var cbEntry) ? cbEntry : null;
            var costBasis = costBasisEntry?.Value;
            var costBasisSourceLabel = costBasisEntry?.Source switch
            {
                Models.Database.CostBasisSource.Transaction => "Echt",
                Models.Database.CostBasisSource.Estimate => "Geschätzt",
                Models.Database.CostBasisSource.Manual => "Manuell",
                _ => null
            };

            double? estimatedNetProceeds = null, netProfit = null, netRoi = null;
            if (bestSell.HasValue)
            {
                var sellResult = _feeCalculator.CalculateSellProceeds(bestSell.Value, totalQty, skills);
                estimatedNetProceeds = sellResult.NetAmount;
                if (costBasis.HasValue)
                {
                    netProfit = estimatedNetProceeds - (costBasis.Value * totalQty);
                    var totalCost = costBasis.Value * totalQty;
                    netRoi = totalCost > 0 ? (netProfit.Value / totalCost) * 100 : 0;
                }
            }

            // Liquiditätsindikator aus Marktdaten (#30): kumulierte Regions-Tiefe aus
            // dem Snapshot + Ø Tagesvolumen aus der lokalen Markthistory. Die eigene
            // Besitzmenge fließt NICHT ein — nur Markttiefe und History.
            LiquidityIndicator? liquidity = null;
            if (primaryContext != null
                && locationRegions.TryGetValue(primaryContext.LocationId, out var liqRegion)
                && latestSnapshots.TryGetValue((typeId, liqRegion), out var liqQuote))
            {
                liquidity = BuildLiquidityIndicator(historyByKey, liqQuote, typeId, liqRegion, now);
            }

            var (score, rec, reason) = CalculateOpportunityScore(spread, netRoi, avgPrice, bestSell, bestBuy, liquidity);

            items.Add(new InventoryItem
            {
                OwnerCharacterId = characterId, TypeId = typeId, TypeName = typeName, TotalQuantity = totalQty,
                Locations = locations, SellContexts = sellContexts, SellContextNote = sellContextNote,
                BestBuyPrice = bestBuy, BestSellPrice = bestSell, AveragePrice = avgPrice, SpreadPercent = spread,
                CostBasisPerUnit = costBasis, CostBasisSourceLabel = costBasisSourceLabel,
                EstimatedNetProceeds = estimatedNetProceeds, NetProfitAfterFees = netProfit, NetRoiAfterFees = netRoi,
                OpportunityScore = score, Recommendation = rec, RecommendationReason = reason,
                BuyPriceSource = buySource, SellPriceSource = sellSource,
                ComparisonQuote = BuildComparisonQuote(typeId, comparisonMarket, comparisonSnapshots, priceLookup, now),
                RawAssets = group.ToList()
            });
        }

        _logger.LogInformation("Inventory: {Count} item types, {TotalQty} total units, {WithPrice} with prices",
            items.Count, items.Sum(i => i.TotalQuantity), items.Count(i => i.BestSellPrice.HasValue));
        return items;
    }

    /// <summary>
    /// Baut den kontextgebundenen Vergleichs-Quote (Issue #63) für einen Type.
    /// Der Quote stammt AUSSCHLIESSLICH aus dem lokalen Snapshot der Region des
    /// Vergleichsmarkts; fehlt er, bleibt nur der ESI-Referenzpreis (Reference-only).
    /// Ein frischer, zweiseitiger Snapshot ergibt einen vollständigen Vergleich;
    /// veraltete oder einseitige Snapshots werden benannt, nie als ausführbar
    /// verkauft. Der Rückgabewert beeinflusst den ausführbaren Item-Quote nicht.
    /// </summary>
    private static ComparisonQuote? BuildComparisonQuote(
        int typeId,
        Models.Database.MarketHubProfile? comparisonMarket,
        IReadOnlyDictionary<int, Models.Database.MarketSnapshot> comparisonSnapshots,
        IReadOnlyDictionary<int, Models.Esi.Markets.MarketPrice> priceLookup,
        DateTime now)
    {
        if (comparisonMarket == null) return null;

        var quote = new ComparisonQuote
        {
            MarketName = comparisonMarket.Name,
            RegionId = comparisonMarket.RegionId,
            SystemId = comparisonMarket.SystemId
        };

        if (comparisonSnapshots.TryGetValue(typeId, out var snap))
        {
            quote.Source = "market-snapshot";
            quote.BestBuyPrice = snap.BestBuyPrice;
            quote.BestSellPrice = snap.BestSellPrice;
            quote.BuyVolume = snap.BuyVolume;
            quote.SellVolume = snap.SellVolume;
            quote.SnapshotTimestamp = snap.Timestamp;
            quote.IsStale = now - snap.Timestamp > MaxExecutableQuoteAge;

            if (quote.IsStale)
            {
                quote.Note = $"Snapshot veraltet (älter als {MaxExecutableQuoteAge.TotalHours:0} Stunden) — nur noch Referenz, kein ausführbarer Kurs.";
            }
            else if (!snap.BestBuyPrice.HasValue && !snap.BestSellPrice.HasValue)
            {
                quote.Note = "Kein Order-Buch in der Region dieses Markts — Preis unbekannt.";
            }
            else if (!snap.BestBuyPrice.HasValue || !snap.BestSellPrice.HasValue)
            {
                quote.Note = "Einseitiger Quote (partial) — nur eine Order-Seite vorhanden.";
            }
        }
        else if (priceLookup.TryGetValue(typeId, out var mp) && (mp.AveragePrice ?? mp.AdjustedPrice).HasValue)
        {
            // Kein lokaler Snapshot am Vergleichsmarkt: nur der globale
            // ESI-Referenzpreis ist sichtbar — Reference-only, keine Order-Seite.
            quote.Source = "esi-reference";
            quote.Note = "Kein Order-Buch-Snapshot an diesem Markt — nur ESI-Referenzpreis (nicht ausführbar).";
        }
        else
        {
            quote.Source = "unknown";
            quote.Note = "Kein Snapshot und kein Referenzpreis — Preis unbekannt.";
        }

        if (priceLookup.TryGetValue(typeId, out var mpRef))
        {
            quote.AveragePrice = mpRef.AveragePrice ?? mpRef.AdjustedPrice;
        }

        return quote;
    }

    /// <summary>
    /// Baut die ortsgebundenen Verkaufskontexte: ein Eintrag je tatsächlichem Ort mit
    /// Menge und Netto-Erlös genau dieses Ortes. Orte ohne aufgelösten Handelsplatz
    /// (Container, System, unbekannt) sind blockiert — ihre Menge bleibt sichtbar,
    /// wird aber nie als verkaufbarer Stapel behandelt. Ein Kontext erhält einen
    /// Sell-Preis nur aus dem FRISCHEN Quote der Region genau dieses Ortes.
    /// </summary>
    private List<InventorySellContext> BuildSellContexts(
        IEnumerable<InventoryLocationAggregate> locations,
        IReadOnlyDictionary<long, Models.Database.MarketSnapshot> quotesByLocation,
        CharacterSkills? skills)
    {
        var contexts = new List<InventorySellContext>();
        foreach (var loc in locations)
        {
            var ctx = new InventorySellContext
            {
                LocationId = loc.LocationId,
                LocationType = loc.LocationType,
                LocationLabel = loc.LocationLabel,
                Quantity = loc.Quantity
            };
            if (!loc.IsMarketVenue)
            {
                ctx.IsBlocked = true;
                ctx.BlockReason = loc.SellContextBlockReason;
            }
            else if (quotesByLocation.TryGetValue(loc.LocationId, out var quote) && quote.BestSellPrice.HasValue)
            {
                ctx.SellPrice = quote.BestSellPrice.Value;
                ctx.MarketSnapshotId = quote.Id;
                ctx.EstimatedNetProceeds = _feeCalculator.CalculateSellProceeds(quote.BestSellPrice.Value, loc.Quantity, skills).NetAmount;
            }
            contexts.Add(ctx);
        }
        return contexts;
    }

    /// <summary>
    /// UI-Hinweis, warum keine aggregierte Verkaufsaktion angeboten wird: Menge an
    /// mehreren Orten oder mindestens ein Ort ohne aufgelösten Handelsplatz.
    /// Leer, wenn genau ein aufgelöster Handelsplatz existiert (aggregiert zulässig).
    /// </summary>
    private static string BuildSellContextNote(IReadOnlyList<InventoryLocationAggregate> locations)
    {
        if (locations.Count <= 1 && locations.All(l => l.IsMarketVenue))
            return string.Empty;
        var unresolved = locations.Count(l => !l.IsMarketVenue);
        if (locations.Count > 1)
            return unresolved > 0
                ? $"Menge liegt an {locations.Count} Orten ({unresolved} ohne aufgelösten Handelsplatz) — kein gemeinsamer verkaufbarer Stapel; je Ort getrennt simulieren."
                : $"Menge liegt an {locations.Count} Orten — kein gemeinsamer verkaufbarer Stapel; je Ort getrennt simulieren.";
        return locations[0].SellContextBlockReason ?? string.Empty;
    }

    /// <summary>
    /// Berechnet den Opportunity Score aus Spread, ROI, Preisstabilität und Liquidität.
    /// Ohne Cost Basis (netRoi=null) wird der Score aus Spread + Liquidität gebildet.
    /// Die Liquidität stammt AUSSCHLIESSLICH aus Marktdaten (kumulierte Orderbuchtiefe +
    /// History) — die eigene Besitzmenge beeinflusst den Score nicht mehr (#30).
    /// </summary>
    private static (double Score, string Rec, string Reason) CalculateOpportunityScore(
        double? spread, double? netRoi, double? avgPrice, double? bestSell, double? bestBuy,
        LiquidityIndicator? liquidity)
    {
        if (!bestSell.HasValue || !bestBuy.HasValue)
            return (0, "watch", "Keine aktuellen Marktdaten.");

        double score = 0;
        if (spread.HasValue) score += spread.Value > 10 ? 30 : spread.Value > 5 ? 20 : spread.Value > 2 ? 10 : 5;
        if (netRoi.HasValue) score += netRoi.Value > 20 ? 40 : netRoi.Value > 10 ? 30 : netRoi.Value > 5 ? 20 : netRoi.Value > 0 ? 10 : netRoi.Value < -10 ? -20 : 0;
        if (spread.HasValue) score += spread.Value < 2 ? 20 : spread.Value < 5 ? 10 : 5;
        if (avgPrice.HasValue && bestSell.HasValue)
        {
            var dev = Math.Abs(bestSell.Value - avgPrice.Value) / avgPrice.Value * 100;
            score += dev < 5 ? 10 : dev < 15 ? 5 : 0;
        }
        // Liquidität aus Marktdaten (kumulierte Orderbuchtiefe + History). Die eigene
        // Besitzmenge ist KEIN Liquiditätssignal mehr (vorher: Mengen-Bonus).
        score += LiquidityScore(liquidity);

        score = Math.Clamp(score, 0, 100);

        // Ohne belastbare Marktliquidität wird keine „ausreichende Liquidität"
        // behauptet — der Zustand bleibt explizit unbekannt (#30). Fehlt nur die
        // History (Tiefe vorhanden, aber veraltet), wird auch das benannt.
        bool hasCostBasis = netRoi.HasValue;
        var liquidityUnknown = liquidity is null || liquidity.IsUnknown;
        var liquidityClaim = liquidityUnknown ? string.Empty : " bei ausreichender Liquidität";
        var liquidityNote = liquidityUnknown
            ? " Liquidität unbekannt — keine belastbaren Marktdaten (Orderbuchtiefe/History)."
            : liquidity!.HasHistory
                ? string.Empty
                : " Nur Orderbuchtiefe vorhanden — History fehlt oder ist veraltet, Liquidität gering.";

        if (hasCostBasis && score >= 60 && netRoi > 0)
            return (score, "sell", $"Gute Marge ({netRoi:F1}% ROI){liquidityClaim}. Verkauf empfohlen.{liquidityNote}");
        if (!hasCostBasis && score >= 60 && spread.HasValue && spread > 5)
            return (score, "sell", $"Guter Spread ({spread:F1}%){liquidityClaim}. Cost Basis unbekannt — prüfe selbst ob der Einkaufspreis passt.{liquidityNote}");
        if (hasCostBasis && score >= 40 && netRoi > 0)
            return (score, "watch", $"Mäßige Marge ({netRoi:F1}% ROI). Beobachten oder auf besseren Preis warten.{liquidityNote}");
        if (!hasCostBasis && score >= 40)
            return (score, "watch", $"Spread von {spread:F1}% — beobachten. Cost Basis unbekannt, daher keine ROI-Berechnung möglich.{liquidityNote}");
        if (hasCostBasis && netRoi < 0)
            return (score, "hold", $"Im Minus ({netRoi:F1}% ROI). Halten oder nur bei Kapitalbedarf verkaufen.{liquidityNote}");
        if (score >= 30)
            return (score, "watch", $"Spread: {spread:F1}%. Geringe Marge — beobachten.{liquidityNote}");
        return (score, "hold", $"Keine klare Opportunität. Bestand halten.{liquidityNote}");
    }

    /// <summary>Score-Punkte aus der Marktliquidität (0 bei Unknown oder fehlenden Daten).</summary>
    private static int LiquidityScore(LiquidityIndicator? liquidity)
    {
        if (liquidity is null || liquidity.IsUnknown)
            return 0;
        return liquidity.Tier switch
        {
            LiquidityTier.High => 15,
            LiquidityTier.Medium => 10,
            _ => 5 // Low: nur ein Signal bzw. geringe Tiefe/Tagesmenge
        };
    }

    /// <summary>
    /// Baut den Liquiditätsindikator eines Items aus bereits geladenen Marktdaten (#30):
    /// kumulierte Regions-Tiefe (Snapshot-Sellvolumen über alle Stufen — keine einzelne
    /// Top-Order) und Ø Tagesvolumen der lokalen Markthistory. Fehlende oder veraltete
    /// History (älter als <see cref="MaxLiquidityHistoryAgeDays"/> Tage) ergibt explizit
    /// Unknown/Low, niemals „liquide". Kein ESI-Abruf, keine neue Discovery.
    /// </summary>
    private static LiquidityIndicator BuildLiquidityIndicator(
        IReadOnlyDictionary<(int TypeId, int RegionId), List<Models.Database.MarketHistory>> historyByKey,
        Models.Database.MarketSnapshot quote,
        int typeId,
        int regionId,
        DateTime now)
    {
        const int maxHistoryDays = 30;

        var hasDepth = quote.SellVolume > 0;
        var appraisableQuantity = hasDepth ? quote.SellVolume : 0;

        var hasHistory = false;
        long averageDailyVolume = 0;
        var historyDays = 0;
        if (historyByKey.TryGetValue((typeId, regionId), out var history) && history.Count > 0)
        {
            var latest = history[0];
            if ((now.Date - latest.Date.Date).TotalDays <= MaxLiquidityHistoryAgeDays)
            {
                var recent = history.Take(maxHistoryDays).ToList();
                hasHistory = true;
                averageDailyVolume = (long)recent.Average(h => (double)h.Volume);
                historyDays = recent.Count;
            }
        }

        var tier = LiquidityIndicator.DeriveTier(hasDepth, appraisableQuantity, hasHistory, averageDailyVolume);
        // Der Snapshot liefert nur die Summe über alle Stufen, keine Preisstufen: ohne
        // nachweislich mehrstufige Tiefe bewusst höchstens Medium (#30).
        if (tier == LiquidityTier.High)
            tier = LiquidityTier.Medium;

        return new LiquidityIndicator
        {
            HasDepth = hasDepth,
            AppraisableQuantity = appraisableQuantity,
            DepthLevelsUsed = hasDepth ? 1 : 0,
            HasHistory = hasHistory,
            AverageDailyVolume = averageDailyVolume,
            HistoryDays = historyDays,
            Tier = tier
        };
    }
}