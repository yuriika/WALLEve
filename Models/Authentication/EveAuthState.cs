namespace WALLEve.Models.Authentication;

public class EveAuthState
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public int CharacterId { get; set; }
    public string CharacterName { get; set; } = string.Empty;
    public List<string> Scopes { get; set; } = new();

    /// <summary>
    /// Beim Login zugestandene, aber von der App benötigte und nicht autorisierte
    /// Scopes (ordinaler Soll/Ist-Abgleich gegen <c>EveOnlineSettings.Scopes</c>).
    /// Leer, wenn alle benötigten Scopes autorisiert wurden. Ein fehlender Scope
    /// macht den Login nicht ungültig, sondern wird sichtbar gemeldet.
    /// </summary>
    public List<string> MissingScopes { get; set; } = new();

    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    public bool IsValid => !string.IsNullOrEmpty(AccessToken) && !string.IsNullOrEmpty(RefreshToken);
}
