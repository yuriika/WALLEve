namespace WALLEve.Models;

/// <summary>
/// Status der lokalen SDE-Datenbank
/// </summary>
public class SdeStatus
{
    /// <summary>
    /// Ist die SDE-Datei vorhanden?
    /// </summary>
    public bool Exists { get; set; }

    /// <summary>
    /// Vollständiger Pfad zur SDE-Datei
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Größe der Datei in Bytes
    /// </summary>
    public long FileSizeBytes { get; set; }

    /// <summary>
    /// Größe der Datei formatiert (z.B. "412 MB")
    /// </summary>
    public string FileSizeFormatted => FormatBytes(FileSizeBytes);

    /// <summary>
    /// Datum der lokalen Datei
    /// </summary>
    public DateTime? LocalFileDate { get; set; }

    /// <summary>
    /// MD5-Hash der lokalen Datei
    /// </summary>
    public string? LocalChecksum { get; set; }

    /// <summary>
    /// Datum der Remote-Version (aus HTTP Header, falls verfügbar)
    /// </summary>
    public DateTime? RemoteFileDate { get; set; }

    /// <summary>
    /// MD5-Hash der Online-Version
    /// </summary>
    public string? RemoteChecksum { get; set; }

    /// <summary>
    /// Gespeicherte Checksum der heruntergeladenen BZ2-Datei
    /// </summary>
    public string? StoredBz2Checksum { get; set; }

    /// <summary>
    /// Ist ein Update verfügbar?
    /// </summary>
    public bool UpdateAvailable => !string.IsNullOrEmpty(RemoteChecksum)
                                   && !string.IsNullOrEmpty(StoredBz2Checksum)
                                   && !RemoteChecksum.Equals(StoredBz2Checksum, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Ist die lokale Version zu alt?
    /// </summary>
    public bool IsOutdated { get; set; }

    /// <summary>
    /// Ist die SDE gültig und nutzbar?
    /// </summary>
    public bool IsValid => Exists && !IsOutdated;

    /// <summary>
    /// Alter der lokalen Datei in Tagen
    /// </summary>
    public int? AgeDays => LocalFileDate.HasValue
        ? (int)(DateTime.Now - LocalFileDate.Value).TotalDays
        : null;

    /// <summary>
    /// Zeitpunkt der letzten Online-Prüfung
    /// </summary>
    public DateTime? LastOnlineCheck { get; set; }

    /// <summary>
    /// Fehlermeldung falls vorhanden
    /// </summary>
    public string? ErrorMessage { get; set; }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1_073_741_824)
            return $"{bytes / 1_073_741_824.0:F1} GB";
        if (bytes >= 1_048_576)
            return $"{bytes / 1_048_576.0:F1} MB";
        if (bytes >= 1_024)
            return $"{bytes / 1_024.0:F1} KB";
        return $"{bytes} Bytes";
    }
}

/// <summary>
/// Fortschritt eines Downloads
/// </summary>
public class SdeDownloadProgress
{
    public long BytesDownloaded { get; set; }
    public long TotalBytes { get; set; }
    public int ProgressPercent => TotalBytes > 0 ? (int)(BytesDownloaded * 100 / TotalBytes) : 0;
    public string Status { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public bool HasError { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Informationen über ein Sonnensystem aus der SDE
/// </summary>
public class SolarSystemInfo
{
    public int SolarSystemId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string RegionName { get; set; } = string.Empty;
    public int RegionId { get; set; }
    public float Security { get; set; }

    public string SecurityClass => Security switch
    {
        >= 0.5f => "Highsec",
        >= 0.1f => "Lowsec",
        _ => "Nullsec"
    };
}

/// <summary>
/// Skill-Informationen aus SDE + ESI kombiniert
/// </summary>
public class SkillInfo
{
    public int SkillId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public int TrainedSkillLevel { get; set; }
    public long SkillPointsInSkill { get; set; }
    public int ActiveSkillLevel { get; set; }

    public string FormattedSkillPoints => SkillPointsInSkill.ToString("N0");
}