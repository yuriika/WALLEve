namespace WALLEve.Models.Holdings;

/// <summary>
/// Ein Synchronisationslauf, der die Bestände EINES Owners gegen ESI abgleicht.
/// Trägt den zusammengesetzten Owner-Schlüssel (OwnerType/OwnerId), damit
/// gleiche Item-IDs verschiedener Owner niemals kollidieren.
/// </summary>
public class HoldingSyncRun
{
    public long Id { get; set; }

    /// <summary>Owner-Dimension (Character oder Corporation).</summary>
    public OwnerType OwnerType { get; set; }

    /// <summary>EVE-ID des Owners (CharacterId bzw. CorporationId).</summary>
    public int OwnerId { get; set; }

    public DateTime StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    /// <summary>Lauf-Status, z. B. "running", "completed", "failed".</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Letzte Fehlermeldung, falls der Lauf fehlschlug.</summary>
    public string? Error { get; set; }

    public ICollection<HoldingSnapshot> Snapshots { get; set; } = new List<HoldingSnapshot>();
}