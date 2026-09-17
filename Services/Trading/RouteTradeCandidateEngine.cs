using System.Globalization;
using WALLEve.Models.Database;
using WALLEve.Models.Esi.Markets;
using WALLEve.Models.Risk;

namespace WALLEve.Services.Trading;

/// <summary>
/// Ergebnis der RouteTrade-Kandidaten-Analyse (Issue #74): entweder ein
/// ausführbarer Kandidat (gültige Route, Menge erfüllt Cargo/Kapital/Tiefe
/// gleichzeitig, positive Nettomarge nach Gebühren UND Transport) oder eine
/// explizite deutsche Ablehnungsbegründung. Reine C#-Logik ohne Datenbank und
/// ohne externe Dienste; gleiche Eingaben plus gleiche Algorithmusversion
/// reproduzieren das Ergebnis exakt.
/// </summary>
public sealed record RouteTradeCandidateResult(
    bool IsActionable,
    string? NotActionableReason,
    string AlgorithmVersion,
    int? Quantity,
    int? CargoLimitQuantity,
    int? CapitalLimitQuantity,
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
    int? JumpCount,
    double? TransportMinutes,
    double? NetProfit,
    double? NetProfitExclTransport,
    double? RequiredCapital,
    double? RoiPercent,
    double? TopAskAgeHours,
    double? TopBidAgeHours,
    RiskLevel? RouteRiskLevel,
    IReadOnlyList<string> RiskLines,
    string Evidence);

/// <summary>
/// Route-Eingabe des Kandidaten: vollständiger Pfad inklusive Start- und
/// Zielsystem mit Sicherheits-Aufbruch. Eine gültige Route hat einen Pfad mit
/// mindestens zwei Systemen (mindestens ein Sprung) — ohne Route kein Handel.
/// </summary>
public sealed record RouteTradeRoute(
    IReadOnlyList<int> SystemIds,
    int Jumps,
    int HighSecJumps,
    int LowSecJumps,
    int NullSecJumps);

/// <summary>
/// Annahmen des Kandidaten (Issue #74): Cargo-Kapazität in Einheiten des Items
/// (der Aufrufer rechnet m³ → Einheiten über die SDE-Item-Volumina um), maximale
/// Kapitalbindung in ISK, Zeitannahme je Sprung in Minuten und Transportkosten
/// je Einheit in ISK (0 = Selbsttransport ohne direkte Kosten). Bewusst
/// parameterisiert statt erfunden — dieselbe Item-Unabhängigkeit wie bei
/// <see cref="StationTradeCandidateEngine"/>.
/// </summary>
public sealed record RouteTradeAssumptions(
    int CargoUnits,
    double MaxCapitalIsk,
    double MinutesPerJump,
    double TransportCostPerUnit);

/// <summary>
/// Reine RouteTrade-Kandidaten-Engine (Issue #74): bewertet Kauf in einer
/// Region/Location und Verkauf in einer anderen über eine gemeinsame Route.
/// Die ausführbare Menge erfüllt Cargo, Kapital und Tiefe GLEICHZEITIG
/// (Minimum der drei Grenzen); ohne gültige Route gibt es keinen Handel.
/// Risikoevidenz (RouteRiskService, #73) ist reines Enrichment: Sie verändert
/// weder Menge noch Netto — fällt zKillboard aus, bleibt die erwartete
/// Netto-Spanne (mit/ohne Transportkosten) vollständig nachvollziehbar.
/// </summary>
/// <remarks>
/// Ablehnungsgründe (deutsch, deterministisch) — „keine belastbare Chance“:
/// <list type="bullet">
/// <item>Keine gültige Route (leerer Pfad oder null Sprünge).</item>
/// <item>Keine Orders an Kauf- oder Verkaufsort.</item>
/// <item>Fehlende History: weniger als <see cref="StationTradeCandidateEngine.MinHistoryDays"/>
/// Tage im <see cref="StationTradeCandidateEngine.HistoryWindowDays"/>-Fenster.</item>
/// <item>Manipulierte Top-Order (identische Ausreißer-Schwellen wie StationTrade).</item>
/// <item>Ausführbare Menge unter <see cref="StationTradeCandidateEngine.MinExecutableQuantity"/>
/// oder über dem <see cref="StationTradeCandidateEngine.MaxShareOfDailyVolume"/>-Fachen des
/// Tagesvolumens.</item>
/// <item>Negative Nettomarge nach Gebühren UND Transport; Break-even wird ausgewiesen.</item>
/// </list>
/// Alle Schwellen sind die dokumentierten Konstanten der StationTrade-Engine —
/// bewusst einheitlich, keine erfundenen Item-Regeln.
/// </remarks>
public static class RouteTradeCandidateEngine
{
    /// <summary>Version der Analyse; Bestandteil jedes Ergebnisses und des Vertrags.</summary>
    public const string AlgorithmVersion = "route-trade-v1";

    public static RouteTradeCandidateResult Evaluate(
        int buyRegionId,
        int sellRegionId,
        int typeId,
        IReadOnlyList<RegionalMarketOrder> asks,
        IReadOnlyList<RegionalMarketOrder> bids,
        IReadOnlyList<MarketHistory> history,
        StationTradeFees fees,
        RouteTradeRoute route,
        RouteTradeAssumptions assumptions,
        RouteRiskSummary? risk,
        DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(asks);
        ArgumentNullException.ThrowIfNull(bids);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(fees);
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(assumptions);

        // Gültige Route: Pfad mit mindestens zwei Systemen (= mindestens ein Sprung).
        var hasValidRoute = route.SystemIds != null
            && route.SystemIds.Count >= 2
            && route.Jumps >= 1;
        if (!hasValidRoute)
        {
            return Rejected(buyRegionId, sellRegionId, typeId,
                "Keine gültige Route (leerer Pfad oder null Sprünge) — ohne Route kein Handel.");
        }

        // Kaufseite = Verkaufsaufträge (Asks) an der Kaufstation, aufsteigend;
        // Verkaufsseite = Kaufaufträge (Bids) an der Verkaufsstation, absteigend.
        var askOrders = asks.Where(o => !o.IsBuyOrder).OrderBy(o => o.Price).ToList();
        var bidOrders = bids.Where(o => o.IsBuyOrder).OrderByDescending(o => o.Price).ToList();

        if (askOrders.Count == 0 || bidOrders.Count == 0)
        {
            return Rejected(buyRegionId, sellRegionId, typeId,
                "Keine Orders an Kauf- oder Verkaufsort — kein belastbarer Kandidat.");
        }

        var bestAsk = askOrders[0];
        var bestBid = bidOrders[0];

        if (fees.BrokerFeeRate < 0 || fees.SalesTaxRate < 0
            || fees.BrokerFeeRate + fees.SalesTaxRate >= 1.0)
        {
            return Rejected(buyRegionId, sellRegionId, typeId,
                "Ungültige Gebühren-Sätze (Broker und/oder Steuer außerhalb des gültigen Bereichs).");
        }

        // History-Fenster der KAUF-Region: nur Einträge innerhalb des Fensters zählen.
        var windowStart = utcNow.AddDays(-StationTradeCandidateEngine.HistoryWindowDays);
        var window = history.Where(h => h.Date >= windowStart.Date && h.Date <= utcNow.Date).ToList();
        if (window.Count < StationTradeCandidateEngine.MinHistoryDays)
        {
            return Rejected(buyRegionId, sellRegionId, typeId,
                $"Fehlende History: nur {window.Count} von mindestens {StationTradeCandidateEngine.MinHistoryDays} Tagen im {StationTradeCandidateEngine.HistoryWindowDays}-Tage-Fenster — keine belastbare Chance.");
        }

        var avgPrice = window.Average(h => h.Average);
        var avgDailyVolume = window.Average(h => (double)h.Volume);

        // Manipulierte Top-Order (Ausreißer gegen den History-Schnitt): ein Preis
        // massiv unter/über dem 30-Tage-Schnitt ist keine echte Marktquote.
        if (bestAsk.Price < avgPrice * StationTradeCandidateEngine.OutlierAskDiscountFactor)
        {
            return Rejected(buyRegionId, sellRegionId, typeId,
                $"Verdächtige Top-Order: Best-Ask {bestAsk.Price.ToString("N2", CultureInfo.InvariantCulture)} ISK liegt unter {StationTradeCandidateEngine.OutlierAskDiscountFactor:P0} des 30-Tage-Schnitts ({avgPrice.ToString("N2", CultureInfo.InvariantCulture)} ISK) — manipulierte Top-Order, keine belastbare Chance.");
        }

        if (bestBid.Price > avgPrice * StationTradeCandidateEngine.OutlierBidPremiumFactor)
        {
            return Rejected(buyRegionId, sellRegionId, typeId,
                $"Verdächtige Top-Order: Best-Bid {bestBid.Price.ToString("N2", CultureInfo.InvariantCulture)} ISK liegt über dem {StationTradeCandidateEngine.OutlierBidPremiumFactor:F0}-fachen des 30-Tage-Schnitts ({avgPrice.ToString("N2", CultureInfo.InvariantCulture)} ISK) — manipulierte Top-Order, keine belastbare Chance.");
        }

        // Kumulative Tiefe innerhalb der Preistoleranz um den Top-Preis — nur an
        // der jeweils genannten Station (identische Begründung wie StationTrade):
        // Die Empfehlung nennt genau ein Kauf-/Verkaufsstationspaar; Orders
        // anderer Stationen sind nicht substituierbar.
        var askOrdersAtBuyStation = askOrders.Where(o => o.LocationId == bestAsk.LocationId).ToList();
        var bidOrdersAtSellStation = bidOrders.Where(o => o.LocationId == bestBid.LocationId).ToList();

        if (askOrdersAtBuyStation.Count == 0 || bidOrdersAtSellStation.Count == 0)
        {
            return Rejected(buyRegionId, sellRegionId, typeId,
                "Keine Orders an der gewählten Kauf- oder Verkaufsstation — kein belastbarer Kandidat.");
        }

        var askCeiling = bestAsk.Price * (1.0 + StationTradeCandidateEngine.DepthPriceTolerance);
        var bidFloor = bestBid.Price * (1.0 - StationTradeCandidateEngine.DepthPriceTolerance);
        var cumAskDepth = askOrdersAtBuyStation.Where(o => o.Price <= askCeiling).Sum(o => Math.Max(0, o.VolumeRemain));
        var cumBidDepth = bidOrdersAtSellStation.Where(o => o.Price >= bidFloor).Sum(o => Math.Max(0, o.VolumeRemain));
        var depthQuantity = (int)Math.Min(cumAskDepth, cumBidDepth);

        // Gebühren (offizielle EVE-Formel, identisch zu FeeCalculatorService):
        // Brokergebühr auf Kauf UND Verkauf, Sales Tax nur auf Verkauf.
        var buyCostPerUnit = bestAsk.Price * (1.0 + fees.BrokerFeeRate);
        var sellNetPerUnit = bestBid.Price * (1.0 - fees.BrokerFeeRate - fees.SalesTaxRate);

        // Menge erfüllt Cargo, Kapital und Tiefe GLEICHZEITIG: Minimum der drei
        // Grenzen. Kapitalgrenze = ganzzahlige Einheiten, die das Kapital deckt.
        var capitalLimit = assumptions.MaxCapitalIsk > 0
            ? (int)Math.Floor(assumptions.MaxCapitalIsk / buyCostPerUnit)
            : 0;
        var cargoLimit = assumptions.CargoUnits;
        var quantity = Math.Min(depthQuantity, Math.Min(cargoLimit, capitalLimit));

        if (quantity < StationTradeCandidateEngine.MinExecutableQuantity)
        {
            return Rejected(buyRegionId, sellRegionId, typeId,
                $"Menge erfüllt Cargo/Kapital/Tiefe nicht gleichzeitig: Tiefe {depthQuantity:N0}, Cargo {cargoLimit:N0}, Kapital {capitalLimit:N0} → ausführbar nur {quantity:N0}, Minimum {StationTradeCandidateEngine.MinExecutableQuantity:N0} — keine belastbare Chance.");
        }

        // Historische Mengen begrenzen die Belastbarkeit (identische Regel wie StationTrade).
        if (avgDailyVolume > 0 && quantity > avgDailyVolume * StationTradeCandidateEngine.MaxShareOfDailyVolume)
        {
            return Rejected(buyRegionId, sellRegionId, typeId,
                $"Illiquide Tiefe: {quantity:N0} Einheiten übersteigen das {StationTradeCandidateEngine.MaxShareOfDailyVolume:F0}-fache des historischen Tagesvolumens ({avgDailyVolume:N0}) — keine belastbare Chance.");
        }

        // Netto-Spanne: Mit Transportkosten (konservativ, untere Grenze) und ohne
        // (obere Grenze, reiner Handelsgewinn). Risiko-Enrichment bleibt außen vor.
        var netPerUnitExclTransport = sellNetPerUnit - buyCostPerUnit;
        var netPerUnit = netPerUnitExclTransport - assumptions.TransportCostPerUnit;

        if (netPerUnit <= 0)
        {
            return Rejected(buyRegionId, sellRegionId, typeId,
                $"Negative Nettomarge nach Gebühren und Transport: Kauf {buyCostPerUnit.ToString("N2", CultureInfo.InvariantCulture)} ISK/Stück (inkl. Broker {fees.BrokerFeeRate:P1}), Verkauf netto {sellNetPerUnit.ToString("N2", CultureInfo.InvariantCulture)} ISK/Stück (Broker + Steuer {fees.SalesTaxRate:P1}), Transportannahme {assumptions.TransportCostPerUnit.ToString("N2", CultureInfo.InvariantCulture)} ISK/Stück — kein belastbarer Kandidat.");
        }

        var netProfit = netPerUnit * quantity;
        var netProfitExclTransport = netPerUnitExclTransport * quantity;
        var requiredCapital = bestAsk.Price * quantity;
        var buyCostNet = buyCostPerUnit * quantity;
        var roi = buyCostNet > 0 ? (netProfit / buyCostNet) * 100 : 0;
        var breakEven = (buyCostPerUnit + assumptions.TransportCostPerUnit)
            / Math.Max(double.Epsilon, 1.0 - fees.BrokerFeeRate - fees.SalesTaxRate);

        var topAskAgeHours = (utcNow - bestAsk.Issued).TotalHours;
        var topBidAgeHours = (utcNow - bestBid.Issued).TotalHours;

        var transportMinutes = route.Jumps * Math.Max(0, assumptions.MinutesPerJump);

        // Risiko-Enrichment (#73): nur sichtbar, nie Teil der Rechnung.
        var riskLines = BuildRiskLines(risk);

        var evidence =
            $"Route-Trade (Region {buyRegionId} → Region {sellRegionId}): Kauf bei {bestAsk.LocationId} ({bestAsk.Price.ToString("N2", CultureInfo.InvariantCulture)} ISK/Stück, Ask-Tiefe an dieser Station {cumAskDepth:N0}, Top-Order {topAskAgeHours:F0} h alt) — " +
            $"Verkauf bei {bestBid.LocationId} ({bestBid.Price.ToString("N2", CultureInfo.InvariantCulture)} ISK/Stück, Bid-Tiefe an dieser Station {cumBidDepth:N0}, Top-Order {topBidAgeHours:F0} h alt). " +
            $"Route: {route.Jumps} Sprünge (Highsec {route.HighSecJumps}, Lowsec {route.LowSecJumps}, Nullsec {route.NullSecJumps}), {transportMinutes:F0} Min Transportzeit ({assumptions.MinutesPerJump:F0} Min/Sprung). " +
            $"History (Kaufregion): {window.Count} Tage, Schnitt {avgPrice.ToString("N2", CultureInfo.InvariantCulture)} ISK, {avgDailyVolume:N0} Stück/Tag. " +
            $"Ausführbar: {quantity:N0} Stück (Min(Cargo, Kapital, Tiefen): Cargo {cargoLimit:N0}, Kapital {capitalLimit:N0}, Tiefen {depthQuantity:N0}). " +
            $"Netto {netProfit:N0} ISK mit Transport ({netProfitExclTransport:N0} ISK ohne Transport), ROI {roi:F1}%, Kapital {requiredCapital:N0} ISK, Break-even {breakEven.ToString("N2", CultureInfo.InvariantCulture)} ISK. " +
            $"Gebühren: Broker {fees.BrokerFeeRate:P1}, Steuer {fees.SalesTaxRate:P1}, Transportannahme {assumptions.TransportCostPerUnit.ToString("N2", CultureInfo.InvariantCulture)} ISK/Stück.";

        return new RouteTradeCandidateResult(
            IsActionable: true,
            NotActionableReason: null,
            AlgorithmVersion,
            Quantity: quantity,
            CargoLimitQuantity: cargoLimit,
            CapitalLimitQuantity: capitalLimit,
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
            JumpCount: route.Jumps,
            TransportMinutes: transportMinutes,
            NetProfit: netProfit,
            NetProfitExclTransport: netProfitExclTransport,
            RequiredCapital: requiredCapital,
            RoiPercent: roi,
            TopAskAgeHours: topAskAgeHours,
            TopBidAgeHours: topBidAgeHours,
            RouteRiskLevel: risk?.RouteLevel,
            RiskLines: riskLines,
            Evidence: evidence);
    }

    /// <summary>
    /// Risiko-Enrichment als deutsche Anzeigezeilen (#73). Fehlt die Evidenz oder
    /// ist eine Quelle (z. B. zKillboard) nicht verfügbar, wird das ausgewiesen —
    /// die Netto-Rechnung bleibt davon unberührt (Akzeptanzkriterium 2).
    /// </summary>
    internal static IReadOnlyList<string> BuildRiskLines(RouteRiskSummary? risk)
    {
        if (risk == null)
        {
            return Array.Empty<string>();
        }

        var lines = new List<string>(3)
        {
            $"Routen-Risiko: {RiskLevelLabel(risk.RouteLevel)} (konservativ — unbekannt gilt als nicht sicher)."
        };

        foreach (var source in risk.UnavailableSources)
        {
            lines.Add(source == RiskEvidenceSource.Zkillboard
                ? "zKillboard nicht verfügbar — Verlustnachweis fehlt, Risiko nur ESI-Evidenz (Route unbekannt)."
                : "ESI-Aktivitätsdaten nicht verfügbar — Routen-Risiko unvollständig.");
        }

        return lines;
    }

    internal static string RiskLevelLabel(RiskLevel level) => level switch
    {
        RiskLevel.Low => "niedrig",
        RiskLevel.Elevated => "erhöht",
        RiskLevel.High => "hoch",
        _ => "unbekannt"
    };

    private static RouteTradeCandidateResult Rejected(int buyRegionId, int sellRegionId, int typeId, string reason)
        => new(
            IsActionable: false,
            NotActionableReason: reason,
            AlgorithmVersion,
            Quantity: null,
            CargoLimitQuantity: null,
            CapitalLimitQuantity: null,
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
            JumpCount: null,
            TransportMinutes: null,
            NetProfit: null,
            NetProfitExclTransport: null,
            RequiredCapital: null,
            RoiPercent: null,
            TopAskAgeHours: null,
            TopBidAgeHours: null,
            RouteRiskLevel: null,
            RiskLines: Array.Empty<string>(),
            Evidence: reason);
}