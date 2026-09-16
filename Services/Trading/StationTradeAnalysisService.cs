using Microsoft.EntityFrameworkCore;
using WALLEve.Data;
using WALLEve.Models.Database;
using WALLEve.Models.Esi.Markets;
using WALLEve.Models.Market;
using WALLEve.Models.Trading;
using WALLEve.Services.Authentication.Interfaces;
using WALLEve.Services.Esi.Interfaces;
using WALLEve.Services.Market.Interfaces;
using WALLEve.Services.Trading.Interfaces;

namespace WALLEve.Services.Trading;

/// <summary>
/// Station-Trade-Analyse (Issue #71): nutzt die vorhandene Pipeline und die
/// Regions-Cache-Grundlage (#69) — kein zweiter Scanner, keine zweite
/// Ranking-Engine. Pro (Region, Item) wird der Kandidat aus den kumulierten
/// Order-Tiefen beider Seiten, der 30-Tage-History und den echten Char-Gebühren
/// (<see cref="StationTradeCandidateEngine"/>) bestimmt; ausführbare Kandidaten
/// werden als station_trading-Opportunity + unveränderlicher Vertrag (#37)
/// persistiert. Analysen ohne belastbare Chance (manipulierte Top-Order,
/// illiquider Spread, fehlende History, negative Nettomarge) erzeugen nichts
/// und invalidieren eine zuvor aktive Opportunity zu diesem (Region, Item).
/// </summary>
public sealed class StationTradeAnalysisService : IStationTradeAnalysisService
{
    /// <summary>Gehandelte Haupthubs — identische Grundlage wie der MarketDataCollector.</summary>
    private static readonly int[] TrackedRegions =
    {
        10000002,  // The Forge (Jita)
        10000043,  // Domain (Amarr)
        10000032,  // Sinq Laison (Dodixie)
        10000030,  // Heimatar (Rens)
        10000042   // Metropolis (Hek)
    };

    private readonly WalletDbContext _db;
    private readonly IFeeCalculatorService _feeCalculator;
    private readonly IEveAuthenticationService _authService;
    private readonly IEsiApiService _esiApi;
    private readonly IRegionalMarketCacheService _regionCache;
    private readonly ITradeStatusService _tradeStatusService;
    private readonly ILogger<StationTradeAnalysisService> _logger;

    public StationTradeAnalysisService(
        WalletDbContext db,
        IFeeCalculatorService feeCalculator,
        IEveAuthenticationService authService,
        IEsiApiService esiApi,
        IRegionalMarketCacheService regionCache,
        ITradeStatusService tradeStatusService,
        ILogger<StationTradeAnalysisService> logger)
    {
        _db = db;
        _feeCalculator = feeCalculator;
        _authService = authService;
        _esiApi = esiApi;
        _regionCache = regionCache;
        _tradeStatusService = tradeStatusService;
        _logger = logger;
    }

    public async Task<List<TradingOpportunity>> AnalyzeStationTradesAsync(CancellationToken ct = default)
    {
        try
        {
            var authState = await _authService.GetAuthStateAsync();
            if (authState?.IsValid != true)
            {
                _logger.LogWarning("No authenticated character — skipping station trade analysis");
                return new List<TradingOpportunity>();
            }

            var skills = await _esiApi.GetCharacterSkillsAsync();
            var feeProfile = _feeCalculator.BuildFeeProfile(skills);
            if (feeProfile.HasUnknownInput)
            {
                _logger.LogWarning("Unknown fee inputs (invalid override) — station trade analysis blocked");
                return new List<TradingOpportunity>();
            }

            // Items: Standard-Handelsgüter + Favoriten des Owners (Owner-Isolation, #69-Stil).
            var favoriteTypeIds = await _db.MarketFavorits
                .Where(f => f.CharacterId == authState.CharacterId)
                .Select(f => f.TypeId)
                .Distinct()
                .ToListAsync(ct);
            var typeIds = favoriteTypeIds
                .Concat(DefaultTrackedTypeIds)
                .Distinct()
                .OrderBy(t => t)
                .ToList();

            // Bestehende aktive station_trading-Opportunities EINMAL laden
            // (kein Query pro Item) — Schlüssel (TypeId, RegionId).
            var existingByKey = await _db.TradingOpportunities
                .Where(o => o.OpportunityType == "station_trading"
                         && o.CharacterId == authState.CharacterId
                         && RecommendationStatus.PlannedStorageValues.Contains(o.Status))
                .ToListAsync(ct);
            var existingMap = existingByKey
                .GroupBy(o => (o.TypeId, o.BuyRegionId ?? o.SellRegionId ?? 0))
                .ToDictionary(g => g.Key, g => g.First());

            var producedKeys = new HashSet<(int TypeId, int RegionId)>();
            var opportunities = new List<TradingOpportunity>();
            var contractSources = new Dictionary<TradingOpportunity,
                (StationTradeCandidateResult Result, DateTime ScannedAtUtc, int RegionId)>();

            foreach (var regionId in TrackedRegions)
            {
                var orders = await _regionCache.GetRegionOrdersAsync(regionId, ct);
                if (orders.Count == 0)
                {
                    continue;
                }

                var historyByType = (await _db.MarketHistory
                        .Where(h => h.RegionId == regionId && typeIds.Contains(h.TypeId))
                        .ToListAsync(ct))
                    .GroupBy(h => h.TypeId)
                    .ToDictionary(g => g.Key, g => g.ToList());

                foreach (var typeId in typeIds)
                {
                    var asks = orders.Where(o => o.TypeId == typeId && !o.IsBuyOrder).ToList();
                    var bids = orders.Where(o => o.TypeId == typeId && o.IsBuyOrder).ToList();
                    if (asks.Count == 0 || bids.Count == 0)
                    {
                        continue;
                    }

                    historyByType.TryGetValue(typeId, out var history);
                    var result = StationTradeCandidateEngine.Evaluate(
                        regionId, typeId, asks, bids, history ?? new List<MarketHistory>(),
                        new StationTradeFees(feeProfile.BrokerFeeRate, feeProfile.SalesTaxRate),
                        DateTime.UtcNow);

                    if (!result.IsActionable)
                    {
                        _logger.LogDebug(
                            "Station trade {RegionId}/{TypeId} rejected: {Reason}",
                            regionId, typeId, result.NotActionableReason);
                        continue;
                    }

                    // Nur belastbare Kandidaten zählen als „produziert" — ein heute
                    // abgelehnter (Region, Item) invalidiert seine zuvor aktive
                    // Opportunity (Historie bleibt erhalten).
                    producedKeys.Add((typeId, regionId));

                    var key = (typeId, regionId);
                    existingMap.TryGetValue(key, out var existing);

                    var score = Math.Clamp(55 + (result.RoiPercent.GetValueOrDefault() * 1.5), 55, 95);
                    var evidence = result.Evidence +
                        $" Gebühren: Broker {feeProfile.BrokerFeeRate:P1} ({feeProfile.BrokerRateOrigin.Label()}), Steuer {feeProfile.SalesTaxRate:P1} ({feeProfile.SalesTaxOrigin.Label()}), Standings-Anteil {feeProfile.StandingsOrigin.Label()}.";

                    var scannedAt = DateTime.UtcNow;

                    if (existing == null)
                    {
                        var opportunity = new TradingOpportunity
                        {
                            CharacterId = authState.CharacterId,
                            TypeId = typeId,
                            OpportunityType = "station_trading",
                            BuyRegionId = regionId,
                            SellRegionId = regionId,
                            BuySystemId = result.BuySystemId,
                            SellSystemId = result.SellSystemId,
                            BuyLocationId = result.BuyLocationId,
                            SellLocationId = result.SellLocationId,
                            BuyPrice = result.BuyPricePerUnit,
                            SellPrice = result.SellPricePerUnit,
                            EstimatedProfit = result.NetProfit.GetValueOrDefault(),
                            RequiredCapital = result.RequiredCapital.GetValueOrDefault(),
                            Score = score,
                            Provenance = TradingOpportunity.ProvenanceHeuristic,
                            AlgorithmVersion = StationTradeCandidateEngine.AlgorithmVersion,
                            DataQuality = null,
                            Evidence = evidence,
                            BrokerFeeRate = feeProfile.BrokerFeeRate * 100.0,
                            SalesTaxRate = feeProfile.SalesTaxRate * 100.0,
                            BrokerFeeOrigin = feeProfile.BrokerRateOrigin.StorageValue(),
                            SalesTaxOrigin = feeProfile.SalesTaxOrigin.StorageValue(),
                            StandingsOrigin = feeProfile.StandingsOrigin.StorageValue(),
                            FeeEvaluatedAtUtc = feeProfile.EvaluatedAtUtc,
                            DetectedAt = scannedAt,
                            ExpiresAt = scannedAt.AddHours(2),
                            Status = RecommendationStatus.Planned
                        };
                        _db.TradingOpportunities.Add(opportunity);
                        opportunities.Add(opportunity);
                        existingMap[key] = opportunity;
                        contractSources[opportunity] = (result, scannedAt, regionId); // Snapshot nach SaveChanges
                    }
                    else
                    {
                        existing.BuyLocationId = result.BuyLocationId;
                        existing.SellLocationId = result.SellLocationId;
                        existing.BuySystemId = result.BuySystemId;
                        existing.SellSystemId = result.SellSystemId;
                        existing.BuyPrice = result.BuyPricePerUnit;
                        existing.SellPrice = result.SellPricePerUnit;
                        existing.EstimatedProfit = result.NetProfit.GetValueOrDefault();
                        existing.RequiredCapital = result.RequiredCapital.GetValueOrDefault();
                        existing.Score = score;
                        existing.Provenance = TradingOpportunity.ProvenanceHeuristic;
                        existing.AlgorithmVersion = StationTradeCandidateEngine.AlgorithmVersion;
                        existing.Evidence = evidence;
                        existing.BrokerFeeRate = feeProfile.BrokerFeeRate * 100.0;
                        existing.SalesTaxRate = feeProfile.SalesTaxRate * 100.0;
                        existing.BrokerFeeOrigin = feeProfile.BrokerRateOrigin.StorageValue();
                        existing.SalesTaxOrigin = feeProfile.SalesTaxOrigin.StorageValue();
                        existing.StandingsOrigin = feeProfile.StandingsOrigin.StorageValue();
                        existing.FeeEvaluatedAtUtc = feeProfile.EvaluatedAtUtc;
                        existing.ExpiresAt = scannedAt.AddHours(2);
                        existing.DetectedAt = scannedAt;
                    }
                }
            }

            await _db.SaveChangesAsync(ct);

            // Issue #37: unveränderlicher StationTrade-Vertrag je NEU erzeugter
            // Opportunity. Quote-Quelle ist der soeben erzeugte Regionen-Snapshot
            // (Best-Ask/-Bid beider Stationen) — vollständig reproduzierbar.
            foreach (var (opportunity, source) in contractSources)
            {
                var (result, scannedAt, regionId) = source;
                var snapshot = new MarketSnapshot
                {
                    RegionId = regionId,
                    TypeId = opportunity.TypeId,
                    Timestamp = scannedAt,
                    BestBuySystemId = result.SellSystemId,
                    BestSellSystemId = result.BuySystemId,
                    BestBuyLocationId = result.SellLocationId,
                    BestSellLocationId = result.BuyLocationId,
                    BestBuyPrice = result.SellPricePerUnit,
                    BestSellPrice = result.BuyPricePerUnit,
                    BuyVolume = (long)(result.CumulativeBidDepth ?? 0),
                    SellVolume = (long)(result.CumulativeAskDepth ?? 0),
                    Spread = (result.BuyPricePerUnit ?? 0) - (result.SellPricePerUnit ?? 0)
                };
                _db.MarketSnapshots.Add(snapshot);
                await _db.SaveChangesAsync(ct); // Id für den Vertrag

                _db.TradeContracts.Add(TradeContractFactory.CreateStationTrade(
                    tradingOpportunityId: opportunity.Id,
                    characterId: opportunity.CharacterId,
                    typeId: opportunity.TypeId,
                    algorithmVersion: StationTradeCandidateEngine.AlgorithmVersion,
                    createdAt: opportunity.DetectedAt,
                    quantity: result.Quantity!.Value,
                    buyLocationId: result.BuyLocationId!.Value,
                    sellLocationId: result.SellLocationId!.Value,
                    buyRegionId: regionId,
                    sellRegionId: regionId,
                    buyPricePerUnit: (decimal)(result.BuyPricePerUnit ?? 0),
                    sellPricePerUnit: (decimal)(result.SellPricePerUnit ?? 0),
                    buyMarketSnapshotId: snapshot.Id,
                    sellMarketSnapshotId: snapshot.Id,
                    brokerRatePercent: (decimal)(feeProfile.BrokerFeeRate * 100.0),
                    salesTaxPercent: (decimal)(feeProfile.SalesTaxRate * 100.0),
                    jumpDistance: null,
                    estimatedNetProceeds: (decimal)(result.NetProfit.GetValueOrDefault()
                        + (result.BuyPricePerUnit ?? 0) * result.Quantity!.Value * (1.0 + feeProfile.BrokerFeeRate)),
                    estimatedProfit: (decimal)opportunity.EstimatedProfit,
                    requiredCapital: (decimal)opportunity.RequiredCapital,
                    netRoiPercent: (decimal)result.RoiPercent.GetValueOrDefault(),
                    evidence: opportunity.Evidence));
            }
            if (contractSources.Count > 0)
            {
                await _db.SaveChangesAsync(ct);
            }

            // Stale: bisher aktive station_trading-Opportunities, deren (Region, Item)
            // heute keinen belastbaren Kandidaten mehr liefert → invalidieren (Historie bleibt).
            var staleCount = 0;
            foreach (var ((typeId, regionId), stale) in existingMap)
            {
                if (producedKeys.Contains((typeId, regionId)))
                {
                    continue;
                }

                if (await _tradeStatusService.InvalidateStagedAsync(stale, "station trade no longer actionable", DateTime.UtcNow))
                {
                    staleCount++;
                }
            }

            _logger.LogInformation(
                "Station trade analysis done: {New} new, {Stale} invalidated ({Regions} regions, {Types} types)",
                opportunities.Count, staleCount, TrackedRegions.Length, typeIds.Count);

            return opportunities
                .Concat(existingMap.Values.Where(o => o.ExpiresAt >= DateTime.UtcNow
                    && RecommendationStatus.PlannedStorageValues.Contains(o.Status)))
                .OrderByDescending(o => o.Score)
                .ToList();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing station trades");
            return new List<TradingOpportunity>();
        }
    }

    /// <summary>Standard-Handelsgüter (identisch zur Grundmenge des Collectors).</summary>
    private static readonly int[] DefaultTrackedTypeIds =
    {
        44992,  // PLEX
        40520,  // Large Skill Injector
        40519,  // Small Skill Injector
        34,     // Tritanium
        35,     // Pyerite
        36,     // Mexallon
        37,     // Isogen
        38,     // Nocxium
        39,     // Zydrine
        40,     // Megacyte
    };
}