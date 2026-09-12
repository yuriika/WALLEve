namespace WALLEve.Services.Market.Interfaces;

/// <summary>
/// Datenqualität der fremden Orderbuch-Daten.
/// Bewusst getrennt vom Inhalt: ein fehlgeschlagener ESI-Abruf ist kein
/// „leeres Orderbuch" und darf keine Positions-Empfehlung erzeugen.
/// </summary>
public enum OrderBookDataStatus
{
    /// <summary>Fremd-Orders vollständig geladen.</summary>
    Ok,

    /// <summary>Gültig leeres Orderbuch — es gibt tatsächlich keine konkurrierenden Orders.</summary>
    Empty,

    /// <summary>ESI-Abruf fehlgeschlagen — Position und Simulation nicht bewertbar.</summary>
    Failed
}

/// <summary>
/// Stufen der Marktliquidität aus Orderbuchtiefe und historischem Tagesvolumen.
/// Bewusst KEINE Besitzmenge: die eigene Bestandsmenge ändert die Marktliquidität nicht.
/// </summary>
public enum LiquidityTier
{
    /// <summary>Weder Tiefe noch History verfügbar — Liquidität nicht bewertbar.</summary>
    Unknown,

    /// <summary>Geringe bewertbare Menge und/oder geringes Tagesvolumen — oder nur ein Signal vorhanden.</summary>
    Low,

    /// <summary>Mittlere kumulierte Tiefe und/oder mittleres Tagesvolumen.</summary>
    Medium,

    /// <summary>Hohe kumulierte Tiefe UND hohes historisches Tagesvolumen.</summary>
    High
}

/// <summary>
/// Liquiditätsindikator aus Marktdaten statt Besitzmenge (#30): kumulative
/// Orderbuchtiefe (mehrstufig) und verfügbares historisches Tagesvolumen.
/// Fehlende oder veraltete History ist explizit Unknown — niemals „liquide".
/// </summary>
public class LiquidityIndicator
{
    /// <summary>Sell-Orders vorhanden (mindestens eine Preisstufe).</summary>
    public bool HasDepth { get; set; }

    /// <summary>
    /// Bewertbare Verkaufsmenge: kumulierte Restmenge der Sell-Orders innerhalb des
    /// Preisbandes um den besten Sell-Preis über MEHRERE Preisstufen. Die einzelne
    /// Top-Order allein genügt nicht (mehrstufiges Orderbuch begrenzt die Menge korrekt).
    /// </summary>
    public long AppraisableQuantity { get; set; }

    /// <summary>Anzahl berücksichtigter Preisstufen (1..maxDepthLevels).</summary>
    public int DepthLevelsUsed { get; set; }

    /// <summary>History vorhanden und nicht veraltet.</summary>
    public bool HasHistory { get; set; }

    /// <summary>Ø Tagesvolumen aus der frischesten History.</summary>
    public long AverageDailyVolume { get; set; }

    /// <summary>Anzahl ausgewerteter Historientage.</summary>
    public int HistoryDays { get; set; }

    /// <summary>Weder Tiefe noch frische History vorhanden — Liquidität nicht bewertbar.</summary>
    public bool IsUnknown => !HasDepth && !HasHistory;

    /// <summary>Abgeleitete Liquiditätsstufe (nie High ohne BEIDE Signale).</summary>
    public LiquidityTier Tier { get; set; }

    /// <summary>
    /// Leitet die Stufe aus den Signalen ab: High/Medium nur mit BEIDEN Signalen
    /// (kumulierte Tiefe + frische History), ein einzelnes Signal ist höchstens Low.
    /// Fehlende/veraltete History kann damit nie „liquide" ergeben (#30).
    /// </summary>
    public static LiquidityTier DeriveTier(
        bool hasDepth, long appraisableQuantity, bool hasHistory, long averageDailyVolume)
    {
        if (!hasDepth && !hasHistory) return LiquidityTier.Unknown;
        if (hasDepth && hasHistory)
        {
            if (appraisableQuantity >= 500 && averageDailyVolume >= 50_000) return LiquidityTier.High;
            if (appraisableQuantity >= 100 && averageDailyVolume >= 10_000) return LiquidityTier.Medium;
        }
        return LiquidityTier.Low;
    }
}

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

    /// <summary>Cost Basis (Einkaufspreis) pro Einheit, falls für den TypeId bekannt.</summary>
    public double? CostBasisPerUnit { get; set; }

    /// <summary>Wie viele Anbieter/Abnehmer stehen in der eigenen Schlange weiter vorne (gleiche Location).</summary>
    public int CompetingOrdersAhead { get; set; }

    /// <summary>Korrekt für den Käufer billigster Anbieter? (nur Sell, gleiche Location).</summary>
    public bool IsLowestSellAtLocation { get; set; }

    /// <summary>Korrekt höchster Käufer? (nur Buy, gleiche Location).</summary>
    public bool IsHighestBuyAtLocation { get; set; }

    /// <summary>Datenqualität der fremden Orderbuch-Daten (Default Ok für bestehende Aufrufer).</summary>
    public OrderBookDataStatus ForeignDataStatus { get; set; } = OrderBookDataStatus.Ok;

    /// <summary>Fehlertext bei <see cref="OrderBookDataStatus.Failed"/>.</summary>
    public string? ForeignDataError { get; set; }

    /// <summary>
    /// Liquiditätsbewertung aus Orderbuchtiefe + History (null = noch nicht bewertet).
    /// Bewusst unabhängig von der eigenen Besitzmenge.
    /// </summary>
    public LiquidityIndicator? Liquidity { get; set; }
}

/// <summary>Ergebnis einer Preisänderungs-Simulation („Was wäre wenn?").</summary>
public class OrderChangeSimulation
{
    public double NewPrice { get; set; }
    public double OldPrice { get; set; }
    public int Quantity { get; set; }

    /// <summary>Modify-Fee nach offizieller EVE-Formel (Skills/ABR berücksichtigt), min 100 ISK.</summary>
    public double ModifyFee { get; set; }

    /// <summary>Neue Position in der Preisschlange bei gleicher Location (1 = vorne).</summary>
    public int NewPosition { get; set; }

    /// <summary>Konkurrenten an gleicher Location, die weiter vorne stehen würden.</summary>
    public int CompetingAhead { get; set; }

    /// <summary>Wäre die Order damit bestplatziert (günstigster Verkäufer / höchster Käufer)?</summary>
    public bool WouldBeBest { get; set; }

    /// <summary>Geschätzter Netto-Gewinn NACH Modify-Fee (nur Sell-Orders mit Cost Basis).</summary>
    public double? NetProfitAfterChange { get; set; }

    /// <summary>ROI in % (nur Sell-Orders mit Cost Basis).</summary>
    public double? RoiPercent { get; set; }

    /// <summary>Verkaufspreis, ab dem (ohne Modify-Fee) kein Verlust entsteht.</summary>
    public double? BreakEvenPrice { get; set; }

    /// <summary>Bedeutung des Ergebnisses als Klartext (deutsch).</summary>
    public string Summary { get; set; } = string.Empty;
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

    /// <summary>
    /// Simuliert eine Preisänderung der eigenen Order: Modify-Fee (offizielle Formel),
    /// neue Position in der Schlange, Netto-Wirkung in ISK (Sell + Cost Basis vorhanden).
    /// Rein und testbar — keine DB-/ESI-Zugriffe.
    /// </summary>
    OrderChangeSimulation SimulatePriceChange(
        OrderBookContext context,
        double newPrice,
        WALLEve.Models.Esi.Character.CharacterSkills? skills);

    /// <summary>
    /// Wie SimulatePriceChange, holt aber die Charakter-Skills selbst (15-Min-Cache).
    /// Für Aufrufe aus der UI.
    /// </summary>
    Task<OrderChangeSimulation> SimulatePriceChangeAsync(
        int characterId,
        OrderBookContext context,
        double newPrice);

    /// <summary>
    /// Bewertet die Marktliquidität eines Items aus kumulativer Orderbuchtiefe
    /// (mehrstufig, innerhalb eines Preisbandes um den besten Sell-Preis) und der
    /// verfügbaren historischen Tagesmenge. Rein und testbar — keine DB-/ESI-Zugriffe.
    /// Fehlende oder veraltete History macht die Bewertung maximal „Low" — nie liquide.
    /// </summary>
    LiquidityIndicator AssessLiquidity(
        IReadOnlyList<OrderBookLine> sellSideAscending,
        long? averageDailyVolume,
        int historyDays,
        bool historyStale,
        int maxDepthLevels = 5,
        double maxPriceStepPercent = 2.0);
}