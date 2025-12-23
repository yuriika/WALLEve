namespace WALLEve.Configuration;

public class EveOnlineSettings
{
    public string ClientId { get; set; } = string.Empty;
    public string CallbackUrl { get; set; } = "http://localhost:5000/callback";
    public string SsoBaseUrl { get; set; } = "https://login.eveonline.com/v2/oauth";
    public string EsiBaseUrl { get; set; } = "https://esi.evetech.net/latest";
    
    public List<string> Scopes { get; set; } = new()
    {
        "esi-characters.read_standings.v1",
        "esi-skills.read_skills.v1",
        "esi-skills.read_skillqueue.v1",
        "esi-wallet.read_character_wallet.v1",
        "esi-location.read_location.v1",
        "esi-location.read_online.v1",
        "esi-location.read_ship_type.v1"
    };

    public string ScopesString => string.Join(" ", Scopes);
}
