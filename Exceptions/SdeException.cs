namespace WALLEve.Exceptions;

/// <summary>
/// Exception die bei SDE Datenbankfehlern geworfen wird
/// </summary>
public class SdeException : Exception
{
    public string? Query { get; }
    public int? TypeId { get; }

    public SdeException(string message) : base(message)
    {
    }

    public SdeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public SdeException(
        string message,
        string? query = null,
        int? typeId = null)
        : base(message)
    {
        Query = query;
        TypeId = typeId;
    }

    public SdeException(
        string message,
        Exception innerException,
        string? query = null)
        : base(message, innerException)
    {
        Query = query;
    }
}
