using System.Globalization;
using System.Text.RegularExpressions;
using WALLEve.Models.Database;
using WALLEve.Models.Risk;
using WALLEve.Models.Trading;
using WALLEve.Services.Risk.Interfaces;
using WALLEve.Services.Trading.Interfaces;

namespace WALLEve.Services.Trading;

/// <summary>
/// Gemeinsame Karten-Engine für Trading-Empfehlungen (Issue #66). Sie fasst die
/// Schlüsselfakten (Was/Wo/Menge/Preis/Netto/Annahmen) einer Empfehlung zu einer
/// unveränderlichen <see cref="TradingActionCard"/> zusammen. Reine Funktionen,
/// deterministisch testbar; die Analyse selbst benötigt keinen EVE-UI-Scope und
/// keinen Client — Hilfsaktionen (Kopieren/Marktdetails/Wegpunkt) sind ein
/// Seitenkanal der UI und verändern die Karte beziehungsweise Empfehlung nie.
/// </summary>
public sealed class TradingActionCardService : ITradingActionCardService
{
    /// <summary>
    /// Menge in der Evidenz von Bestands-Verkaufsempfehlungen:
    /// "Ortsgebunden (Ort): 1.234 von 5.000 Einheiten — …" bzw. "…: 50 × TypeName — …".
    /// Bewusst konservativ: nur exakt diese beiden dokumentierten Muster; alles
    /// andere bleibt unbekannt ("—"), es wird keine Menge erfunden.
    /// </summary>
    internal static readonly Regex QuantityByTotalPattern = new(
        @":\s*(?<qty>\d{1,3}(?:[.,]\d{3})*|\d+)\s+von\s+(?<total>\d{1,3}(?:[.,]\d{3})*|\d+)\s+Einheiten",
        RegexOptions.Compiled);

    internal static readonly Regex QuantityTimesPattern = new(
        @":\s*(?<qty>\d{1,3}(?:[.,]\d{3})*|\d+)\s+×\s+",
        RegexOptions.Compiled);

    /// <summary>
    /// Ausführbare Menge in der Evidenz von Station-Trade-Empfehlungen (Issue #71):
    /// "Ausführbar: 1.234 Stück (Min(Tiefen))". Gleiche Konvention wie oben:
    /// nur exakt dieses Muster, alles andere bleibt unbekannt ("—").
    /// Route-Trade-Empfehlungen (Issue #74) nutzen dasselbe Muster mit
    /// "Ausführbar: 1.234 Stück (Min(Cargo, Kapital, Tiefen): …)".
    /// </summary>
    internal static readonly Regex QuantityExecutablePattern = new(
        @"Ausführbar:\s*(?<qty>\d{1,3}(?:[.,]\d{3})*|\d+)\s+Stück",
        RegexOptions.Compiled);

    /// <summary>
    /// Dokumentiertes Route-Fragment der RouteTrade-Evidenz (Issue #74):
    /// "Route: 7 Sprünge (Highsec 3, Lowsec 3, Nullsec 1)".
    /// </summary>
    internal static readonly Regex RouteEvidencePattern = new(
        @"Route:\s*\d+\s+Sprünge\s*\([^)]*\)",
        RegexOptions.Compiled);

    /// <summary>
    /// Dokumentierte Transportzeitannahme der RouteTrade-Evidenz:
    /// "14 Min Transportzeit (2 Min/Sprung)".
    /// </summary>
    internal static readonly Regex TransportTimePattern = new(
        @"\d+\s+Min\s+Transportzeit\s*\(\d+\s+Min/Sprung\)",
        RegexOptions.Compiled);

    /// <summary>
    /// Dokumentierte Transportkostenannahme der RouteTrade-Evidenz:
    /// "Transportannahme 0,20 ISK/Stück".
    /// </summary>
    internal static readonly Regex TransportCostPattern = new(
        @"Transportannahme\s+[\d.,]+\s+ISK/Stück",
        RegexOptions.Compiled);

    public IReadOnlyList<TradingActionCardModel> BuildCards(
        IReadOnlyList<TradingOpportunity> opportunities,
        IReadOnlyDictionary<int, string> typeNames,
        IReadOnlyDictionary<long, string> locationNames)
        => BuildCards(opportunities, typeNames, locationNames, null);

    /// <summary>
    /// Baut Karten und reichert sie optional mit beobachtetem Routen-Risiko an
    /// (Issue #74): <paramref name="riskByOpportunityId"/> liefert je Opportunity
    /// die Risiko-Zusammenfassung des <see cref="RouteRiskService"/> (#73) als
    /// reines Enrichment — Menge, Preise und Netto bleiben unverändert, nur die
    /// Risiko-Anzeige wird ergänzt. Reine Funktion, deterministisch testbar.
    /// </summary>
    public IReadOnlyList<TradingActionCardModel> BuildCards(
        IReadOnlyList<TradingOpportunity> opportunities,
        IReadOnlyDictionary<int, string> typeNames,
        IReadOnlyDictionary<long, string> locationNames,
        IReadOnlyDictionary<int, RouteRiskSummary>? riskByOpportunityId)
    {
        ArgumentNullException.ThrowIfNull(opportunities);

        var cards = new List<TradingActionCardModel>(opportunities.Count);
        foreach (var opp in opportunities)
        {
            RouteRiskSummary? risk = null;
            riskByOpportunityId?.TryGetValue(opp.Id, out risk);
            cards.Add(BuildCard(opp, typeNames, locationNames, risk));
        }

        return cards;
    }

    /// <summary>
    /// Erhebt je Route-Trade-Opportunity die Risiko-Zusammenfassung der Endpunkt-Systeme
    /// (RouteRiskService, #73) als reines Enrichment (#74). Gleiche Endpunkt-Paare
    /// (richtungslos, z. B. Jita→Amarr und Amarr→Jita) teilen dieselbe Evidenz und werden
    /// nur einmal erhoben. Opportunities ohne beide Endpunkt-Systeme oder mit Fehlschlag
    /// der Erhebung erzeugen keinen Eintrag — die Karten gleichen dann exakt denen ohne
    /// Enrichment (unverändertes Verhalten, Akzeptanzkriterium 2). Der Risikodienst wirft
    /// vertragsgemäß nie; defensiv wird trotzdem abgefangen, damit die Karten nie blockieren.
    /// </summary>
    public async Task<IReadOnlyDictionary<int, RouteRiskSummary>> CollectRiskByOpportunityAsync(
        IReadOnlyList<TradingOpportunity> opportunities,
        IRouteRiskService riskService,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(opportunities);
        ArgumentNullException.ThrowIfNull(riskService);

        var cache = new Dictionary<(int, int), RouteRiskSummary>();
        var failedPairs = new HashSet<(int, int)>();
        var result = new Dictionary<int, RouteRiskSummary>();

        foreach (var opp in opportunities)
        {
            if (opp.OpportunityType != "route_trade")
            {
                continue;
            }

            var buySystemId = opp.BuySystemId;
            var sellSystemId = opp.SellSystemId;
            if (!buySystemId.HasValue || !sellSystemId.HasValue || buySystemId.Value == sellSystemId.Value)
            {
                continue;
            }

            var key = buySystemId.Value < sellSystemId.Value
                ? (buySystemId.Value, sellSystemId.Value)
                : (sellSystemId.Value, buySystemId.Value);

            if (!cache.TryGetValue(key, out var summary) && !failedPairs.Contains(key))
            {
                try
                {
                    summary = await riskService.CollectRouteRiskAsync(
                        new[] { key.Item1, key.Item2 },
                        ct);
                    cache[key] = summary;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                    // Enrichment darf die Karten nie blockieren: fehlende Evidenz
                    // bedeutet schlicht keine Risiko-Anzeige.
                    failedPairs.Add(key);
                    summary = null;
                }
            }

            if (summary != null)
            {
                result[opp.Id] = summary;
            }
        }

        return result;
    }

    private static TradingActionCardModel BuildCard(
        TradingOpportunity opp,
        IReadOnlyDictionary<int, string> typeNames,
        IReadOnlyDictionary<long, string> locationNames,
        RouteRiskSummary? risk)
    {
        var typeName = typeNames.TryGetValue(opp.TypeId, out var tn) ? tn : $"Type {opp.TypeId}";
        var locationId = opp.BuyLocationId ?? opp.SellLocationId;
        var locationLabel = locationId.HasValue
            ? (locationNames.TryGetValue(locationId.Value, out var ln) ? ln : $"Location {locationId.Value}")
            : null;

        var quantity = TryParseQuantity(opp);
        var evidenceLines = SplitLines(opp.Evidence);
        var assumptions = BuildAssumptions(opp);
        var hasActual = opp.ActualProfit.HasValue;

        return new TradingActionCardModel
        {
            OpportunityId = opp.Id,
            CharacterId = opp.CharacterId,
            OpportunityType = opp.OpportunityType,
            TypeName = typeName,
            TypeId = opp.TypeId,
            LocationLabel = locationLabel,
            LocationId = locationId,
            Quantity = quantity,
            BuyPrice = opp.BuyPrice,
            SellPrice = opp.SellPrice,
            EstimatedProfit = opp.EstimatedProfit,
            NetValue = hasActual ? opp.ActualProfit!.Value : opp.EstimatedProfit,
            HasActualResult = hasActual,
            ActualProfit = opp.ActualProfit,
            Score = opp.Score,
            AlgorithmVersion = opp.AlgorithmVersion,
            Provenance = opp.Provenance,
            IsLegacyProvenance = opp.Provenance == TradingOpportunity.ProvenanceLegacy,
            DataQuality = opp.DataQuality,
            BrokerFeeRate = opp.BrokerFeeRate,
            SalesTaxRate = opp.SalesTaxRate,
            BrokerFeeOrigin = opp.BrokerFeeOrigin,
            SalesTaxOrigin = opp.SalesTaxOrigin,
            DetectedAt = opp.DetectedAt,
            ExpiresAt = opp.ExpiresAt,
            Status = opp.Status,
            IsTerminalStatus = IsTerminal(opp.Status),
            Assumptions = assumptions,
            EvidenceLines = evidenceLines,
            CopyPricePayload = BuildPricePayload(opp),
            CopyQuantityPayload = quantity.HasValue ? quantity.Value.ToString(CultureInfo.InvariantCulture) : null,
            RiskLevel = risk?.RouteLevel,
            RiskLines = RouteTradeCandidateEngine.BuildRiskLines(risk),
            CostRouteAssumptions = BuildCostRouteAssumptions(opp)
        };
    }

    /// <summary>
    /// Menge aus der Evidenz ableiten (siehe Muster im Regex-Kommentar):
    /// Bestandsverkauf über die beiden dokumentierten Ortsmuster, Station-Trade
    /// über das dokumentierte "Ausführbar: N Stück"-Muster (Issue #71).
    /// Zahlen mit Tausenderpunkt/-komma werden ohne Kultur-Abhängigkeit normalisiert.
    /// Öffentlich, weil die Ableitung ein dokumentierter Teil des Kartenvertrags ist
    /// und die Regressionstests sie direkt prüfen.
    /// </summary>
    public static int? TryParseQuantity(TradingOpportunity opp)
    {
        if (string.IsNullOrWhiteSpace(opp.Evidence))
            return null;

        var patterns = opp.OpportunityType switch
        {
            "inventory_sell" => new[] { QuantityByTotalPattern, QuantityTimesPattern },
            "station_trading" => new[] { QuantityExecutablePattern },
            "route_trade" => new[] { QuantityExecutablePattern },
            _ => Array.Empty<Regex>()
        };

        foreach (var pattern in patterns)
        {
            var match = pattern.Match(opp.Evidence);
            if (match.Success && TryParsePlainNumber(match.Groups["qty"].Value, out var qty) && qty > 0)
                return qty;
        }

        return null;
    }

    /// <summary>
    /// „Preis kopieren": der für die Aktion relevante Preis (Verkaufspreis, sonst
    /// Kaufpreis), invariant ohne Währungssymbol. <c>null</c> = kein belegbarer Preis.
    /// </summary>
    internal static string? BuildPricePayload(TradingOpportunity opp)
    {
        var price = opp.SellPrice ?? opp.BuyPrice;
        return price.HasValue
            ? price.Value.ToString("0.00", CultureInfo.InvariantCulture)
            : null;
    }

    /// <summary>
    /// Annahmen der Rechnung — ehrlich aus persistierten Feldern abgeleitet
    /// (Datenqualität, Gebührenherkunft #46), nie erfunden.
    /// </summary>
    internal static IReadOnlyList<string> BuildAssumptions(TradingOpportunity opp)
    {
        var assumptions = new List<string>(4);

        switch (opp.DataQuality)
        {
            case "complete":
                assumptions.Add("Cost-Basis gesichert (Transaktion oder manuelle Eingabe).");
                break;
            case "partial":
                assumptions.Add("Cost-Basis geschätzt — Gewinnangabe ist eine Schätzung.");
                break;
        }

        if (opp.BrokerFeeRate.HasValue)
            assumptions.Add($"Brokergebühr {opp.BrokerFeeRate.Value.ToString("0.00", CultureInfo.InvariantCulture)}% ({FeeOriginLabel(opp.BrokerFeeOrigin)}).");
        else if (!string.IsNullOrEmpty(opp.BrokerFeeOrigin))
            assumptions.Add($"Brokergebühr: Herkunft {FeeOriginLabel(opp.BrokerFeeOrigin)}, Satz nicht persistiert.");

        if (opp.SalesTaxRate.HasValue)
            assumptions.Add($"Verkaufssteuer {opp.SalesTaxRate.Value.ToString("0.00", CultureInfo.InvariantCulture)}% ({FeeOriginLabel(opp.SalesTaxOrigin)}).");
        else if (!string.IsNullOrEmpty(opp.SalesTaxOrigin))
            assumptions.Add($"Verkaufssteuer: Herkunft {FeeOriginLabel(opp.SalesTaxOrigin)}, Satz nicht persistiert.");

        if (assumptions.Count == 0)
            assumptions.Add("Keine expliziten Annahmen persistiert (Legacy-Datensatz).");

        return assumptions;
    }

    /// <summary>Gebührenherkunft (#46) als deutsche Anzeige.</summary>
    internal static string FeeOriginLabel(string? origin) => origin switch
    {
        "automatic" => "aus Skills berechnet",
        "manual_override" => "manueller Override",
        "estimated" => "konservative Schätzung",
        "unknown" => "unbekannt",
        _ => string.IsNullOrEmpty(origin) ? "unbekannt" : origin
    };

    /// <summary>
    /// Aufklappbare Kosten-/Routenannahmen für Route-Trade-Karten (Issue #74):
    /// Route (Sprünge + Sicherheits-Aufbruch), Transportzeitannahme und
    /// Transportkosten je Einheit — ausschließlich aus den dokumentierten
    /// Evidenz-Fragmenten der RouteTrade-Engine abgeleitet. Nicht vorhandene
    /// Fragmente bleiben weg (ehrlicher Platzhalter statt erfundener Annahmen);
    /// als Fallback dient die persistierte Sprungdistanz.
    /// </summary>
    internal static IReadOnlyList<string> BuildCostRouteAssumptions(TradingOpportunity opp)
    {
        if (opp.OpportunityType != "route_trade" || string.IsNullOrWhiteSpace(opp.Evidence))
        {
            return Array.Empty<string>();
        }

        var lines = new List<string>(3);
        var routeMatch = RouteEvidencePattern.Match(opp.Evidence);
        if (routeMatch.Success)
        {
            lines.Add(routeMatch.Value.Trim());
        }

        var timeMatch = TransportTimePattern.Match(opp.Evidence);
        if (timeMatch.Success)
        {
            lines.Add(timeMatch.Value.Trim());
        }

        var costMatch = TransportCostPattern.Match(opp.Evidence);
        if (costMatch.Success)
        {
            lines.Add(costMatch.Value.Trim());
        }

        if (lines.Count == 0 && opp.JumpDistance.HasValue)
        {
            lines.Add($"Route: {opp.JumpDistance} Sprünge (Sicherheits-Aufbruch nicht gespeichert).");
        }

        return lines;
    }

    private static IReadOnlyList<string> SplitLines(string evidence)
    {
        if (string.IsNullOrWhiteSpace(evidence))
            return Array.Empty<string>();

        return evidence
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.Length > 0)
            .ToList();
    }

    private static bool IsTerminal(string status) => status switch
    {
        RecommendationStatus.Executed => true,
        RecommendationStatus.Expired => true,
        RecommendationStatus.Invalid => true,
        _ => false
    };

    /// <summary>Normale Zahlen ohne Kulturen; Tausender-Separatoren werden entfernt.</summary>
    private static bool TryParsePlainNumber(string raw, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var normalized = raw.Replace(".", string.Empty).Replace(",", string.Empty);
        return int.TryParse(normalized, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value > 0;
    }
}