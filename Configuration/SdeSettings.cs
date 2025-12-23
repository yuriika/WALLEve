namespace WALLEve.Configuration;

/// <summary>
/// Konfiguration für den Static Data Export
/// </summary>
public class SdeSettings
{
    /// <summary>
    /// Aktiviert automatische Prüfung auf Updates beim Start
    /// </summary>
    public bool CheckForUpdatesOnStartup { get; set; } = true;

    /// <summary>
    /// Leitet zur Settings-Seite um, wenn SDE fehlt oder zu alt ist
    /// </summary>
    public bool RequireSdeForStartup { get; set; } = true;

    /// <summary>
    /// Maximales Alter der SDE in Tagen, bevor sie als "veraltet" gilt
    /// </summary>
    public int MaxAgeDays { get; set; } = 30;

    /// <summary>
    /// URL zur SQLite SDE (Fuzzwork)
    /// </summary>
    public string DownloadUrl { get; set; } = "https://www.fuzzwork.co.uk/dump/sqlite-latest.sqlite.bz2";

    /// <summary>
    /// URL zur MD5-Prüfsumme
    /// </summary>
    public string ChecksumUrl { get; set; } = "https://www.fuzzwork.co.uk/dump/sqlite-latest.sqlite.bz2.md5";

    /// <summary>
    /// Lokaler Dateiname der SDE
    /// </summary>
    public string LocalFileName { get; set; } = "sde.sqlite";
}