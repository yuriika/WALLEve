namespace WALLEve.Models.Holdings;

/// <summary>
/// Kategorie einer Asset-Location. Container sind Items innerhalb desselben
/// Bestands (location_type "item"); Station/Struktur/Sonnensystem sind Anker-
/// Locations. Unresolved kennzeichnet bewusst NICHT aufgelöste IDs — das
/// Auflösungsergebnis rät nie und erfindet keine System-/Region-Zuordnung.
/// </summary>
public enum LocationKind
{
    Station,
    Structure,
    SolarSystem,
    Container,
    Unresolved
}

/// <summary>
/// Auflösung EINER LocationId. Der Rohwert (LocationId) bleibt immer erhalten.
/// Unresolved-Einträge tragen den konkreten Grund (403, not-in-sde,
/// missing-parent, cycle, unknown-location, ...) statt zu raten oder zu werfen.
/// </summary>
public class LocationResolution
{
    public long LocationId { get; init; }

    public LocationKind Kind { get; init; }

    /// <summary>Name der Location (Station/Struktur/Sonnensystem); Container haben keinen Namen.</summary>
    public string? Name { get; init; }

    public int? SolarSystemId { get; init; }

    public string? SolarSystemName { get; init; }

    public int? RegionId { get; init; }

    public string? RegionName { get; init; }

    /// <summary>Fehler-/Alter-Grund bei Unresolved, sonst null.</summary>
    public string? UnresolvedReason { get; init; }

    public bool IsResolved => Kind != LocationKind.Unresolved;
}

/// <summary>
/// Aufgelöste Sicht auf ein Snapshot-Item: direkte Location, vollständige
/// Container-Kette bis zum Anker und Fehlerzustand. Reine Daten — keine UI,
/// kein neues Aggregat (Nicht-Ziele von #50).
/// </summary>
public class ResolvedHoldingItem
{
    public HoldingItem Item { get; init; } = null!;

    /// <summary>Direkte Location des Items (erstes Ketten-Element oder Container).</summary>
    public LocationResolution Location { get; init; } = null!;

    /// <summary>Kette von der direkten Location bis zum Anker (inclusive).</summary>
    public IReadOnlyList<LocationResolution> Chain { get; init; } = Array.Empty<LocationResolution>();

    /// <summary>Anker (Station/Struktur/Sonnensystem) am Kettenende, falls aufgelöst.</summary>
    public LocationResolution? Anchor { get; init; }

    /// <summary>Wahr, wenn die komplette Kette bis zum Anker aufgelöst ist.</summary>
    public bool IsResolved => Anchor != null && Chain.Count > 0 && Chain.All(c => c.IsResolved);

    /// <summary>Erster Fehlergrund in der Kette, sonst null.</summary>
    public string? UnresolvedReason => Chain.FirstOrDefault(c => !c.IsResolved)?.UnresolvedReason;
}