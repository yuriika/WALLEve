using WALLEve.Models.Holdings;

namespace WALLEve.Services.Holdings;

/// <summary>
/// Ergebnis der reinen Kettenauflösung eines einzelnen Items.
/// </summary>
public sealed class ChainResult
{
    public IReadOnlyList<LocationResolution> Chain { get; init; } = Array.Empty<LocationResolution>();

    public LocationResolution? Anchor { get; init; }

    public bool IsResolved { get; init; }

    public string? UnresolvedReason { get; init; }
}

/// <summary>
/// Reine, deterministische Auflösung der Parent-Kette (Kern von #50).
/// Verfolgt Container-Items über ItemId-Lookup in den Bestand hinein, bis ein
/// Anker (Station/Struktur/Sonnensystem) oder ein Fehlerzustand erreicht ist.
/// Keine Datenbank-, ESI- oder SDE-Zugriffe — nur der Item-Bestand und die
/// bereits extern aufgelösten Locations. Zyklen und fehlende Parents erzeugen
/// Unresolved statt Absturz oder Endlosschleife.
/// </summary>
public static class LocationChainResolver
{
    /// <summary>
    /// Klassifikation einer LocationId anhand der etablierten EVE-ID-Bereiche
    /// (ESI-Dokumentation): Sonnensysteme 30.000.000–39.999.999, NPC-Stationen
    /// 60.000.000–69.999.999, Spielerstrukturen ab 1e12. Container werden NICHT
    /// über Bereiche, sondern über die Mitgliedschaft im Item-Bestand erkannt.
    /// Unbekannte Bereiche liefern ehrlich Unresolved statt erfundener Zuordnung.
    /// </summary>
    public static LocationKind Classify(long locationId)
    {
        if (locationId >= 30_000_000 && locationId < 40_000_000)
            return LocationKind.SolarSystem;
        if (locationId >= 60_000_000 && locationId < 70_000_000)
            return LocationKind.Station;
        if (locationId >= 1_000_000_000_000)
            return LocationKind.Structure;
        return LocationKind.Unresolved;
    }

    /// <summary>
    /// Löst die Location-Kette eines Items auf. <paramref name="itemsById"/>
    /// enthält alle Bestands-Items (ItemId → Item), <paramref name="external"/>
    /// die bereits extern aufgelösten (oder bewusst fehlgeschlagenen)
    /// Nicht-Container-Locations (Station/Struktur/Sonnensystem).
    /// </summary>
    public static ChainResult ResolveChain(
        HoldingItem item,
        IReadOnlyDictionary<long, HoldingItem> itemsById,
        IReadOnlyDictionary<long, LocationResolution> external)
    {
        var chain = new List<LocationResolution>();
        var visited = new HashSet<long>();
        var node = item.LocationId;
        HoldingItem? source = item;

        while (true)
        {
            if (!visited.Add(node))
            {
                // Zyklus in der Parent-Kette (A in B, B in A): niemals raten.
                return Failed(node, "cycle", chain);
            }

            if (itemsById.TryGetValue(node, out var container))
            {
                // Location ist selbst ein Item im Bestand → Container-Knoten, weiterlaufen.
                chain.Add(new LocationResolution { LocationId = node, Kind = LocationKind.Container });
                source = container;
                node = container.LocationId;
                continue;
            }

            if (external.TryGetValue(node, out var ext))
            {
                chain.Add(ext);
                if (ext.IsResolved)
                {
                    return new ChainResult
                    {
                        Chain = chain,
                        Anchor = ext,
                        IsResolved = true
                    };
                }
                // Externer Knoten ist bereits Unresolved (z. B. 403, not-in-sde):
                // Grund übernehmen, keinen zweiten Eintrag für dieselbe ID erzeugen.
                return new ChainResult
                {
                    Chain = chain,
                    Anchor = null,
                    IsResolved = false,
                    UnresolvedReason = ext.UnresolvedReason ?? "unknown-location"
                };
            }

            // Weder Bestands-Item noch extern bekannt: Ist die Location laut
            // ESI ein Item-Verweis (ParentItemId), fehlt der Parent im Bestand;
            // sonst ist die ID unbekannt. Beides ist Unresolved, kein System-Rat.
            var reason = source.ParentItemId.HasValue
                ? "missing-parent"
                : "unknown-location";
            return Failed(node, reason, chain);
        }
    }

    private static ChainResult Failed(long locationId, string reason, List<LocationResolution> chain)
    {
        chain.Add(new LocationResolution
        {
            LocationId = locationId,
            Kind = LocationKind.Unresolved,
            UnresolvedReason = reason
        });
        return new ChainResult
        {
            Chain = chain,
            Anchor = null,
            IsResolved = false,
            UnresolvedReason = reason
        };
    }
}