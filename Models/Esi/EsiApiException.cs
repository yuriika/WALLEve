using System.Net;

namespace WALLEve.Models.Esi;

/// <summary>
/// Custom Exception für ESI API Fehler
/// </summary>
public class EsiApiException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public string Endpoint { get; }
    public RateLimitInfo? RateLimitInfo { get; }
    public string? ResponseBody { get; }

    public EsiApiException(
        string message,
        HttpStatusCode statusCode,
        string endpoint,
        RateLimitInfo? rateLimitInfo = null,
        string? responseBody = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Endpoint = endpoint;
        RateLimitInfo = rateLimitInfo;
        ResponseBody = responseBody;
    }

    public bool IsRateLimited => StatusCode == HttpStatusCode.TooManyRequests || (int)StatusCode == 420;
    public bool IsClientError => (int)StatusCode >= 400 && (int)StatusCode < 500;
    public bool IsServerError => (int)StatusCode >= 500;
    public bool IsAuthError => StatusCode == HttpStatusCode.Unauthorized || StatusCode == HttpStatusCode.Forbidden;

    public override string ToString()
    {
        var message = $"ESI API Error ({(int)StatusCode} {StatusCode}): {Message}\nEndpoint: {Endpoint}";

        if (RateLimitInfo != null)
        {
            message += $"\nRate Limit: {RateLimitInfo.Remaining}/{RateLimitInfo.Limit}";
        }

        if (!string.IsNullOrEmpty(ResponseBody))
        {
            message += $"\nResponse: {ResponseBody}";
        }

        return message;
    }
}
