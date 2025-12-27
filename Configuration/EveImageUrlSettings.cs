namespace WALLEve.Configuration;

public class EveImageUrlSettings
{
    public string CharacterPortrait { get; set; } = "https://images.evetech.net/characters/{0}/portrait";
    public string CorporationLogo { get; set; } = "https://images.evetech.net/corporations/{0}/logo";
    public string AllianceLogo { get; set; } = "https://images.evetech.net/alliances/{0}/logo";

    public string GetCharacterPortraitUrl(int characterId, int size = 256)
        => $"{string.Format(CharacterPortrait, characterId)}?size={size}";

    public string GetCorporationLogoUrl(int corporationId, int size = 128)
        => $"{string.Format(CorporationLogo, corporationId)}?size={size}";

    public string GetAllianceLogoUrl(int allianceId, int size = 128)
        => $"{string.Format(AllianceLogo, allianceId)}?size={size}";
}
