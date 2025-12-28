using System.ComponentModel.DataAnnotations;

namespace WALLEve.Models.Database;

/// <summary>
/// Repräsentiert einen EVE Character in der Wallet-DB
/// Für Multi-Character Support
/// </summary>
public class WalletCharacter
{
    /// <summary>
    /// EVE Character ID (von ESI)
    /// </summary>
    [Key]
    public int CharacterId { get; set; }

    /// <summary>
    /// Character Name
    /// </summary>
    [Required]
    [MaxLength(255)]
    public required string CharacterName { get; set; }

    /// <summary>
    /// Letzter Sync-Zeitpunkt mit ESI
    /// </summary>
    public DateTime LastSyncedAt { get; set; }

    /// <summary>
    /// Wann wurde dieser Character zuerst hinzugefügt?
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Navigation: Alle Links für diesen Character
    /// </summary>
    public ICollection<WalletEntryLink> Links { get; set; } = new List<WalletEntryLink>();
}
