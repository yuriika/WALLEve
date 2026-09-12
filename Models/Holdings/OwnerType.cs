namespace WALLEve.Models.Holdings;

/// <summary>
/// Besitzer-Dimension eines Holdings-Datensatzes (EVE Owner).
/// Zusammen mit OwnerId bildet sie den zusammengesetzten Owner-Schlüssel.
/// </summary>
public enum OwnerType
{
    /// <summary>EVE-Character (owner_id = CharacterId).</summary>
    Character = 0,

    /// <summary>EVE-Corporation (owner_id = CorporationId).</summary>
    Corporation = 1
}