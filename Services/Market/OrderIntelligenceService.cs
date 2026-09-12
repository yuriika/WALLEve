using WALLEve.Models.Esi.Character;
using WALLEve.Services.Esi.Interfaces;
using WALLEve.Services.Market.Interfaces;
using WALLEve.Services.Sde.Interfaces;

namespace WALLEve.Services.Market;

public class OrderIntelligenceService : IOrderIntelligenceService
{
    private readonly IEsiApiService _esiApi;
    private readonly ISdeUniverseService _sde;
    private readonly IFeeCalculatorService _feeCalculator;
    private readonly ICostBasisService _costBasis;
    private readonly ILogger<OrderIntelligenceService> _logger;
    private static readonly TimeSpan SkillsCacheDuration = TimeSpan.FromMinutes(15);
    private CharacterSkills? _skillsCache;
    private DateTime _skillsCacheTime = DateTime.MinValue;

    private async Task<CharacterSkills?> GetSkillsCachedAsync()
    {
        if (_skillsCache != null && DateTime.UtcNow - _skillsCacheTime < SkillsCacheDuration)
        {
            return _skillsCache;
        }

        _skillsCache = await _esiApi.GetCharacterSkillsAsync();
        _skillsCacheTime = DateTime.UtcNow;
        return _skillsCache;
    }

    public OrderIntelligenceService(
        IEsiApiService esiApi,
        ISdeUniverseService sde,
        IFeeCalculatorService feeCalculator,
        ICostBasisService costBasis,
        ILogger<OrderIntelligenceService> logger)
    {
        _esiApi = esiApi;
        _sde = sde;
        _feeCalculator = feeCalculator;
        _costBasis = costBasis;
        _logger = logger;
    }

    public async Task<OrderBookContext?> GetOrderBookAsync(int characterId, long orderId)
    {
        try
        {
            var myOrders = await _esiApi.GetMarketOrdersAsync(characterId);
            var own = myOrders?.FirstOrDefault(o => o.OrderId == orderId);
            if (own == null)
            {
                _logger.LogWarning("Order {OrderId} not found for character {CharacterId}", orderId, characterId);
                return null;
            }

            // Fremde Orders desselben Items in derselben Region (ESI-cached ~5 Min).
            // null = Abruf fehlgeschlagen → KEIN leeres Orderbuch (keine Positions-Empfehlung).
            var foreign = await _esiApi.GetAllRegionalMarketOrdersAsync(own.RegionId, own.TypeId, "all");
            var foreignFailed = foreign == null;
            if (foreignFailed)
            {
                _logger.LogWarning("Failed to fetch foreign orders for order {OrderId} — order book position not assessable", orderId);
            }
            foreign ??= new List<Models.Esi.Markets.RegionalMarketOrder>();

            var sdeAvailable = await _sde.IsDatabaseAvailableAsync();

            // SDE-Namen EINMAL für alle distinct Locations/Systeme laden (statt N+1 pro Order)
            var locationNameCache = new Dictionary<long, string>();
            var systemNameCache = new Dictionary<int, string>();
            if (sdeAvailable)
            {
                foreach (var locId in foreign.Select(o => o.LocationId)
                             .Append(own.LocationId).Distinct())
                {
                    locationNameCache[locId] = await _sde.GetLocationNameAsync(locId) ?? $"Loc {locId}";
                }
                foreach (var sysId in foreign.Select(o => o.SystemId).Distinct())
                {
                    systemNameCache[sysId] = (await _sde.GetSolarSystemAsync(sysId))?.Name ?? $"Sys {sysId}";
                }
            }

            var lines = new List<OrderBookLine>();

            foreach (var order in foreign)
            {
                if (order.OrderId == own.OrderId) continue; // eigene Order raus (wird unten separat eingefügt)

                lines.Add(new OrderBookLine
                {
                    OrderId = order.OrderId,
                    Price = order.Price,
                    VolumeRemain = order.VolumeRemain,
                    VolumeTotal = order.VolumeTotal,
                    LocationId = order.LocationId,
                    LocationName = sdeAvailable ? locationNameCache.GetValueOrDefault(order.LocationId) : null,
                    SystemId = order.SystemId,
                    SystemName = sdeAvailable ? systemNameCache.GetValueOrDefault(order.SystemId) : null,
                    IsBuyOrder = order.IsBuyOrder,
                    Range = order.Range,
                    RemainingDays = order.Duration,
                    Issued = order.Issued,
                    IsSameLocation = order.LocationId == own.LocationId
                });
            }

            var ownLine = new OrderBookLine
            {
                OrderId = own.OrderId,
                IsOwn = true,
                Price = own.Price,
                VolumeRemain = own.VolumeRemain,
                VolumeTotal = own.VolumeTotal,
                LocationId = own.LocationId,
                LocationName = sdeAvailable ? locationNameCache.GetValueOrDefault(own.LocationId) : null,
                IsBuyOrder = own.IsBuyOrder,
                Range = own.Range,
                RemainingDays = own.Duration,
                Issued = own.Issued,
                IsSameLocation = true
            };

            var typeName = sdeAvailable ? await _sde.GetTypeNameAsync(own.TypeId) : null;

            var context = BuildOrderBook(lines, ownLine);
            context.TypeId = own.TypeId;
            context.TypeName = typeName ?? $"Type {own.TypeId}";
            context.OwnOrderId = own.OrderId;
            context.OwnIsBuyOrder = own.IsBuyOrder;
            context.OwnPrice = own.Price;
            context.OwnRemaining = own.VolumeRemain;
            context.OwnLocationId = own.LocationId;
            context.OwnLocationName = ownLine.LocationName;

            // Cost Basis des Items nachschlagen (für Gewinn-/Break-even-Bewertung der Simulation)
            try
            {
                context.CostBasisPerUnit = await _costBasis.GetCostBasisPerUnitAsync(characterId, own.TypeId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not load cost basis for type {TypeId} (order book)", own.TypeId);
            }

            // Datenqualität des Orderbuchs: Fehler ≠ leeres Orderbuch.
            // Ein fehlgeschlagener Fremd-Abruf erzeugt keine Positions-Empfehlung.
            if (foreignFailed)
            {
                context.ForeignDataStatus = OrderBookDataStatus.Failed;
                context.ForeignDataError =
                    "Fremd-Orderbuch derzeit nicht verfügbar — ESI-Abruf fehlgeschlagen. " +
                    "Position und Preissimulation sind nicht bewertbar.";
                context.OwnPosition = 0;
                context.CompetingOrdersAhead = 0;
                context.IsHighestBuyAtLocation = false;
                context.IsLowestSellAtLocation = false;
            }
            else if (foreign.Count == 0)
            {
                // Gültig leeres Orderbuch: es gibt tatsächlich keine konkurrierenden Orders
                context.ForeignDataStatus = OrderBookDataStatus.Empty;
            }

            return context;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error building order book for order {OrderId}", orderId);
            return null;
        }
    }

    public OrderBookContext BuildOrderBook(IEnumerable<OrderBookLine> foreignOrders, OrderBookLine ownOrder)
    {
        var context = new OrderBookContext();

        var sells = foreignOrders.Where(o => !o.IsBuyOrder).ToList();
        var buys = foreignOrders.Where(o => o.IsBuyOrder).ToList();

        // EVE-Regeln: Käufer kauft vom BILLIGSTEN Anbieter → Sell aufsteigend nach Preis;
        // Verkäufer verkauft an den HÖCHSTEN Käufer → Buy absteigend nach Preis.
        // Bei gleichem Preis wird die ÄLTERE Order zuerst bedient (FIFO nach Issued).
        var sellSorted = sells
            .OrderBy(o => o.Price)
            .ThenBy(o => o.Issued)
            .ThenByDescending(o => o.VolumeRemain)
            .ToList();
        var buySorted = buys
            .OrderByDescending(o => o.Price)
            .ThenBy(o => o.Issued)
            .ThenByDescending(o => o.VolumeRemain)
            .ToList();

        // Eigene Order in die passende Schlange einfügen (gleiche Sortierregeln)
        if (ownOrder.IsBuyOrder)
        {
            var idx = buySorted.FindIndex(o =>
                o.Price < ownOrder.Price
                || (Math.Abs(o.Price - ownOrder.Price) < 0.005 && o.Issued > ownOrder.Issued));
            if (idx < 0) idx = buySorted.Count;
            buySorted.Insert(idx, ownOrder);
        }
        else
        {
            var idx = sellSorted.FindIndex(o =>
                o.Price > ownOrder.Price
                || (Math.Abs(o.Price - ownOrder.Price) < 0.005 && o.Issued > ownOrder.Issued));
            if (idx < 0) idx = sellSorted.Count;
            sellSorted.Insert(idx, ownOrder);
        }

        AssignPositions(sellSorted);
        AssignPositions(buySorted);
        context.SellSide = sellSorted;
        context.BuySide = buySorted;

        // Position/Kennzahlen der eigenen Order
        if (ownOrder.IsBuyOrder)
        {
            context.OwnPosition = ownOrder.Position;
            context.CompetingOrdersAhead = buySorted.Count(o =>
                o.Price > ownOrder.Price && o.IsSameLocation);
            context.IsHighestBuyAtLocation = buySorted
                .Where(o => o.IsSameLocation && !o.IsOwn)
                .All(o => o.Price <= ownOrder.Price);
        }
        else
        {
            context.OwnPosition = ownOrder.Position;
            context.CompetingOrdersAhead = sellSorted.Count(o =>
                o.Price < ownOrder.Price && o.IsSameLocation);
            context.IsLowestSellAtLocation = sellSorted
                .Where(o => o.IsSameLocation && !o.IsOwn)
                .All(o => o.Price >= ownOrder.Price);
        }

        return context;
    }

    private static void AssignPositions(List<OrderBookLine> lines)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            lines[i].Position = i + 1;
        }
    }

    public async Task<OrderChangeSimulation> SimulatePriceChangeAsync(
        int characterId,
        OrderBookContext context,
        double newPrice)
    {
        _ = characterId; // Auth-Zustand wird für öffentliche ESI-/SDE-Daten nicht benötigt
        var skills = await GetSkillsCachedAsync();
        return SimulatePriceChange(context, newPrice, skills);
    }

    public OrderChangeSimulation SimulatePriceChange(
        OrderBookContext context,
        double newPrice,
        Models.Esi.Character.CharacterSkills? skills)
    {
        var sim = new OrderChangeSimulation
        {
            NewPrice = newPrice,
            OldPrice = context.OwnPrice,
            Quantity = context.OwnRemaining
        };

        if (newPrice <= 0 || context.OwnRemaining <= 0)
        {
            sim.Summary = "Bitte einen gültigen Preis (> 0) eingeben.";
            return sim;
        }

        // Modify-Fee nach offizieller EVE-Formel (nur bei tatsächlicher Änderung)
        sim.ModifyFee = Math.Abs(newPrice - context.OwnPrice) < 0.005
            ? 0
            : _feeCalculator.CalculateOrderModifyFee(context.OwnPrice, newPrice, context.OwnRemaining, skills);

        // Neue Position: eigene Order (mit neuem Preis) neu in die Fremd-Schlange einsortieren.
        // BuildOrderBook enthält die eigene Order — also erst eigene rausfiltern, dann neu einfügen.
        var foreignSells = context.SellSide.Where(l => !l.IsOwn).ToList();
        var foreignBuys = context.BuySide.Where(l => !l.IsOwn).ToList();

        // Eigene Order (mit echtem Issued aus dem Kontext) auf den neuen Preis setzen
        var ownLine = context.SellSide.Concat(context.BuySide).FirstOrDefault(l => l.IsOwn)
                      ?? new OrderBookLine { Issued = DateTime.UtcNow };

        var ownWithNewPrice = new OrderBookLine
        {
            OrderId = context.OwnOrderId,
            IsOwn = true,
            Price = newPrice,
            VolumeRemain = context.OwnRemaining,
            VolumeTotal = context.OwnRemaining,
            LocationId = context.OwnLocationId,
            IsBuyOrder = context.OwnIsBuyOrder,
            Issued = ownLine.Issued, // echtes Einstelldatum für korrektes FIFO-Tie-Breaking
            IsSameLocation = true
        };

        var recomputed = BuildOrderBook(
            context.OwnIsBuyOrder ? foreignBuys : foreignSells,
            ownWithNewPrice);

        sim.NewPosition = recomputed.OwnPosition;
        sim.CompetingAhead = recomputed.CompetingOrdersAhead;
        sim.WouldBeBest = context.OwnIsBuyOrder
            ? recomputed.IsHighestBuyAtLocation
            : recomputed.IsLowestSellAtLocation;

        // Gewinn-Wirkung (nur Sell-Orders mit bekannter Cost Basis)
        if (!context.OwnIsBuyOrder && context.CostBasisPerUnit is { } costBasis && costBasis > 0)
        {
            var sellProceeds = _feeCalculator.CalculateSellProceeds(newPrice, context.OwnRemaining, skills).NetAmount;
            // Invariante (#4) wie in der Marktanalyse: Die gespeicherte Cost Basis
            // enthält die Erwerbskosten genau einmal — kein erneuter Buy-Aufschlag.
            var acquisitionCost = costBasis * context.OwnRemaining;
            var netProfit = sellProceeds - acquisitionCost - sim.ModifyFee;
            var roi = acquisitionCost > 0 ? netProfit / acquisitionCost * 100 : 0;

            sim.NetProfitAfterChange = netProfit;
            sim.RoiPercent = roi;
            sim.BreakEvenPrice = _feeCalculator.CalculateBreakEvenSellPriceForStoredBasis(costBasis, skills);

            if (netProfit > 0)
            {
                sim.Summary = netProfit >= sim.ModifyFee
                    ? $"Bei {newPrice:N2} ISK verdienst du netto {netProfit:N0} ISK (inkl. Modify-Fee {sim.ModifyFee:N0} ISK). Unter {sim.BreakEvenPrice:N2} ISK lohnt es sich nicht mehr."
                    : $"Bei {newPrice:N2} ISK machst du {netProfit:N0} ISK Verlust — {sim.ModifyFee:N0} ISK Modify-Fee fressen den Gewinn auf. Ab {sim.BreakEvenPrice:N2} ISK wärst du im Plus.";
            }
            else
            {
                sim.Summary = $"Bei {newPrice:N2} ISK machst du {netProfit:N0} ISK Verlust — das lohnt sich nicht. Mindestens {sim.BreakEvenPrice:N2} ISK nötig (ohne Modify-Fee).";
            }
        }
        else if (context.OwnIsBuyOrder)
        {
            var posText = sim.WouldBeBest
                ? $"Mit {newPrice:N2} ISK wärst du der höchste Käufer an deiner Station."
                : $"Mit {newPrice:N2} ISK stehst du an Position {sim.NewPosition} der Kauf-Schlange.";
            var feeText = sim.ModifyFee > 0
                ? $" Die Änderung kostet {sim.ModifyFee:N0} ISK Modify-Fee."
                : "";
            sim.Summary = $"{posText}{feeText} Ob sich der höhere Kaufpreis lohnt, hängt von deinem Weiterverkaufspreis ab.";
        }
        else
        {
            var posText = sim.WouldBeBest
                ? $"Mit {newPrice:N2} ISK wärst du der günstigste Anbieter an deiner Station."
                : $"Mit {newPrice:N2} ISK stehst du an Position {sim.NewPosition} der Verkaufs-Schlange.";
            sim.Summary = $"{posText} Keine Cost Basis bekannt — die Gewinn-Wirkung kann nicht berechnet werden. Pflege sie unter 'Cost Basis', um Gewinn/Verlust zu sehen.";
        }

        return sim;
    }
}