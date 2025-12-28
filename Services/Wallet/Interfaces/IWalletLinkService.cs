using WALLEve.Models.Database;
using WALLEve.Models.Wallet;

namespace WALLEve.Services.Wallet.Interfaces;

/// <summary>
/// Service für Verwaltung von Wallet Entry Links (Character & Corporation)
/// </summary>
public interface IWalletLinkService
{
    // ===== Character Wallet Links =====

    /// <summary>
    /// Holt alle Links für einen Character
    /// </summary>
    Task<List<WalletEntryLink>> GetCharacterLinksAsync(int characterId);

    /// <summary>
    /// Holt Links für spezifische Entry IDs eines Characters
    /// </summary>
    Task<List<WalletEntryLink>> GetCharacterLinksByEntryIdsAsync(int characterId, IEnumerable<long> entryIds);

    /// <summary>
    /// Speichert neue Links für einen Character
    /// </summary>
    Task SaveCharacterLinksAsync(int characterId, string characterName, IEnumerable<WalletEntryLink> links);

    // ===== Corporation Wallet Links =====

    /// <summary>
    /// Holt alle Links für eine Corporation Division
    /// </summary>
    Task<List<WalletEntryLink>> GetCorporationLinksAsync(int corporationId, int division);

    /// <summary>
    /// Holt Links für spezifische Entry IDs einer Corporation Division
    /// </summary>
    Task<List<WalletEntryLink>> GetCorporationLinksByEntryIdsAsync(
        int corporationId,
        int division,
        IEnumerable<long> entryIds);

    /// <summary>
    /// Speichert neue Links für eine Corporation Division
    /// </summary>
    Task SaveCorporationLinksAsync(
        int corporationId,
        string corporationName,
        int division,
        IEnumerable<WalletEntryLink> links);

    // ===== Manuelle Verwaltung =====

    /// <summary>
    /// Markiert einen Link als manuell bestätigt
    /// </summary>
    Task<bool> VerifyLinkAsync(int linkId);

    /// <summary>
    /// Markiert einen Link als manuell abgelehnt
    /// </summary>
    Task<bool> RejectLinkAsync(int linkId);

    /// <summary>
    /// Erstellt einen manuellen Link
    /// </summary>
    Task<WalletEntryLink> CreateManualLinkAsync(
        long sourceEntryId,
        long targetEntryId,
        LinkType type,
        int? characterId = null,
        int? corporationId = null,
        int? division = null,
        string? userNotes = null);

    /// <summary>
    /// Löscht einen Link
    /// </summary>
    Task<bool> DeleteLinkAsync(int linkId);

    // ===== Cleanup =====

    /// <summary>
    /// Löscht alte Links (älter als X Tage)
    /// </summary>
    Task<int> CleanupOldLinksAsync(int daysToKeep = 90);

    /// <summary>
    /// Löscht alle Links für einen Character
    /// </summary>
    Task<int> DeleteCharacterLinksAsync(int characterId);

    /// <summary>
    /// Löscht alle Links für eine Corporation
    /// </summary>
    Task<int> DeleteCorporationLinksAsync(int corporationId);
}
