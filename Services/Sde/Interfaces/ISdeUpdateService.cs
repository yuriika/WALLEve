using WALLEve.Models.Sde;

namespace WALLEve.Services.Sde.Interfaces;

/// <summary>
/// Service für SDE-Download und Update-Verwaltung
/// </summary>
public interface ISdeUpdateService
{
    /// <summary>
    /// Prüft den Status der lokalen SDE
    /// </summary>
    Task<SdeStatus> GetStatusAsync(bool checkOnline = false);

    /// <summary>
    /// Lädt die SDE herunter und entpackt sie
    /// </summary>
    Task DownloadAsync(IProgress<SdeDownloadProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Prüft ob ein Startup-Redirect zur Settings-Seite nötig ist
    /// </summary>
    Task<bool> RequiresSetupAsync();

    /// <summary>
    /// Event das bei Status-Änderungen ausgelöst wird
    /// </summary>
    event EventHandler<SdeStatus>? StatusChanged;
}