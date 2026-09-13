using WALLEve.Models.Stockpiles;

namespace WALLEve.Services.Stockpiles;

/// <summary>
/// Reine, deterministische Bestandsberechnung für Stockpile-Ziele (Issue #43).
/// Keine Datenbank-, ESI- oder SDE-Zugriffe — nur die übergebenen Rohdaten,
/// damit Regressionstests ohne Live-Abhängigkeiten auskommen.
///
/// Kerngarantien:
/// - Shortage/Surplus werden NUR gegen den physischen Bestand abgeleitet;
///   Orders werden nie still addiert/subtrahiert (Kriterium 1: Escrow-Sell-Orders
///   sind keine Assets — ein Abzug vom physischen Bestand wäre Doppel-Diskont).
/// - Der Ort-/Container-Scope greift über die Parent-Kette: Ein Item zählt für
///   eine Ziel-Location, wenn seine Location-Kette die Ziel-Location enthält
///   (Kriterium 2). Jede Asset-Zeile wird dabei genau EINMAL gezählt (Kriterium 3).
/// - Fehlende oder unvollständige Eingabequellen markieren die Zeile als partial
///   und blockieren die Ableitung statt falscher Nullstände (Kriterium 2).
/// - Owner-Dimension liegt außerhalb: Assets/Orders gelten bereits als owner-scoped.
/// </summary>
public static class StockpileCalculator
{
    /// <summary>Eine Asset-Rohzeile (aus HoldingItem abgeleitet).</summary>
    public readonly record struct AssetLine(long ItemId, int TypeId, long LocationId, int Quantity);

    /// <summary>Eine aktive Market-Order mit dem noch offenen Volumen.</summary>
    public readonly record struct OrderLine(int TypeId, long LocationId, bool IsBuyOrder, int VolumeRemain);

    /// <summary>
    /// Berechnet die Bestandsanteile für alle Ziele.
    /// </summary>
    /// <param name="targets">Zu berechnende Ziele (Owner- und Archiv-Filter liegen beim Aufrufer).</param>
    /// <param name="assets">Asset-Zeilen des Owners (Rohdaten, inkl. Container-Membership über ItemId).</param>
    /// <param name="orders">Aktive Orders mit offenem Volumen.</param>
    /// <param name="physicalSourceAvailable">false = kein abgeschlossener Bestands-Snapshot vorhanden.</param>
    /// <param name="ordersSourceAvailable">false = Order-Bestand nicht vollständig/aktuell verfügbar.</param>
    public static IReadOnlyList<StockpileCalculationLine> Calculate(
        IReadOnlyList<StockpileTarget> targets,
        IReadOnlyList<AssetLine> assets,
        IReadOnlyList<OrderLine> orders,
        bool physicalSourceAvailable = true,
        bool ordersSourceAvailable = true)
    {
        var lines = new List<StockpileCalculationLine>(targets.Count);

        // Container-Membership: ItemId → eigene LocationId. ItemIds sind nicht
        // global eindeutig (#35/#50): erste Zeile gewinnt deterministisch,
        // für die Kettenauflösung zählt nur die Mitgliedschaft.
        var parentOf = new Dictionary<long, long>();
        foreach (var asset in assets)
        {
            parentOf.TryAdd(asset.ItemId, asset.LocationId);
        }

        static IReadOnlyList<long> Chain(long startLocationId, IReadOnlyDictionary<long, long> parentOf)
        {
            var chain = new List<long>();
            var visited = new HashSet<long>();
            var node = startLocationId;
            while (visited.Add(node))
            {
                chain.Add(node);
                if (!parentOf.TryGetValue(node, out var next))
                {
                    break;
                }
                node = next;
            }
            return chain;
        }

        // Chain-Auflösung pro LocationId einmalig (Ketten hängen nur an der LocationId).
        var chainByLocation = new Dictionary<long, IReadOnlyList<long>>();
        foreach (var asset in assets)
        {
            if (!chainByLocation.ContainsKey(asset.LocationId))
            {
                chainByLocation[asset.LocationId] = Chain(asset.LocationId, parentOf);
            }
        }

        foreach (var target in targets)
        {
            string? partialReason = null;

            // Ziel-Typzeilen filtern; Ort-/Container-Scope: Kette muss die Ziel-Location enthalten.
            IEnumerable<AssetLine> scoped;
            if (target.LocationId is { } scopeId)
            {
                scoped = assets
                    .Where(a => a.TypeId == target.TypeId && chainByLocation[a.LocationId].Contains(scopeId));
            }
            else
            {
                scoped = assets.Where(a => a.TypeId == target.TypeId);
            }

            // Abdeckung: Der Scope muss nachweislich im Snapshot enthalten sein, sonst
            // kann 0 nicht von "nicht synchronisiert" unterschieden werden → blockieren.
            bool covered = physicalSourceAvailable;
            if (covered && target.LocationId is { } coverScopeId)
            {
                covered = assets.Any(a => chainByLocation[a.LocationId].Contains(coverScopeId));
                if (!covered)
                {
                    partialReason ??= "scope-not-covered";
                }
            }
            else if (!physicalSourceAvailable)
            {
                partialReason ??= "physical-source-missing";
            }

            var physical = covered ? scoped.Sum(a => a.Quantity) : (int?)null;

            // Orders: Buy → Inbound (eingehend), Sell → Bound (gebunden). Scope = Order-Location.
            IReadOnlyList<OrderLine> typeOrders = orders.Where(o => o.TypeId == target.TypeId).ToList();
            if (target.LocationId is { } orderScope)
            {
                typeOrders = typeOrders.Where(o => o.LocationId == orderScope).ToList();
            }

            int? inbound = null;
            int? bound = null;
            if (ordersSourceAvailable)
            {
                inbound = typeOrders.Where(o => o.IsBuyOrder).Sum(o => o.VolumeRemain);
                bound = typeOrders.Where(o => !o.IsBuyOrder).Sum(o => o.VolumeRemain);
            }
            else
            {
                partialReason ??= "orders-source-missing";
            }

            int? shortage = physical is { } physQuantity ? Math.Max(0, target.Quantity - physQuantity) : null;
            int? surplus = physical is { } physStock ? Math.Max(0, physStock - target.Quantity) : null;

            lines.Add(new StockpileCalculationLine
            {
                TargetId = target.Id,
                TypeId = target.TypeId,
                LocationId = target.LocationId,
                TargetQuantity = target.Quantity,
                IsArchived = target.IsArchived,
                Note = target.Note,
                Physical = physical,
                Inbound = inbound,
                Bound = bound,
                Shortage = shortage,
                Surplus = surplus,
                IsPartial = !covered || !ordersSourceAvailable,
                PartialReason = partialReason
            });
        }

        return lines;
    }
}