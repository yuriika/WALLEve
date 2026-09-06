using WALLEve.Models.Database;

namespace WALLEve.Services.Market.Interfaces;

/// <summary>
/// Verwaltet persistierte Hintergrund-Tasks (BackgroundJobs).
/// Status und Fortschritt überleben App-Neustarts; interrupted Jobs können
/// an der abgebrochenen Stelle fortgesetzt werden.
/// </summary>
public interface IBackgroundJobManager
{
    /// <summary>Legt einen neuen Job an und liefert ihn zurück.</summary>
    Task<BackgroundJob> CreateJobAsync(string jobType, string displayName, int? characterId,
        int total = 0, string? parametersJson = null);

    /// <summary>Holt den aktuellen Job-Zustand.</summary>
    Task<BackgroundJob?> GetJobAsync(long jobId);

    /// <summary>Setzt Fortschritt (Current/Total) und aktualisiert UpdatedAt.</summary>
    Task UpdateProgressAsync(long jobId, int current, int? total = null);

    Task MarkCompletedAsync(long jobId);
    Task MarkFailedAsync(long jobId, string error);

    /// <summary>Setzt einen Job auf Paused (wird vom Ausführer respektiert).</summary>
    Task PauseAsync(long jobId);

    /// <summary>Setzt einen abgeschlossenen/interruptierten Job zurück auf einen Neustart (Current=0).</summary>
    Task RestartAsync(long jobId);

    /// <summary>Alle Jobs, neueste zuerst — für die Settings-Übersicht.</summary>
    Task<List<BackgroundJob>> GetJobsAsync(int limit = 50);
}