using System.Net;
using WALLEve.Models.Esi;

namespace WALLEve.Exceptions;

/// <summary>
/// Exception für ESI Not Found Fehler (404)
/// Wird geworfen wenn die angeforderte Ressource nicht existiert
/// </summary>
public class EsiNotFoundException : EsiApiException
{
    public EsiNotFoundException(
        string endpoint,
        RateLimitInfo? rateLimit = null)
        : base(
            "ESI Ressource nicht gefunden (404). Die angeforderte Ressource existiert nicht.",
            endpoint,
            HttpStatusCode.NotFound,
            null,
            rateLimit)
    {
    }
}
