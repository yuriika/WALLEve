using WALLEve.Models.Authentication;

namespace WALLEve.Services.Authentication.Interfaces;

/// <summary>Validiert EVE SSO JWT-Zugriffstokens gegen JWKS-Signatur, Issuer, Audience und Expiry.</summary>
public interface IJwtTokenValidator
{
    /// <summary>Validiert einen Access-Token und gibt das Ergebnis zurück.</summary>
    Task<JwtValidationResult> ValidateTokenAsync(string accessToken, string expectedClientId);
}