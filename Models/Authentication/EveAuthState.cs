namespace WALLEve.Models.Authentication;

public class EveAuthState
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public int CharacterId { get; set; }
    public string CharacterName { get; set; } = string.Empty;
    public List<string> Scopes { get; set; } = new();

    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    public bool IsValid => !string.IsNullOrEmpty(AccessToken) && !string.IsNullOrEmpty(RefreshToken);
}
