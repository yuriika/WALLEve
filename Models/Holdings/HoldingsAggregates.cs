namespace WALLEve.Models.Holdings;

/// <summary>
/// Aggregierte Sicht auf den neuesten Holdings-Snapshot eines Owners (#57).
/// Enthält die Type- und Type/Location-Projektion sowie den Ortsbaum für den
/// Drill-down bis zu den Rohdimensionen (ItemId/TypeId/Menge/Flag/Singleton).
/// Die Summen sind mengengleich zum Snapshot; unbekannte Orte erscheinen
/// separat und vermischen sich nie mit aufgelösten Orten. Owner werden über
/// OwnerType/OwnerId strikt getrennt — kein Baum, keine Summe mischt Owner.
/// </summary>
public sealed class HoldingsTreeResult
{
    public OwnerType OwnerType { get; init; }

    public int OwnerId { get; init; }

    /// <summary>Quell-Snapshot dieses Baums (Freshness verankert an SyncedAt).</summary>
    public long SnapshotId { get; init; }

    public DateTime SyncedAt { get; init; }

    /// <summary>Alter des Snapshots zum Erstellungszeitpunkt.</summary>
    public TimeSpan Age { get; init; }

    /// <summary>Freshness: Alter höchstens 24 Stunden.</summary>
    public bool IsFresh { get; init; }

    /// <summary>Mengengleiche Gesamtmenge über ALLE Orte (aufgelöst + unbekannt).</summary>
    public long TotalQuantity { get; init; }

    /// <summary>Anzahl Roh-Item-Zeilen im Snapshot.</summary>
    public int RawItemCount { get; init; }

    /// <summary>Qualität: Item-Zeilen, deren Location-Kette vollständig aufgelöst ist.</summary>
    public int ResolvedItemCount { get; init; }

    /// <summary>Qualität: Item-Zeilen mit unbekannter Location (separat sichtbar).</summary>
    public int UnknownLocationItemCount { get; init; }

    /// <summary>Ortsbaum: Wurzel je aufgelöstem Anker (Station/Struktur/Sonnensystem); Container als Kinder.</summary>
    public IReadOnlyList<HoldingsLocationNode> LocationTrees { get; init; } = Array.Empty<HoldingsLocationNode>();

    /// <summary>Separater Bucket für Items mit unbekannter Location (Anker unaufgelöst), sonst null.</summary>
    public HoldingsLocationNode? UnknownLocations { get; init; }

    /// <summary>Type-Projektion: eine Zeile je (Owner, TypeId) mit Menge und Qualität.</summary>
    public IReadOnlyList<HoldingsTypeAggregate> TypeAggregates { get; init; } = Array.Empty<HoldingsTypeAggregate>();

    /// <summary>Type/Location-Projektion: eine Zeile je (Owner, TypeId, direkte Location).</summary>
    public IReadOnlyList<HoldingsTypeLocationAggregate> TypeLocationAggregates { get; init; } = Array.Empty<HoldingsTypeLocationAggregate>();
}

/// <summary>
/// Knoten des Ortsbaums. Ein Knoten steht für eine Location (Anker oder
/// Container) und aggregiert die Mengen aller Items seines Teilbaums.
/// Item-Rohzeilen (Drill-down) hängen am tiefsten Knoten ihrer Kette.
/// </summary>
public sealed class HoldingsLocationNode
{
    /// <summary>LocationId des Knotens; bei Container-Knoten die ItemId des Containers.</summary>
    public long LocationId { get; init; }

    public LocationKind Kind { get; init; }

    /// <summary>Name des Ankers (Station/Struktur) bzw. Container-Typname; null wenn unbekannt.</summary>
    public string? Name { get; init; }

    public string? SolarSystemName { get; init; }

    public string? RegionName { get; init; }

    public bool IsResolved { get; init; }

    public string? UnresolvedReason { get; init; }

    /// <summary>LocationFlag des Containers (aus dessen eigener Rohzeile); bei Ankern null.</summary>
    public string? LocationFlag { get; init; }

    /// <summary>Summe der Mengen dieses Teilbaums (Items direkt + alle Kinder).</summary>
    public long Quantity { get; init; }

    /// <summary>Anzahl der Item-Rohzeilen dieses Teilbaums.</summary>
    public int RawItemCount { get; init; }

    public IReadOnlyList<HoldingsLocationNode> Children { get; init; } = Array.Empty<HoldingsLocationNode>();

    /// <summary>Roh-Items, die direkt an diesem Knoten liegen (Drill-down).</summary>
    public IReadOnlyList<HoldingsItemLeaf> Items { get; init; } = Array.Empty<HoldingsItemLeaf>();
}

/// <summary>
/// Roh-Item-Zeile im Drill-down. Trägt die unveränderten Rohdimensionen des
/// Snapshots (#57: "Rohdimensionen bleiben über Drill-down erreichbar").
/// </summary>
public sealed class HoldingsItemLeaf
{
    public long ItemId { get; init; }

    public int TypeId { get; init; }

    public string? TypeName { get; init; }

    public int Quantity { get; init; }

    public bool IsSingleton { get; init; }

    public string LocationFlag { get; init; } = string.Empty;
}

/// <summary>
/// Type-Projektion (#57): je (Owner, TypeId) die Gesamtmenge, Anzahl Roh-Zeilen
/// und Qualität (aufgelöste vs. unbekannte Locations).
/// </summary>
public sealed class HoldingsTypeAggregate
{
    public int TypeId { get; init; }

    public string? TypeName { get; init; }

    public long TotalQuantity { get; init; }

    public int RawItemCount { get; init; }

    public int ResolvedLocationItemCount { get; init; }

    public int UnknownLocationItemCount { get; init; }

    public bool AllLocationsResolved => UnknownLocationItemCount == 0;

    /// <summary>Orte dieses Typs (Type/Location-Sicht) — Drill-down je Typ.</summary>
    public IReadOnlyList<HoldingsTypeLocationAggregate> Locations { get; init; } = Array.Empty<HoldingsTypeLocationAggregate>();
}

/// <summary>
/// Type/Location-Projektion (#57): je (Owner, TypeId, direkte Location) Menge,
/// Anzahl Roh-Zeilen, aufgelöste Ortsinformation und Unresolved-Grund. Die
/// direkte Location ist das erste Ketten-Element (Container oder Anker).
/// </summary>
public sealed class HoldingsTypeLocationAggregate
{
    public int TypeId { get; init; }

    public long LocationId { get; init; }

    public LocationKind Kind { get; init; }

    public string? Name { get; init; }

    public string? SolarSystemName { get; init; }

    public string? RegionName { get; init; }

    /// <summary>Qualität: komplette Kette bis zum Anker aufgelöst.</summary>
    public bool IsResolved { get; init; }

    public string? UnresolvedReason { get; init; }

    public string LocationFlag { get; init; } = string.Empty;

    public long Quantity { get; init; }

    public int RawItemCount { get; init; }

    /// <summary>LocationIds der Kette von der direkten Location bis zum Anker (inclusive).</summary>
    public IReadOnlyList<long> ChainLocationIds { get; init; } = Array.Empty<long>();
}