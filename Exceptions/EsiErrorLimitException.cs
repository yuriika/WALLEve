using System.Net;
using WALLEve.Models.Esi;

namespace WALLEve.Exceptions;

/// <summary>
/// Exception für ESI Error Limit (420 - zu viele Fehler)
/// Dieser spezielle ESI-Statuscode bedeutet, dass zu viele fehlerhafte Requests gesendet wurden
/// und alle weiteren Requests blockiert werden bis zum Reset-Zeitpunkt.
/// </summary>
public class EsiErrorLimitException : EsiApiException
{
    public int? ErrorsRemaining { get; }
    public int? ErrorLimit { get; }
    public int? ResetInSeconds { get; }

    public EsiErrorLimitException(
        string endpoint,
        RateLimitInfo? rateLimit = null)
        : base(
            BuildMessage(rateLimit),
            endpoint,
            (HttpStatusCode)420,
            null,
            rateLimit)
    {
        ErrorsRemaining = rateLimit?.ErrorLimitRemain;
        ErrorLimit = rateLimit?.ErrorLimitRemain; // Note: Appears to be same value in original code
        ResetInSeconds = rateLimit?.ErrorLimitReset;
    }

    private static string BuildMessage(RateLimitInfo? rateLimit)
    {
        if (rateLimit != null)
        {
            return $"ESI Error Limit erreicht (420). Zu viele fehlerhafte Requests. " +
                   $"Verbleibende Fehler: {rateLimit.ErrorLimitRemain}/{rateLimit.ErrorLimitRemain}. " +
                   $"Reset in {rateLimit.ErrorLimitReset}s. " +
                   $"Alle Requests sind blockiert bis zum Reset.";
        }

        return "ESI Error Limit erreicht (420). Zu viele fehlerhafte Requests. Requests blockiert.";
    }
}
