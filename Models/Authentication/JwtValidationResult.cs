namespace WALLEve.Models.Authentication;

/// <summary>Ergebnis einer JWT-Validierung durch den JwtTokenValidator.</summary>
public class JwtValidationResult
{
    public bool IsValid { get; init; }
    public EveJwtPayload? Payload { get; init; }
    public string? Error { get; init; }

    public static JwtValidationResult Valid(EveJwtPayload payload) => new() { IsValid = true, Payload = payload };
    public static JwtValidationResult Invalid(string error) => new() { IsValid = false, Error = error };
}