using WALLEve.Services.Esi.Interfaces;
using WALLEve.Services.Market.Interfaces;
using WALLEve.Services.Sde.Interfaces;

namespace WALLEve.Services.Market;

public class OrderIntelligenceService : IOrderIntelligenceService
{
    private readonly IEsiApiService _esiApi;
    private readonly ISdeUniverseService _sde;
    private readonly ILogger<OrderIntelligenceService> _logger;

    public OrderIntelligenceService(
        IEsiApiService esiApi,
        ISdeUniverseService sde,
        ILogger<OrderIntelligenceService> logger)
    {
        _esiApi = esiApi;
        _sde = sde;
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

            // Fremde Orders desselben Items in derselben Region (ESI-cached ~5 Min)
            var foreign = await _esiApi.GetAllRegionalMarketOrdersAsync(own.RegionId, own.TypeId, "all")
                          ?? new List<Models.Esi.Markets.RegionalMarketOrder>();

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
}