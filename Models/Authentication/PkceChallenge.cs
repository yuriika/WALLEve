namespace WALLEve.Models.Authentication;

public class PkceChallenge
{
    public string CodeVerifier { get; set; } = string.Empty;
    public string CodeChallenge { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
}
