namespace WALLEve.Services.Market.Interfaces;

/// <summary>Eine Zeile im Orderbuch (eigene oder fremde Order).</summary>
public class OrderBookLine
{
    public long OrderId { get; set; }
    public bool IsOwn { get; set; }
    public double Price { get; set; }
    public int VolumeRemain { get; set; }
    public int VolumeTotal { get; set; }
    public long LocationId { get; set; }
    public string? LocationName { get; set; }
    public int SystemId { get; set; }
    public string? SystemName { get; set; }
    public bool IsBuyOrder { get; set; }
    public string Range { get; set; } = "station"; // "station", "solarsystem", "region", "1".."40"
    public int RemainingDays { get; set; }
    public int Position { get; set; }       // Rang in der Preisschlange (1 = vorne)
    public bool IsSameLocation { get; set; } // gleiche Station/Struktur wie eigene Order

    /// <summary>Einstelldatum — EVE bedient bei gleichem Preis die ÄLTERE Order zuerst (FIFO).</summary>
    public DateTime Issued { get; set; }
}

/// <summary>
/// Markt-Kontext einer eigenen Order: fremde Orders desselben Items in derselben
/// Region, sortiert wie im EVE-Markt (Sell aufsteigend nach Preis, Buy absteigend),
/// plus Position der eigenen Order in der Preisschlange.
/// </summary>
public class OrderBookContext
{
    public int TypeId { get; set; }
    public string? TypeName { get; set; }
    public long OwnOrderId { get; set; }
    public bool OwnIsBuyOrder { get; set; }
    public double OwnPrice { get; set; }
    public int OwnRemaining { get; set; }
    public long OwnLocationId { get; set; }
    public string? OwnLocationName { get; set; }

    /// <summary>Sell-Orders aufsteigend nach Preis (billigste zuerst = Spitze der Schlange).</summary>
    public List<OrderBookLine> SellSide { get; set; } = new();

    /// <summary>Buy-Orders absteigend nach Preis (höchste zuerst = Spitze der Schlange).</summary>
    public List<OrderBookLine> BuySide { get; set; } = new();

    /// <summary>Position der eigenen Order (1 = vorne, 0 = nicht gefunden).</summary>
    public int OwnPosition { get; set; }

    /// <summary>Wie viele Anbieter/Abnehmer stehen in der eigenen Schlange weiter vorne (gleiche Location).</summary>
    public int CompetingOrdersAhead { get; set; }

    /// <summary>Korrekt für den Käufer billigster Anbieter? (nur Sell, gleiche Location).</summary>
    public bool IsLowestSellAtLocation { get; set; }

    /// <summary>Korrekt höchster Käufer? (nur Buy, gleiche Location).</summary>
    public bool IsHighestBuyAtLocation { get; set; }
}

public interface IOrderIntelligenceService
{
    /// <summary>Lädt den Markt-Kontext für eine eigene Order (fremde Orders + Position).</summary>
    Task<OrderBookContext?> GetOrderBookAsync(int characterId, long orderId);

    /// <summary>
    /// Sortiert die Fremd-Orders nach EVE-Regeln und berechnet die Position der eigenen Order.
    /// </summary>
    OrderBookContext BuildOrderBook(
        IEnumerable<OrderBookLine> foreignOrders,
        OrderBookLine ownOrder);
}