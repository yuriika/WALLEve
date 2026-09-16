using System.Globalization;
using WALLEve.Models.Database;
using WALLEve.Models.Esi.Markets;

namespace WALLEve.Services.Trading;

/// <summary>
/// Ergebnis der StationTrade-Kandidaten-Analyse (Issue #71): entweder ein
/// ausführbarer Kandidat (alle Qualitätsprüfungen bestanden — Menge, Preise,
/// Tiefe, History, Gebühren) oder eine explizite deutsche Ablehnungsbegründung.
/// Reine C#-Logik ohne Datenbank und ohne externe Dienste; gleiche Eingaben
/// plus gleiche Algorithmusversion reproduzieren das Ergebnis exakt.
/// </summary>
public sealed record StationTradeCandidateResult(
    bool IsActionable,
    string? NotActionableReason,
    string AlgorithmVersion,
    int? Quantity,
    double? BuyPricePerUnit,
    double? SellPricePerUnit,
    long? BuyLocationId,
    long? SellLocationId,
    int? BuySystemId,
    int? SellSystemId,
    double? CumulativeAskDepth,
    double? CumulativeBidDepth,
    double? AvgDailyVolume,
    int? HistoryDays,
    double? NetProfit,
    double? RequiredCapital,
    double? RoiPercent,
    double? TopAskAgeHours,
    double? TopBidAgeHours,
    string Evidence);

/// <summary>
/// Gebühren-Eingaben des Kandidaten als Dezimal-Sätze (z. B. 0.015 = 1,5 %).
/// Abgeleitet aus dem FeeProfile des FeeCalculatorService (echte Char-Skills,
/// Issue #46) — der Motor selbst ist rein und bekommt die Sätze übergeben.
/// </summary>
public sealed record StationTradeFees(double BrokerFeeRate, double SalesTaxRate);

/// <summary>
/// Reine StationTrade-Kandidaten-Engine (Issue #71): prüft einen Kandidaten
/// (Kauf gegen Aufträge an der Kaufstation, Verkauf an der Verkaufsstation)
/// gegen kumulative Tiefe, historische Mengen und Preise, Ausreißer-/
/// Manipulations-Erkennung, Kapital und Gebühren. Keine zweite Ranking-Engine:
/// Die vorhandene Pipeline (TradeProfileFilter → TradeRankingEngine → Action
/// Cards) konsumiert das Ergebnis unverändert.
/// </summary>
/// <remarks>
/// Ablehnungsgründe (deutsch, deterministisch) — „keine belastbare Chance":
/// <list type="bullet">
/// <item>Keine Orders an Kauf- oder Verkaufsort.</item>
/// <item>Fehlende History: weniger als <see cref="MinHistoryDays"/> Tage im
/// Fenster von <see cref="HistoryWindowDays"/> Tagen.</item>
/// <item>Manipulierte Top-Order: Best-Ask unter <see cref="OutlierAskDiscountFactor"/>
/// oder Best-Bid über <see cref="OutlierBidPremiumFactor"/> des History-Schnitts.</item>
/// <item>Illiquider Spread: ausführbare Menge unter <see cref="MinExecutableQuantity"/>
/// oder über dem <see cref="MaxShareOfDailyVolume"/>-Fachen des historischen
/// Tagesvolumens (Tiefe ist fake/übertrieben).</item>
/// <item>Negative Nettomarge nach Gebühren (Broker auf Kauf UND Verkauf,
/// Sales Tax auf Verkauf — offizielle EVE-Formel, identisch zu
/// <see cref="WALLEve.Services.Market.FeeCalculatorService"/>); auch Break-even wird ausgewiesen.</item>
/// </list>
/// Alle Schwellen sind dokumentierte Konstanten, bewusst item-unabhängig
/// (keine erfundenen Item-Regeln); die Profile-Grenzen (MinProfit, MinQuality,
/// MaxCapital …) bleiben Aufgabe der vorhandenen Filterpipeline.
/// </remarks>
public static class StationTradeCandidateEngine
{
    /// <summary>Version der Analyse; Bestandteil jedes Ergebnisses und des Vertrags.</summary>
    public const string AlgorithmVersion = "station-trade-v1";

    /// <summary>History-Fenster in Tagen (ESI liefert tägliche Markthistorie).</summary>
    public const int HistoryWindowDays = 30;

    /// <summary>Mindestanzahl History-Tage im Fenster für eine belastbare Chance.</summary>
    public const int MinHistoryDays = 14;

    /// <summary>
    /// Manipulations-Schwelle Kauf: Ein Best-Ask unter dem Faktor × History-Schnitt
    /// ist eine verdächtige Top-Order (klassische Lockvogel-Manipulation), kein
    /// belastbarer Preis.
    /// </summary>
    public const double OutlierAskDiscountFactor = 0.50;

    /// <summary>
    /// Manipulations-Schwelle Verkauf: Ein Best-Bid über dem Faktor × History-Schnitt
    /// ist eine verdächtige Top-Order (übertrieben hohes Lockgebot).
    /// </summary>
    public const double OutlierBidPremiumFactor = 2.0;

    /// <summary>Preistoleranz für die kumulative Tiefe (Top-Preis ± %).</summary>
    public const double DepthPriceTolerance = 0.02;

    /// <summary>Mindest-mengenmäßig ausführbare Einheiten (Tiefe auf beiden Seiten).</summary>
    public const int MinExecutableQuantity = 100;

    /// <summary>Illiquiditäts-Schwelle: ausführbare Menge über Faktor × historischem Tagesvolumen.</summary>
    public const double MaxShareOfDailyVolume = 3.0;

    public static StationTradeCandidateResult Evaluate(
        int regionId,
        int typeId,
        IReadOnlyList<RegionalMarketOrder> asks,
        IReadOnlyList<RegionalMarketOrder> bids,
        IReadOnlyList<MarketHistory> history,
        StationTradeFees fees,
        DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(asks);
        ArgumentNullException.ThrowIfNull(bids);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(fees);

        // Kaufseite = Verkaufsaufträge (Asks) an der Kaufstation, aufsteigend;
        // Verkaufsseite = Kaufaufträge (Bids) an der Verkaufsstation, absteigend.
        var askOrders = asks.Where(o => !o.IsBuyOrder).OrderBy(o => o.Price).ToList();
        var bidOrders = bids.Where(o => o.IsBuyOrder).OrderByDescending(o => o.Price).ToList();

        if (askOrders.Count == 0 || bidOrders.Count == 0)
        {
            return Rejected(regionId, typeId, "Keine Orders an Kauf- oder Verkaufsort — kein belastbarer Kandidat.");
        }

        var bestAsk = askOrders[0];
        var bestBid = bidOrders[0];

        if (fees.BrokerFeeRate < 0 || fees.SalesTaxRate < 0
            || fees.BrokerFeeRate + fees.SalesTaxRate >= 1.0)
        {
            return Rejected(regionId, typeId, "Ungültige Gebühren-Sätze (Broker und/oder Steuer außerhalb des gültigen Bereichs).");
        }

        // History-Fenster: nur Einträge innerhalb des dokumentierten Fensters zählen.
        var windowStart = utcNow.AddDays(-HistoryWindowDays);
        var window = history.Where(h => h.Date >= windowStart.Date && h.Date <= utcNow.Date).ToList();
        if (window.Count < MinHistoryDays)
        {
            return Rejected(regionId, typeId,
                $"Fehlende History: nur {window.Count} von mindestens {MinHistoryDays} Tagen im {HistoryWindowDays}-Tage-Fenster — keine belastbare Chance.");
        }

        var avgPrice = window.Average(h => h.Average);
        var avgDailyVolume = window.Average(h => (double)h.Volume);

        // Manipulierte Top-Order (Ausreißer gegen den History-Schnitt): Ein Preis
        // massiv unter/über dem 30-Tage-Schnitt ist keine echte Marktquote.
        if (bestAsk.Price < avgPrice * OutlierAskDiscountFactor)
        {
            return Rejected(regionId, typeId,
                $"Verdächtige Top-Order: Best-Ask {bestAsk.Price.ToString("N2", CultureInfo.InvariantCulture)} ISK liegt unter {OutlierAskDiscountFactor:P0} des 30-Tage-Schnitts ({avgPrice.ToString("N2", CultureInfo.InvariantCulture)} ISK) — manipulierte Top-Order, keine belastbare Chance.");
        }

        if (bestBid.Price > avgPrice * OutlierBidPremiumFactor)
        {
            return Rejected(regionId, typeId,
                $"Verdächtige Top-Order: Best-Bid {bestBid.Price.ToString("N2", CultureInfo.InvariantCulture)} ISK liegt über dem {OutlierBidPremiumFactor:F0}-fachen des 30-Tage-Schnitts ({avgPrice.ToString("N2", CultureInfo.InvariantCulture)} ISK) — manipulierte Top-Order, keine belastbare Chance.");
        }

        // Kumulative Tiefe innerhalb der Preistoleranz um den Top-Preis:
        // ausführbar ist nur, was bei diesen Preisen tatsächlich geordert ist.
        var askCeiling = bestAsk.Price * (1.0 + DepthPriceTolerance);
        var bidFloor = bestBid.Price * (1.0 - DepthPriceTolerance);
        var cumAskDepth = askOrders.Where(o => o.Price <= askCeiling).Sum(o => Math.Max(0, o.VolumeRemain));
        var cumBidDepth = bidOrders.Where(o => o.Price >= bidFloor).Sum(o => Math.Max(0, o.VolumeRemain));
        var executableQuantity = (int)Math.Min(cumAskDepth, cumBidDepth);

        if (executableQuantity < MinExecutableQuantity)
        {
            return Rejected(regionId, typeId,
                $"Illiquider Spread: nur {executableQuantity:N0} ausführbare Einheiten (Ask-Tiefe {cumAskDepth:N0}, Bid-Tiefe {cumBidDepth:N0}, Minimum {MinExecutableQuantity:N0}) — keine belastbare Chance.");
        }

        // Historische Mengen begrenzen die Belastbarkeit: Wer mehr kaufen will,
        // als das Markt-Tagesvolumen hergibt, sieht Tiefe statt Liquidität.
        if (avgDailyVolume > 0 && executableQuantity > avgDailyVolume * MaxShareOfDailyVolume)
        {
            return Rejected(regionId, typeId,
                $"Illiquide Tiefe: {executableQuantity:N0} Einheiten übersteigen das {MaxShareOfDailyVolume:F0}-fache des historischen Tagesvolumens ({avgDailyVolume:N0}) — keine belastbare Chance.");
        }

        // Gebühren (offizielle EVE-Formel, identisch zu FeeCalculatorService):
        // Brokergebühr auf Kauf UND Verkauf, Sales Tax nur auf Verkauf.
        var buyCostPerUnit = bestAsk.Price * (1.0 + fees.BrokerFeeRate);
        var sellNetPerUnit = bestBid.Price * (1.0 - fees.BrokerFeeRate - fees.SalesTaxRate);
        var netPerUnit = sellNetPerUnit - buyCostPerUnit;

        if (netPerUnit <= 0)
        {
            return Rejected(regionId, typeId,
                $"Negative Nettomarge nach Gebühren: Kauf {buyCostPerUnit.ToString("N2", CultureInfo.InvariantCulture)} ISK/Stück (inkl. Broker {fees.BrokerFeeRate:P1}), Verkauf netto {sellNetPerUnit.ToString("N2", CultureInfo.InvariantCulture)} ISK/Stück (Broker + Steuer {fees.SalesTaxRate:P1}) — kein belastbarer Kandidat.");
        }

        var netProfit = netPerUnit * executableQuantity;
        var requiredCapital = bestAsk.Price * executableQuantity;
        var buyCostNet = buyCostPerUnit * executableQuantity;
        var roi = buyCostNet > 0 ? (netProfit / buyCostNet) * 100 : 0;
        var breakEven = buyCostPerUnit / Math.Max(double.Epsilon, 1.0 - fees.BrokerFeeRate - fees.SalesTaxRate);

        var topAskAgeHours = (utcNow - bestAsk.Issued).TotalHours;
        var topBidAgeHours = (utcNow - bestBid.Issued).TotalHours;

        var evidence =
            $"Station-Trade (Region {regionId}): Kauf bei {bestAsk.LocationId} ({bestAsk.Price.ToString("N2", CultureInfo.InvariantCulture)} ISK/Stück, Ask-Tiefe {cumAskDepth:N0}, Top-Order {topAskAgeHours:F0} h alt) — " +
            $"Verkauf bei {bestBid.LocationId} ({bestBid.Price.ToString("N2", CultureInfo.InvariantCulture)} ISK/Stück, Bid-Tiefe {cumBidDepth:N0}, Top-Order {topBidAgeHours:F0} h alt). " +
            $"History: {window.Count} Tage, Schnitt {avgPrice.ToString("N2", CultureInfo.InvariantCulture)} ISK, {avgDailyVolume:N0} Stück/Tag. " +
            $"Ausführbar: {executableQuantity:N0} Stück (Min(Tiefen)). " +
            $"Netto {netProfit:N0} ISK (ROI {roi:F1}%), Kapital {requiredCapital:N0} ISK, Break-even {breakEven.ToString("N2", CultureInfo.InvariantCulture)} ISK. " +
            $"Gebühren: Broker {fees.BrokerFeeRate:P1}, Steuer {fees.SalesTaxRate:P1}.";

        return new StationTradeCandidateResult(
            IsActionable: true,
            NotActionableReason: null,
            AlgorithmVersion,
            Quantity: executableQuantity,
            BuyPricePerUnit: bestAsk.Price,
            SellPricePerUnit: bestBid.Price,
            BuyLocationId: bestAsk.LocationId,
            SellLocationId: bestBid.LocationId,
            BuySystemId: bestAsk.SystemId,
            SellSystemId: bestBid.SystemId,
            CumulativeAskDepth: cumAskDepth,
            CumulativeBidDepth: cumBidDepth,
            AvgDailyVolume: avgDailyVolume,
            HistoryDays: window.Count,
            NetProfit: netProfit,
            RequiredCapital: requiredCapital,
            RoiPercent: roi,
            TopAskAgeHours: topAskAgeHours,
            TopBidAgeHours: topBidAgeHours,
            Evidence: evidence);
    }

    private static StationTradeCandidateResult Rejected(int regionId, int typeId, string reason)
        => new(
            IsActionable: false,
            NotActionableReason: reason,
            AlgorithmVersion,
            Quantity: null,
            BuyPricePerUnit: null,
            SellPricePerUnit: null,
            BuyLocationId: null,
            SellLocationId: null,
            BuySystemId: null,
            SellSystemId: null,
            CumulativeAskDepth: null,
            CumulativeBidDepth: null,
            AvgDailyVolume: null,
            HistoryDays: null,
            NetProfit: null,
            RequiredCapital: null,
            RoiPercent: null,
            TopAskAgeHours: null,
            TopBidAgeHours: null,
            Evidence: reason);
}