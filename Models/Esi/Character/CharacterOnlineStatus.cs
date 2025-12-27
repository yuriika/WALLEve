using System.Text.Json.Serialization;

namespace WALLEve.Models.Esi.Character;

public class CharacterOnlineStatus
{
    [JsonPropertyName("online")]
    public bool Online { get; set; }

    [JsonPropertyName("last_login")]
    public DateTime? LastLogin { get; set; }

    [JsonPropertyName("last_logout")]
    public DateTime? LastLogout { get; set; }

    [JsonPropertyName("logins")]
    public int? Logins { get; set; }
}
