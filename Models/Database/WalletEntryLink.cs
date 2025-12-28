using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WALLEve.Models.Wallet;

namespace WALLEve.Models.Database;

/// <summary>
/// Repräsentiert eine persistierte Verknüpfung zwischen zwei Wallet-Einträgen
/// Unterstützt sowohl Character als auch Corporation Wallets
/// </summary>
public class WalletEntryLink
{
    /// <summary>
    /// Primary Key
    /// </summary>
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Source Journal Entry ID (z.B. Tax Entry)
    /// </summary>
    public long SourceEntryId { get; set; }

    /// <summary>
    /// Target Journal Entry ID (z.B. Market Transaction)
    /// </summary>
    public long TargetEntryId { get; set; }

    /// <summary>
    /// Character ID (für Character Wallets) - NULL für Corp Wallets
    /// </summary>
    [ForeignKey(nameof(Character))]
    public int? CharacterId { get; set; }

    /// <summary>
    /// Corporation ID (für Corp Wallets) - NULL für Character Wallets
    /// </summary>
    [ForeignKey(nameof(Corporation))]
    public int? CorporationId { get; set; }

    /// <summary>
    /// Corp Wallet Division (1-7) - nur für Corp Wallets
    /// </summary>
    public int? Division { get; set; }

    /// <summary>
    /// Art des Links (Tax, Market Order, etc.)
    /// </summary>
    public LinkType Type { get; set; }

    /// <summary>
    /// Wie sicher ist dieser Link?
    /// </summary>
    public LinkConfidence Confidence { get; set; }

    /// <summary>
    /// Wann wurde dieser Link erstellt?
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Wurde dieser Link vom Nutzer manuell bestätigt?
    /// </summary>
    public bool IsManuallyVerified { get; set; }

    /// <summary>
    /// Wurde dieser Link vom Nutzer abgelehnt?
    /// </summary>
    public bool IsManuallyRejected { get; set; }

    /// <summary>
    /// Von welchem System wurde der Link erstellt?
    /// (Heuristic, Manual, AI, etc.)
    /// </summary>
    [MaxLength(50)]
    public string? CreatedBy { get; set; } = "Heuristic";

    /// <summary>
    /// Optionale Notizen vom Nutzer
    /// </summary>
    [MaxLength(500)]
    public string? UserNotes { get; set; }

    // Navigation Properties
    public WalletCharacter? Character { get; set; }
    public WalletCorporation? Corporation { get; set; }
}

/// <summary>
/// Vertrauensstufe des Links
/// </summary>
public enum LinkConfidence
{
    /// <summary>
    /// 100% sicher (z.B. ContextId-Match)
    /// </summary>
    Direct = 1,

    /// <summary>
    /// Sehr wahrscheinlich (Zeit + Betrag passt perfekt)
    /// </summary>
    HeuristicHigh = 2,

    /// <summary>
    /// Wahrscheinlich (Zeit passt, Betrag ca.)
    /// </summary>
    HeuristicMedium = 3,

    /// <summary>
    /// Unsicher (nur Zeitfenster passt)
    /// </summary>
    HeuristicLow = 4,

    /// <summary>
    /// Vom Nutzer manuell bestätigt
    /// </summary>
    Manual = 5
}
