using System.Net;

namespace WALLEve.Exceptions;

/// <summary>
/// Exception die bei ESI API Fehlern geworfen wird
/// </summary>
public class EsiApiException : Exception
{
    public HttpStatusCode? StatusCode { get; }
    public string? Endpoint { get; }
    public string? ResponseContent { get; }

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
        string? responseContent = null)
        : base(message)
    {
        Endpoint = endpoint;
        StatusCode = statusCode;
        ResponseContent = responseContent;
    }

    public EsiApiException(
        string message,
        Exception innerException,
        string? endpoint = null,
        HttpStatusCode? statusCode = null)
        : base(message, innerException)
    {
        Endpoint = endpoint;
        StatusCode = statusCode;
    }
}
