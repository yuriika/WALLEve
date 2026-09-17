namespace WALLEve.Models.Industry;

/// <summary>Verknüpfungszustand eines Blueprints zu seinem Holdings-Asset (#49).</summary>
public enum BlueprintLinkState
{
    /// <summary>Ein Holdings-Asset mit identischer ItemId desselben Owners existiert.</summary>
    Known = 0,

    /// <summary>
    /// Kein passendes Asset (fehlendes Asset, fehlender Snapshot oder partieller
    /// Sync) — es wird bewusst keine TypeId-only-Zuordnung erfunden.
    /// </summary>
    Unknown = 1
}

/// <summary>
/// Verknüpfung eines Blueprint-Registereintrags mit seinem Holdings-Asset (#49).
/// Die Zuordnung erfolgt ausschließlich über die Item-Identität (ItemId) innerhalb
/// derselben belegten Identität (CharacterId = Snapshot-Owner). Ein TypeId-only-
/// Match auf ein einzelnes Exemplar ist verboten: mehrere Blueprints desselben
/// Typs (z. B. ein BPO und ein BPC) wären sonst still falsch zugeordnet.
/// </summary>
public class BlueprintHoldingsLink
{
    public long ItemId { get; init; }

    public int TypeId { get; init; }

    /// <summary>Owner-Identität, gegen die verknüpft wurde (Owner-Isolation).</summary>
    public int CharacterId { get; init; }

    public BlueprintLinkState State { get; init; }

    /// <summary>Asset-Ort aus dem Holdings-Snapshot (nur bei Known).</summary>
    public long? LinkedLocationId { get; init; }

    /// <summary>Asset-LocationFlag aus dem Holdings-Snapshot (nur bei Known).</summary>
    public string? LinkedLocationFlag { get; init; }

    /// <summary>Asset-Menge aus dem Holdings-Snapshot (nur bei Known).</summary>
    public int? LinkedQuantity { get; init; }

    /// <summary>true, wenn ein Asset fehlt oder der Sync unvollständig sein könnte.</summary>
    public bool IsUnknown => State == BlueprintLinkState.Unknown;
}