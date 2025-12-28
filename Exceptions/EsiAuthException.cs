using System.Net;
using WALLEve.Models.Esi;

namespace WALLEve.Exceptions;

/// <summary>
/// Exception für ESI Authentifizierungs- und Autorisierungsfehler (401, 403)
/// </summary>
public class EsiAuthException : EsiApiException
{
    public bool IsUnauthorized => StatusCode == HttpStatusCode.Unauthorized;
    public bool IsForbidden => StatusCode == HttpStatusCode.Forbidden;

    public EsiAuthException(
        string endpoint,
        HttpStatusCode statusCode,
        RateLimitInfo? rateLimit = null)
        : base(
            BuildMessage(statusCode),
            endpoint,
            statusCode,
            null,
            rateLimit)
    {
    }

    private static string BuildMessage(HttpStatusCode statusCode)
    {
        return statusCode switch
        {
            HttpStatusCode.Unauthorized =>
                "ESI Authentifizierung fehlgeschlagen (401). Access Token ist ungültig oder abgelaufen.",
            HttpStatusCode.Forbidden =>
                "ESI Autorisierung fehlgeschlagen (403). Fehlende Berechtigung (Scope) oder Character nicht autorisiert.",
            _ => $"ESI Authentifizierungs-/Autorisierungsfehler ({(int)statusCode})."
        };
    }
}
