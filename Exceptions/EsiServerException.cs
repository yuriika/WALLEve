using System.Net;
using WALLEve.Models.Esi;

namespace WALLEve.Exceptions;

/// <summary>
/// Exception für ESI Server-Fehler (500, 502, 503, 504)
/// Wird geworfen wenn ESI selbst Probleme hat oder nicht erreichbar ist
/// </summary>
public class EsiServerException : EsiApiException
{
    public bool IsRetryable { get; }

    public EsiServerException(
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
        // 503 und 504 sind typischerweise vorübergehend und retry-fähig
        IsRetryable = statusCode == HttpStatusCode.ServiceUnavailable ||
                      statusCode == HttpStatusCode.GatewayTimeout;
    }

    private static string BuildMessage(HttpStatusCode statusCode)
    {
        return statusCode switch
        {
            HttpStatusCode.InternalServerError =>
                "ESI Internal Server Error (500). ESI hat interne Probleme.",
            HttpStatusCode.BadGateway =>
                "ESI Bad Gateway (502). ESI Proxy-Fehler.",
            HttpStatusCode.ServiceUnavailable =>
                "ESI Service Unavailable (503). ESI ist down oder in Wartung. Retry empfohlen.",
            HttpStatusCode.GatewayTimeout =>
                "ESI Gateway Timeout (504). ESI Request hat zu lange gedauert. Retry empfohlen.",
            _ => $"ESI Server-Fehler ({(int)statusCode})."
        };
    }
}
