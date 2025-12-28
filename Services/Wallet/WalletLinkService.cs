using Microsoft.EntityFrameworkCore;
using WALLEve.Data;
using WALLEve.Models.Database;
using WALLEve.Models.Wallet;
using WALLEve.Services.Wallet.Interfaces;

namespace WALLEve.Services.Wallet;

/// <summary>
/// Service für Verwaltung von Wallet Entry Links
/// </summary>
public class WalletLinkService : IWalletLinkService
{
    private readonly WalletDbContext _dbContext;
    private readonly ILogger<WalletLinkService> _logger;

    public WalletLinkService(WalletDbContext dbContext, ILogger<WalletLinkService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    // ===== Character Wallet Links =====

    public async Task<List<WalletEntryLink>> GetCharacterLinksAsync(int characterId)
    {
        return await _dbContext.Links
            .Where(l => l.CharacterId == characterId)
            .Where(l => !l.IsManuallyRejected) // Exclude rejected links
            .ToListAsync();
    }

    public async Task<List<WalletEntryLink>> GetCharacterLinksByEntryIdsAsync(
        int characterId,
        IEnumerable<long> entryIds)
    {
        var entryIdList = entryIds.ToList();

        return await _dbContext.Links
            .Where(l => l.CharacterId == characterId)
            .Where(l => !l.IsManuallyRejected)
            .Where(l => entryIdList.Contains(l.SourceEntryId) || entryIdList.Contains(l.TargetEntryId))
            .ToListAsync();
    }

    public async Task SaveCharacterLinksAsync(
        int characterId,
        string characterName,
        IEnumerable<WalletEntryLink> links)
    {
        try
        {
            // Ensure Character exists in DB
            await EnsureCharacterExistsAsync(characterId, characterName);

            var linksList = links.ToList();

            // Set CharacterId for all links
            foreach (var link in linksList)
            {
                link.CharacterId = characterId;
                link.CorporationId = null;
                link.Division = null;
            }

            // Add new links (ignore duplicates)
            foreach (var link in linksList)
            {
                var exists = await _dbContext.Links
                    .AnyAsync(l =>
                        l.SourceEntryId == link.SourceEntryId &&
                        l.TargetEntryId == link.TargetEntryId &&
                        l.CharacterId == characterId);

                if (!exists)
                {
                    await _dbContext.Links.AddAsync(link);
                }
            }

            var savedCount = await _dbContext.SaveChangesAsync();
            _logger.LogInformation("Saved {Count} new links for character {CharacterId}", savedCount, characterId);

            // Update LastSyncedAt
            var character = await _dbContext.Characters.FindAsync(characterId);
            if (character != null)
            {
                character.LastSyncedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save character links for {CharacterId}", characterId);
            throw;
        }
    }

    // ===== Corporation Wallet Links =====

    public async Task<List<WalletEntryLink>> GetCorporationLinksAsync(int corporationId, int division)
    {
        return await _dbContext.Links
            .Where(l => l.CorporationId == corporationId && l.Division == division)
            .Where(l => !l.IsManuallyRejected)
            .ToListAsync();
    }

    public async Task<List<WalletEntryLink>> GetCorporationLinksByEntryIdsAsync(
        int corporationId,
        int division,
        IEnumerable<long> entryIds)
    {
        var entryIdList = entryIds.ToList();

        return await _dbContext.Links
            .Where(l => l.CorporationId == corporationId && l.Division == division)
            .Where(l => !l.IsManuallyRejected)
            .Where(l => entryIdList.Contains(l.SourceEntryId) || entryIdList.Contains(l.TargetEntryId))
            .ToListAsync();
    }

    public async Task SaveCorporationLinksAsync(
        int corporationId,
        string corporationName,
        int division,
        IEnumerable<WalletEntryLink> links)
    {
        try
        {
            // Ensure Corporation exists in DB
            await EnsureCorporationExistsAsync(corporationId, corporationName);

            var linksList = links.ToList();

            // Set CorporationId and Division for all links
            foreach (var link in linksList)
            {
                link.CharacterId = null;
                link.CorporationId = corporationId;
                link.Division = division;
            }

            // Add new links (ignore duplicates)
            foreach (var link in linksList)
            {
                var exists = await _dbContext.Links
                    .AnyAsync(l =>
                        l.SourceEntryId == link.SourceEntryId &&
                        l.TargetEntryId == link.TargetEntryId &&
                        l.CorporationId == corporationId &&
                        l.Division == division);

                if (!exists)
                {
                    await _dbContext.Links.AddAsync(link);
                }
            }

            var savedCount = await _dbContext.SaveChangesAsync();
            _logger.LogInformation("Saved {Count} new links for corporation {CorporationId} division {Division}",
                savedCount, corporationId, division);

            // Update LastSyncedAt
            var corporation = await _dbContext.Corporations.FindAsync(corporationId);
            if (corporation != null)
            {
                corporation.LastSyncedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save corporation links for {CorporationId} division {Division}",
                corporationId, division);
            throw;
        }
    }

    // ===== Manuelle Verwaltung =====

    public async Task<bool> VerifyLinkAsync(int linkId)
    {
        var link = await _dbContext.Links.FindAsync(linkId);
        if (link == null) return false;

        link.IsManuallyVerified = true;
        link.IsManuallyRejected = false;
        link.Confidence = LinkConfidence.Manual;
        link.CreatedBy = "Manual";

        await _dbContext.SaveChangesAsync();
        return true;
    }

    public async Task<bool> RejectLinkAsync(int linkId)
    {
        var link = await _dbContext.Links.FindAsync(linkId);
        if (link == null) return false;

        link.IsManuallyRejected = true;
        link.IsManuallyVerified = false;

        await _dbContext.SaveChangesAsync();
        return true;
    }

    public async Task<WalletEntryLink> CreateManualLinkAsync(
        long sourceEntryId,
        long targetEntryId,
        LinkType type,
        int? characterId = null,
        int? corporationId = null,
        int? division = null,
        string? userNotes = null)
    {
        // Validate: Entweder Character ODER Corporation
        if ((characterId.HasValue && corporationId.HasValue) ||
            (!characterId.HasValue && !corporationId.HasValue))
        {
            throw new ArgumentException("Exactly one of characterId or corporationId must be provided");
        }

        // Validate: Division nur für Corporation
        if (division.HasValue && !corporationId.HasValue)
        {
            throw new ArgumentException("Division can only be set for corporation links");
        }

        var link = new WalletEntryLink
        {
            SourceEntryId = sourceEntryId,
            TargetEntryId = targetEntryId,
            Type = type,
            CharacterId = characterId,
            CorporationId = corporationId,
            Division = division,
            Confidence = LinkConfidence.Manual,
            IsManuallyVerified = true,
            CreatedBy = "Manual",
            UserNotes = userNotes,
            CreatedAt = DateTime.UtcNow
        };

        await _dbContext.Links.AddAsync(link);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Created manual link {LinkId} between {SourceId} and {TargetId}",
            link.Id, sourceEntryId, targetEntryId);

        return link;
    }

    public async Task<bool> DeleteLinkAsync(int linkId)
    {
        var link = await _dbContext.Links.FindAsync(linkId);
        if (link == null) return false;

        _dbContext.Links.Remove(link);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Deleted link {LinkId}", linkId);
        return true;
    }

    // ===== Cleanup =====

    public async Task<int> CleanupOldLinksAsync(int daysToKeep = 90)
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-daysToKeep);

        var oldLinks = await _dbContext.Links
            .Where(l => l.CreatedAt < cutoffDate)
            .Where(l => !l.IsManuallyVerified) // Keep manually verified links
            .ToListAsync();

        _dbContext.Links.RemoveRange(oldLinks);
        var deletedCount = await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Cleaned up {Count} old links (older than {Days} days)", deletedCount, daysToKeep);
        return deletedCount;
    }

    public async Task<int> DeleteCharacterLinksAsync(int characterId)
    {
        var links = await _dbContext.Links
            .Where(l => l.CharacterId == characterId)
            .ToListAsync();

        _dbContext.Links.RemoveRange(links);
        var deletedCount = await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Deleted {Count} links for character {CharacterId}", deletedCount, characterId);
        return deletedCount;
    }

    public async Task<int> DeleteCorporationLinksAsync(int corporationId)
    {
        var links = await _dbContext.Links
            .Where(l => l.CorporationId == corporationId)
            .ToListAsync();

        _dbContext.Links.RemoveRange(links);
        var deletedCount = await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Deleted {Count} links for corporation {CorporationId}", deletedCount, corporationId);
        return deletedCount;
    }

    // ===== Helper Methods =====

    private async Task EnsureCharacterExistsAsync(int characterId, string characterName)
    {
        var exists = await _dbContext.Characters.AnyAsync(c => c.CharacterId == characterId);

        if (!exists)
        {
            var character = new WalletCharacter
            {
                CharacterId = characterId,
                CharacterName = characterName,
                CreatedAt = DateTime.UtcNow,
                LastSyncedAt = DateTime.UtcNow
            };

            await _dbContext.Characters.AddAsync(character);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Created new character entry {CharacterId} - {CharacterName}",
                characterId, characterName);
        }
    }

    private async Task EnsureCorporationExistsAsync(int corporationId, string corporationName)
    {
        var exists = await _dbContext.Corporations.AnyAsync(c => c.CorporationId == corporationId);

        if (!exists)
        {
            var corporation = new WalletCorporation
            {
                CorporationId = corporationId,
                CorporationName = corporationName,
                CreatedAt = DateTime.UtcNow,
                LastSyncedAt = DateTime.UtcNow
            };

            await _dbContext.Corporations.AddAsync(corporation);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Created new corporation entry {CorporationId} - {CorporationName}",
                corporationId, corporationName);
        }
    }
}
