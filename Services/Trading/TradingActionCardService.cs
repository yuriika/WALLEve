using System.Globalization;
using System.Text.RegularExpressions;
using WALLEve.Models.Database;
using WALLEve.Models.Trading;
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
    /// </summary>
    internal static readonly Regex QuantityExecutablePattern = new(
        @"Ausführbar:\s*(?<qty>\d{1,3}(?:[.,]\d{3})*|\d+)\s+Stück",
        RegexOptions.Compiled);

    public IReadOnlyList<TradingActionCardModel> BuildCards(
        IReadOnlyList<TradingOpportunity> opportunities,
        IReadOnlyDictionary<int, string> typeNames,
        IReadOnlyDictionary<long, string> locationNames)
    {
        ArgumentNullException.ThrowIfNull(opportunities);

        var cards = new List<TradingActionCardModel>(opportunities.Count);
        foreach (var opp in opportunities)
        {
            cards.Add(BuildCard(opp, typeNames, locationNames));
        }

        return cards;
    }

    private static TradingActionCardModel BuildCard(
        TradingOpportunity opp,
        IReadOnlyDictionary<int, string> typeNames,
        IReadOnlyDictionary<long, string> locationNames)
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
            CopyQuantityPayload = quantity.HasValue ? quantity.Value.ToString(CultureInfo.InvariantCulture) : null
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