namespace WALLEve.Models.Database;

/// <summary>
/// Einfache Key-Value-Einstellungen, die den App-Neustart überleben.
/// Werte sind strings; Typ-Konvertierung übernimmt der aufrufende Service.
/// </summary>
public class AppSetting
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}