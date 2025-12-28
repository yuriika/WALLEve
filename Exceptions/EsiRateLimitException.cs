using System.Net;
using WALLEve.Models.Esi;

namespace WALLEve.Exceptions;

/// <summary>
/// Exception für ESI Rate Limit Fehler (429 Too Many Requests)
/// </summary>
public class EsiRateLimitException : EsiApiException
{
    public int? RetryAfterSeconds { get; }

    public EsiRateLimitException(
        string endpoint,
        RateLimitInfo? rateLimit = null,
        int? retryAfter = null)
        : base(
            BuildMessage(rateLimit, retryAfter),
            endpoint,
            HttpStatusCode.TooManyRequests,
            null,
            rateLimit)
    {
        RetryAfterSeconds = retryAfter;
    }

    private static string BuildMessage(RateLimitInfo? rateLimit, int? retryAfter)
    {
        if (rateLimit != null)
        {
            return $"ESI Rate Limit erreicht. Verbleibend: {rateLimit.Remaining}/{rateLimit.Limit}. " +
                   $"Retry nach {retryAfter ?? rateLimit.RetryAfter}s.";
        }

        return $"ESI Rate Limit erreicht. Retry nach {retryAfter}s.";
    }
}
