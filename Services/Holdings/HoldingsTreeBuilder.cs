using WALLEve.Models.Holdings;

namespace WALLEve.Services.Holdings;

/// <summary>
/// Reiner, deterministischer Aggregat-/Ortsbaum-Aufbau (#57). Verarbeitet die
/// bereits aufgelösten Items eines Snapshots und baut daraus:
///  - Type-Projektion (je Owner+TypeId: Menge, Zeilen, Qualität),
///  - Type/Location-Projektion (je Owner+TypeId+direkte Location),
///  - Ortsbaum (Anker → Container → Roh-Items) mit separatem „Unbekannte Orte“-Bucket.
/// Keine Datenbank-, ESI- oder SDE-Zugriffe — nur die übergebenen Daten, daher
/// direkt in Unit-Tests prüfbar. Mengengleichheit und Owner-Trennung sind
/// Invarianten: Die Summe aller Baumknoten (inkl. Unbekannt) ist exakt die
/// Roh-Summe, und der Aufbau kennt nur die Items EINES Owners.
/// </summary>
public static class HoldingsTreeBuilder
{
    /// <summary>LocationId des Pseudo-Buckets für Items mit unaufgelöstem Anker.</summary>
    public const long UnknownBucketLocationId = -1L;

    /// <summary>Freshness-Schwelle: Snapshot höchstens so alt gilt als frisch.</summary>
    public static readonly TimeSpan FreshnessThreshold = TimeSpan.FromHours(24);

    public static HoldingsTreeResult Build(
        OwnerType ownerType,
        int ownerId,
        long snapshotId,
        DateTime syncedAt,
        DateTime now,
        IReadOnlyList<ResolvedHoldingItem> resolvedItems,
        IReadOnlyDictionary<int, string> typeNames,
        IReadOnlyDictionary<long, HoldingItem> itemsById)
    {
        var nodes = new Dictionary<long, HoldingsLocationNode>();
        var leaves = new Dictionary<long, List<HoldingsItemLeaf>>();
        var parents = new Dictionary<long, long>();
        var rootIds = new List<long>();
        var unresolvedRootIds = new List<long>();

        foreach (var resolved in resolvedItems)
        {
            var chain = resolved.Chain;
            if (chain.Count == 0)
                continue;

            // Knoten je Ketten-Element anlegen (Metadaten vom ersten Vorkommen).
            for (var i = 0; i < chain.Count; i++)
            {
                var element = chain[i];
                if (!nodes.ContainsKey(element.LocationId))
                {
                    nodes[element.LocationId] = new HoldingsLocationNode
                    {
                        LocationId = element.LocationId,
                        Kind = element.Kind,
                        Name = element.Name,
                        SolarSystemName = element.SolarSystemName,
                        RegionName = element.RegionName,
                        IsResolved = element.IsResolved,
                        UnresolvedReason = element.UnresolvedReason
                    };
                }
            }

            // Eltern-Beziehungen (deterministisch: erste Zuordnung gewinnt).
            for (var i = 0; i < chain.Count - 1; i++)
            {
                var child = chain[i].LocationId;
                var parent = chain[i + 1].LocationId;
                if (!parents.ContainsKey(child))
                    parents[child] = parent;
            }

            // Roh-Item (Drill-down) am tiefsten Ketten-Knoten.
            var directId = chain[0].LocationId;
            if (!leaves.TryGetValue(directId, out var nodeLeaves))
            {
                nodeLeaves = new List<HoldingsItemLeaf>();
                leaves[directId] = nodeLeaves;
            }
            nodeLeaves.Add(new HoldingsItemLeaf
            {
                ItemId = resolved.Item.ItemId,
                TypeId = resolved.Item.TypeId,
                TypeName = typeNames.TryGetValue(resolved.Item.TypeId, out var typeName) ? typeName : null,
                Quantity = resolved.Item.Quantity,
                IsSingleton = resolved.Item.IsSingleton,
                LocationFlag = resolved.Item.LocationFlag
            });

            // Letztes Ketten-Element: aufgelöster Anker → Baumwurzel, sonst Unbekannt-Bucket.
            var last = chain[chain.Count - 1];
            if (last.IsResolved)
                rootIds.Add(last.LocationId);
            else
                unresolvedRootIds.Add(last.LocationId);
        }

        // Container-Metadaten (Typname + Flag aus der Container-Rohzeile) nachtragen.
        foreach (var (locationId, node) in nodes)
        {
            if (node.Kind != LocationKind.Container)
                continue;
            if (!itemsById.TryGetValue(locationId, out var containerRow))
                continue;
            var containerName = typeNames.TryGetValue(containerRow.TypeId, out var typeName) ? typeName : null;
            nodes[locationId] = new HoldingsLocationNode
            {
                LocationId = node.LocationId,
                Kind = node.Kind,
                Name = containerName ?? node.Name,
                SolarSystemName = node.SolarSystemName,
                RegionName = node.RegionName,
                IsResolved = node.IsResolved,
                UnresolvedReason = node.UnresolvedReason,
                LocationFlag = containerRow.LocationFlag
            };
        }

        // Kinder je Eltern-Knoten.
        var children = new Dictionary<long, List<long>>();
        foreach (var (child, parent) in parents)
        {
            if (!children.TryGetValue(parent, out var list))
            {
                list = new List<long>();
                children[parent] = list;
            }
            list.Add(child);
        }

        // Post-Order-Mengen: zuerst Leaves + Kinder-Quantitäten hochrechnen.
        var quantities = new Dictionary<long, long>();
        var counts = new Dictionary<long, int>();
        var visited = new HashSet<long>();

        long ComputeSubtree(long nodeId)
        {
            if (visited.Contains(nodeId))
                return quantities.GetValueOrDefault(nodeId);
            visited.Add(nodeId);

            long qty = 0;
            var count = 0;
            if (leaves.TryGetValue(nodeId, out var nodeLeaves))
            {
                foreach (var leaf in nodeLeaves)
                {
                    qty += leaf.Quantity;
                    count++;
                }
            }
            if (children.TryGetValue(nodeId, out var childIds))
            {
                foreach (var childId in childIds)
                {
                    qty += ComputeSubtree(childId);
                    count += counts[childId];
                }
            }
            quantities[nodeId] = qty;
            counts[nodeId] = count;
            return qty;
        }

        foreach (var id in nodes.Keys.Where(id => !parents.ContainsKey(id)))
            ComputeSubtree(id);
        // Defensive Nachzügler (sollte durch die Wurzel-Erreichbarkeit nie vorkommen).
        foreach (var id in nodes.Keys)
            ComputeSubtree(id);

        HoldingsLocationNode Materialize(long nodeId)
        {
            var node = nodes[nodeId];
            var sortedChildren = (children.TryGetValue(nodeId, out var childIds) ? childIds : Enumerable.Empty<long>())
                .Select(Materialize)
                .OrderByDescending(c => c.Quantity).ThenBy(c => c.LocationId)
                .ToList();
            var sortedLeaves = (leaves.TryGetValue(nodeId, out var itemLeaves) ? itemLeaves : new List<HoldingsItemLeaf>())
                .OrderBy(l => l.ItemId)
                .ToList();
            return new HoldingsLocationNode
            {
                LocationId = node.LocationId,
                Kind = node.Kind,
                Name = node.Name,
                SolarSystemName = node.SolarSystemName,
                RegionName = node.RegionName,
                IsResolved = node.IsResolved,
                UnresolvedReason = node.UnresolvedReason,
                LocationFlag = node.LocationFlag,
                Quantity = quantities.GetValueOrDefault(nodeId),
                RawItemCount = counts.GetValueOrDefault(nodeId),
                Children = sortedChildren,
                Items = sortedLeaves
            };
        }

        // Baumwurzeln: aufgelöste Anker (dedupliziert, sortiert).
        var trees = rootIds
            .Distinct()
            .Select(Materialize)
            .OrderByDescending(t => t.Quantity).ThenBy(t => t.LocationId)
            .ToList();

        // Separater Unbekannt-Bucket.
        HoldingsLocationNode? unknown = null;
        var unknownChildren = unresolvedRootIds
            .Distinct()
            .Select(Materialize)
            .OrderByDescending(c => c.Quantity).ThenBy(c => c.LocationId)
            .ToList();
        if (unknownChildren.Count > 0)
        {
            var unknownQty = unknownChildren.Sum(c => c.Quantity);
            unknown = new HoldingsLocationNode
            {
                LocationId = UnknownBucketLocationId,
                Kind = LocationKind.Unresolved,
                Name = "Unbekannte Orte",
                IsResolved = false,
                Quantity = unknownQty,
                RawItemCount = unknownChildren.Sum(c => c.RawItemCount),
                Children = unknownChildren
            };
        }

        // Type-Projektion und Type/Location-Projektion.
        var typeGroups = resolvedItems.GroupBy(r => r.Item.TypeId);
        var typeAggregates = new List<HoldingsTypeAggregate>();
        var locationAggregates = new List<HoldingsTypeLocationAggregate>();
        foreach (var group in typeGroups.OrderByDescending(g => g.Sum(r => (long)r.Item.Quantity)).ThenBy(g => g.Key))
        {
            var items = group.ToList();
            var resolvedCount = items.Count(i => i.IsResolved);
            var locations = items
                .GroupBy(i => (i.Item.TypeId, i.Location.LocationId))
                .Select(g =>
                {
                    var first = g.First();
                    return new HoldingsTypeLocationAggregate
                    {
                        TypeId = g.Key.TypeId,
                        LocationId = g.Key.LocationId,
                        Kind = first.Location.Kind,
                        Name = first.Location.Name,
                        SolarSystemName = first.Location.SolarSystemName,
                        RegionName = first.Location.RegionName,
                        IsResolved = g.All(i => i.IsResolved),
                        UnresolvedReason = g.FirstOrDefault(i => i.UnresolvedReason != null)?.UnresolvedReason,
                        LocationFlag = first.Item.LocationFlag,
                        Quantity = g.Sum(i => (long)i.Item.Quantity),
                        RawItemCount = g.Count(),
                        ChainLocationIds = first.Chain.Select(c => c.LocationId).ToList()
                    };
                })
                .OrderBy(l => l.LocationId)
                .ToList();
            typeAggregates.Add(new HoldingsTypeAggregate
            {
                TypeId = group.Key,
                TypeName = typeNames.TryGetValue(group.Key, out var typeName) ? typeName : null,
                TotalQuantity = items.Sum(i => (long)i.Item.Quantity),
                RawItemCount = items.Count,
                ResolvedLocationItemCount = resolvedCount,
                UnknownLocationItemCount = items.Count - resolvedCount,
                Locations = locations
            });
            locationAggregates.AddRange(locations);
        }

        var total = resolvedItems.Sum(r => (long)r.Item.Quantity);
        var resolvedTotal = resolvedItems.Count(r => r.IsResolved);

        var age = now - syncedAt;
        return new HoldingsTreeResult
        {
            OwnerType = ownerType,
            OwnerId = ownerId,
            SnapshotId = snapshotId,
            SyncedAt = syncedAt,
            Age = age,
            IsFresh = age <= FreshnessThreshold,
            TotalQuantity = total,
            RawItemCount = resolvedItems.Count,
            ResolvedItemCount = resolvedTotal,
            UnknownLocationItemCount = resolvedItems.Count - resolvedTotal,
            LocationTrees = trees,
            UnknownLocations = unknown,
            TypeAggregates = typeAggregates,
            TypeLocationAggregates = locationAggregates
        };
    }
}