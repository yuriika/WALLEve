namespace WALLEve.Extensions;

/// <summary>
/// Extension Methods für Security Status
/// </summary>
public static class SecurityExtensions
{
    public static string GetSecurityClass(this float security)
        => security switch
        {
            >= 0.5f => "highsec",
            >= 0.1f => "lowsec",
            _ => "nullsec"
        };

    public static string GetSecurityClassName(this float security)
        => security switch
        {
            >= 0.5f => "Highsec",
            >= 0.1f => "Lowsec",
            _ => "Nullsec"
        };

    public static string GetSecurityColor(this float security)
        => security switch
        {
            >= 0.5f => "#00ff00",   // Grün
            >= 0.1f => "#ffa500",   // Orange
            _ => "#ff0000"           // Rot
        };
}
