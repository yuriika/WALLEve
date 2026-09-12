namespace WALLEve.Models.Holdings;

/// <summary>
/// Ein Punkt-in-der-Zeit-Abbild der Bestände eines Owners (Rohdaten).
/// Der Owner-Schlüssel ist zusätzlich denormalisiert, damit Snapshots direkt
/// je Owner abfragbar sind, ohne über den SyncRun gehen zu müssen.
/// </summary>
public class HoldingSnapshot
{
    public long Id { get; set; }

    /// <summary>Zugehöriger Synchronisationslauf.</summary>
    public long SyncRunId { get; set; }

    /// <summary>Owner-Dimension (Character oder Corporation).</summary>
    public OwnerType OwnerType { get; set; }

    /// <summary>EVE-ID des Owners (CharacterId bzw. CorporationId).</summary>
    public int OwnerId { get; set; }

    public DateTime SyncedAt { get; set; }

    /// <summary>Herkunft der Rohdaten, z. B. "esi/characters/{id}/assets".</summary>
    public string Source { get; set; } = string.Empty;

    public HoldingSyncRun SyncRun { get; set; } = null!;

    public ICollection<HoldingItem> Items { get; set; } = new List<HoldingItem>();
}