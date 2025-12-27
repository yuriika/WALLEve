namespace WALLEve.Models.Esi;

/// <summary>
/// ESI Rate Limiting Informationen aus Response Headers
/// </summary>
public class RateLimitInfo
{
    /// <summary>
    /// Route group identifier (z.B. "character_wallet_journal")
    /// </summary>
    public string? Group { get; set; }

    /// <summary>
    /// Total tokens per window (z.B. "150/15m")
    /// </summary>
    public string? Limit { get; set; }

    /// <summary>
    /// Verfügbare Tokens
    /// </summary>
    public int? Remaining { get; set; }

    /// <summary>
    /// Tokens consumed by this request
    /// </summary>
    public int? Used { get; set; }

    /// <summary>
    /// Sekunden bis retry möglich (nur bei 429)
    /// </summary>
    public int? RetryAfter { get; set; }

    /// <summary>
    /// Verbleibende Errors im Zeitfenster
    /// </summary>
    public int? ErrorLimitRemain { get; set; }

    /// <summary>
    /// Sekunden bis Error-Limit Reset
    /// </summary>
    public int? ErrorLimitReset { get; set; }

    /// <summary>
    /// Warnung wenn Token-Budget niedrig (< 10%)
    /// </summary>
    public bool IsLowOnTokens()
    {
        if (Remaining == null) return false;

        // Parse Limit string (z.B. "150/15m" -> 150)
        if (Limit != null && Limit.Contains('/'))
        {
            var limitStr = Limit.Split('/')[0];
            if (int.TryParse(limitStr, out var maxTokens))
            {
                return Remaining.Value < (maxTokens * 0.1);
            }
        }

        return Remaining.Value < 10;
    }

    /// <summary>
    /// Kritisch niedrige Error-Limit (< 20 Errors verbleibend)
    /// </summary>
    public bool IsLowOnErrorBudget()
    {
        return ErrorLimitRemain.HasValue && ErrorLimitRemain.Value < 20;
    }
}
