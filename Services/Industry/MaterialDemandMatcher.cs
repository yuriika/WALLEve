using WALLEve.Models.Industry;
using WALLEve.Services.Stockpiles;

namespace WALLEve.Services.Industry;

/// <summary>
/// Deterministischer Abgleich des Materialbedarfs (#56) gegen die Holdings
/// (Issue #62). Reine Funktion ohne DB-/ESI-Zugriffe, damit Regressionstests
/// ohne Live-Abhängigkeiten auskommen.
///
/// Regeln:
/// - Jedes Material hat EINEN Bestandspool über den gesamten Owner-Snapshot;
///   jede Asset-Zeile zählt genau einmal. Gleiches Material in mehreren
///   Orten/Plänen wird so nie mehrfach verfügbar behauptet (Kriterium 1).
/// - Die Fehlmenge wird einmalig je Material gegen den Pool abgeleitet
///   (max(0, Gesamtbedarf − physisch)), nie je Plan oder je Ort.
/// - Buy-/Sell-Orders bleiben getrennte Mengen (Inbound/Bound) und werden
///   nie still addiert oder zur Fehlmenge verrechnet (Kriterium 2).
/// - Fehlende oder unvollständige Quellen markieren IsPartial statt falscher
///   Nullstände; der erste Grund in Prioritätsreihenfolge gewinnt.
/// - Plans sind Planung, keine Reservierung: Der Pool wird nie abgezogen.
/// </summary>
public static class MaterialDemandMatcher
{
    /// <summary>
    /// Gleicht die Bedarfe aller Pläne gegen die Holdings ab.
    /// </summary>
    /// <param name="requests">Bedarfe je Plan (Owner-Dimension liegt außen vor).</param>
    /// <param name="assets">Asset-Zeilen des Owners (Rohdaten des Snapshot).</param>
    /// <param name="orders">Aktive Orders mit offenem Volumen.</param>
    /// <param name="physicalSourceAvailable">false = kein abgeschlossener Bestands-Snapshot.</param>
    /// <param name="ordersSourceAvailable">false = Order-Bestand nicht vollständig/aktuell.</param>
    public static IReadOnlyList<MaterialDemandMatch> Match(
        IReadOnlyList<MaterialDemandRequest> requests,
        IReadOnlyList<StockpileCalculator.AssetLine> assets,
        IReadOnlyList<StockpileCalculator.OrderLine> orders,
        bool physicalSourceAvailable = true,
        bool ordersSourceAvailable = true)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(orders);

        if (requests.Count == 0)
        {
            return Array.Empty<MaterialDemandMatch>();
        }

        // Plan-Zeilen je Material: Ein Material kann in mehreren Plänen
        // vorkommen; der Pool wird trotzdem nur EINMAL gebildet.
        var planRows = new Dictionary<int, List<MaterialDemandPlanRow>>();
        foreach (var request in requests)
        {
            foreach (var material in request.Materials)
            {
                if (!planRows.TryGetValue(material.MaterialTypeId, out var rows))
                {
                    rows = planRows[material.MaterialTypeId] = new List<MaterialDemandPlanRow>();
                }
                rows.Add(new MaterialDemandPlanRow(request.PlanName, material.RequiredQuantity));
            }
        }

        // Deterministische Reihenfolge: größter Gesamtbedarf zuerst, dann TypeId.
        var typeIds = planRows.Keys
            .OrderByDescending(t => planRows[t].Sum(p => p.RequiredQuantity))
            .ThenBy(t => t)
            .ToList();

        var matches = new List<MaterialDemandMatch>(typeIds.Count);
        foreach (var typeId in typeIds)
        {
            var planned = planRows[typeId].Sum(p => p.RequiredQuantity);

            long? physical = null;
            string? partialReason = null;
            if (physicalSourceAvailable)
            {
                // Pool = Summe über GESAMTEN Snapshot; jede Asset-Zeile genau einmal.
                physical = assets.Where(a => a.TypeId == typeId).Sum(a => (long)a.Quantity);
            }
            else
            {
                partialReason = "physical-source-missing";
            }

            long? inbound = null;
            long? bound = null;
            if (ordersSourceAvailable)
            {
                inbound = orders.Where(o => o.TypeId == typeId && o.IsBuyOrder).Sum(o => (long)o.VolumeRemain);
                bound = orders.Where(o => o.TypeId == typeId && !o.IsBuyOrder).Sum(o => (long)o.VolumeRemain);
            }
            else
            {
                partialReason ??= "orders-source-missing";
            }

            long? shortage = physical is { } pool ? Math.Max(0, planned - pool) : null;

            matches.Add(new MaterialDemandMatch
            {
                MaterialTypeId = typeId,
                Plans = planRows[typeId].OrderBy(p => p.PlanName).ToList(),
                PhysicalAvailable = physical,
                Inbound = inbound,
                Bound = bound,
                Shortage = shortage,
                IsPartial = physical is null || !ordersSourceAvailable,
                PartialReason = partialReason
            });
        }

        return matches;
    }
}