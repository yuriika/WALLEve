using System.Net;
using WALLEve.Models.Esi;

namespace WALLEve.Exceptions;

/// <summary>
/// Basis-Exception für alle ESI API Fehler
/// </summary>
public class EsiApiException : Exception
{
    public HttpStatusCode? StatusCode { get; }
    public string? Endpoint { get; }
    public string? ResponseContent { get; }
    public RateLimitInfo? RateLimit { get; }

    public EsiApiException(string message) : base(message)
    {
    }

    public EsiApiException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public EsiApiException(
        string message,
        string? endpoint = null,
        HttpStatusCode? statusCode = null,
        string? responseContent = null,
        RateLimitInfo? rateLimit = null)
        : base(message)
    {
        Endpoint = endpoint;
        StatusCode = statusCode;
        ResponseContent = responseContent;
        RateLimit = rateLimit;
    }

    public EsiApiException(
        string message,
        Exception innerException,
        string? endpoint = null,
        HttpStatusCode? statusCode = null,
        RateLimitInfo? rateLimit = null)
        : base(message, innerException)
    {
        Endpoint = endpoint;
        StatusCode = statusCode;
        RateLimit = rateLimit;
    }
}
