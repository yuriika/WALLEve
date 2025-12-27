namespace WALLEve.Exceptions;

/// <summary>
/// Exception die bei Authentifizierungsfehlern geworfen wird
/// </summary>
public class AuthenticationException : Exception
{
    public string? CharacterName { get; }
    public int? CharacterId { get; }

    public AuthenticationException(string message) : base(message)
    {
    }

    public AuthenticationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public AuthenticationException(
        string message,
        string? characterName = null,
        int? characterId = null)
        : base(message)
    {
        CharacterName = characterName;
        CharacterId = characterId;
    }
}
