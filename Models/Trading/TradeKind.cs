namespace WALLEve.Models.Trading;

/// <summary>
/// Art eines Trade-Vertrags (Issue #37). Der Wert entspricht dem gespeicherten
/// Ganzzahlwert in der Datenbank — niemals umbenennen, nur anhängen.
/// </summary>
public enum TradeKind
{
    /// <summary>Verkauf aus dem eigenen Bestand (inventory_sell).</summary>
    InventorySell = 1,

    /// <summary>Preisänderung einer bestehenden Order (order_adjustment).</summary>
    OrderAdjustment = 2,

    /// <summary>Kauf und Verkauf an zwei Stationen desselben Markts (station_trade).</summary>
    StationTrade = 3,

    /// <summary>Kauf an einem und Verkauf an einem anderen Ort mit Route (route_trade).</summary>
    RouteTrade = 4
}

public static class TradeKindExtensions
{
    /// <summary>DB-String im Format der bestehenden <c>TradingOpportunity.OpportunityType</c>-Werte.</summary>
    public static string ToDatabaseValue(this TradeKind kind) => kind switch
    {
        TradeKind.InventorySell => "inventory_sell",
        TradeKind.OrderAdjustment => "order_adjustment",
        TradeKind.StationTrade => "station_trade",
        TradeKind.RouteTrade => "route_trade",
        _ => "unknown"
    };

    public static TradeKind FromDatabaseValue(string value) => value switch
    {
        "inventory_sell" => TradeKind.InventorySell,
        "order_adjustment" => TradeKind.OrderAdjustment,
        "station_trade" => TradeKind.StationTrade,
        "route_trade" => TradeKind.RouteTrade,
        _ => throw new ArgumentOutOfRangeException(nameof(value), $"Unbekannter TradeKind-Wert: {value}")
    };
}