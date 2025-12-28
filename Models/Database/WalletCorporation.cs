using System.ComponentModel.DataAnnotations;

namespace WALLEve.Models.Database;

/// <summary>
/// Repräsentiert eine EVE Corporation in der Wallet-DB
/// Für Corporation Wallet Support
/// </summary>
public class WalletCorporation
{
    /// <summary>
    /// EVE Corporation ID (von ESI)
    /// </summary>
    [Key]
    public int CorporationId { get; set; }

    /// <summary>
    /// Corporation Name
    /// </summary>
    [Required]
    [MaxLength(255)]
    public required string CorporationName { get; set; }

    /// <summary>
    /// Letzter Sync-Zeitpunkt mit ESI
    /// </summary>
    public DateTime LastSyncedAt { get; set; }

    /// <summary>
    /// Wann wurde diese Corp zuerst hinzugefügt?
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Navigation: Alle Links für diese Corporation
    /// </summary>
    public ICollection<WalletEntryLink> Links { get; set; } = new List<WalletEntryLink>();
}
