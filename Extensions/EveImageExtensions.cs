using WALLEve.Configuration;

namespace WALLEve.Extensions;

/// <summary>
/// Extension Methods für EVE Image URLs
/// </summary>
public static class EveImageExtensions
{
    public static string GetCharacterPortraitUrl(this int characterId, EveImageUrlSettings settings, int size = 256)
        => settings.GetCharacterPortraitUrl(characterId, size);

    public static string GetCorporationLogoUrl(this int corporationId, EveImageUrlSettings settings, int size = 128)
        => settings.GetCorporationLogoUrl(corporationId, size);

    public static string GetAllianceLogoUrl(this int allianceId, EveImageUrlSettings settings, int size = 128)
        => settings.GetAllianceLogoUrl(allianceId, size);
}
