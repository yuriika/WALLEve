namespace WALLEve.Extensions;

/// <summary>
/// Extension Methods für Formatierung
/// </summary>
public static class FormattingExtensions
{
    /// <summary>
    /// Formatiert ISK Beträge (z.B. 1.5B, 250M, 10K)
    /// </summary>
    public static string FormatIsk(this double amount)
    {
        return amount switch
        {
            >= 1_000_000_000 => $"{amount / 1_000_000_000:N2} B",
            >= 1_000_000 => $"{amount / 1_000_000:N2} M",
            >= 1_000 => $"{amount / 1_000:N2} K",
            _ => amount.ToString("N2")
        };
    }

    /// <summary>
    /// Formatiert Skillpoints (z.B. 1.234.567)
    /// </summary>
    public static string FormatSkillPoints(this long skillPoints)
        => skillPoints.ToString("N0");

    /// <summary>
    /// Berechnet und formatiert das Charakteralter
    /// </summary>
    public static string GetCharacterAge(this DateTime birthday)
    {
        var age = DateTime.UtcNow - birthday;
        var years = (int)(age.TotalDays / 365.25);
        var months = (int)((age.TotalDays % 365.25) / 30.44);

        if (years > 0)
            return $"{years} Jahr{(years != 1 ? "e" : "")}, {months} Monat{(months != 1 ? "e" : "")}";
        return $"{months} Monat{(months != 1 ? "e" : "")}";
    }

    /// <summary>
    /// Formatiert Bytes zu lesbarer Größe
    /// </summary>
    public static string FormatBytes(this long bytes)
    {
        return bytes switch
        {
            >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
            >= 1_048_576 => $"{bytes / 1_048_576.0:F1} MB",
            >= 1_024 => $"{bytes / 1_024.0:F1} KB",
            _ => $"{bytes} Bytes"
        };
    }
}
