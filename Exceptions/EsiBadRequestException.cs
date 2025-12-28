using System.Net;
using WALLEve.Models.Esi;

namespace WALLEve.Exceptions;

/// <summary>
/// Exception für ESI Bad Request Fehler (400)
/// Wird geworfen wenn die Request-Parameter ungültig oder fehlerhaft sind
/// </summary>
public class EsiBadRequestException : EsiApiException
{
    public EsiBadRequestException(
        string endpoint,
        string? responseContent = null,
        RateLimitInfo? rateLimit = null)
        : base(
            "ESI Bad Request (400). Ungültige Parameter oder fehlerhafte Request-Struktur.",
            endpoint,
            HttpStatusCode.BadRequest,
            responseContent,
            rateLimit)
    {
    }
}
