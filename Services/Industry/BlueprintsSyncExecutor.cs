using Microsoft.EntityFrameworkCore;
using WALLEve.Data;
using WALLEve.Models.Database;
using WALLEve.Services.Industry.Interfaces;
using WALLEve.Services.Market.Interfaces;

namespace WALLEve.Services.Industry;

/// <summary>
/// Führt den Blueprint-Sync als persistierten BackgroundJob aus — die
/// Collector-Anbindung von Issue #49. Stabile JobType-Konstante (Sync-Registry,
/// DEVELOPMENT.md §8): ein Lauf wird als BackgroundJob gespiegelt, unterbrochene
/// Läufe (App-Neustart → Status Interrupted) werden idempotent fortgesetzt,
/// pausierte Läufe respektiert (Cancel). Manuelle Auslösung über das
/// ForceRun-Flag von <see cref="ISyncTriggerService"/>, automatisch sonst
/// einmal pro <see cref="IdleInterval"/>.
/// </summary>
public class BlueprintsSyncExecutor
{
    /// <summary>Stabiler JobType für die Sync-Registry (UI-Übersicht + Doku).</summary>
    public const string JobType = "BlueprintsSync";

    private const string DisplayName = "Blueprint-Bestand synchronisieren";

    /// <summary>Mindestabstand zwischen zwei automatischen Läufen (wie CostBasisSink).</summary>
    internal static readonly TimeSpan IdleInterval = TimeSpan.FromHours(24);

    private readonly WalletDbContext _db;
    private readonly IBackgroundJobManager _jobManager;
    private readonly ISyncTriggerService _triggers;
    private readonly IBlueprintsSyncService _sync;
    private readonly ILogger<BlueprintsSyncExecutor> _logger;

    public BlueprintsSyncExecutor(
        WalletDbContext db,
        IBackgroundJobManager jobManager,
        ISyncTriggerService triggers,
        IBlueprintsSyncService sync,
        ILogger<BlueprintsSyncExecutor> logger)
    {
        _db = db;
        _jobManager = jobManager;
        _triggers = triggers;
        _sync = sync;
        _logger = logger;
    }

    /// <summary>
    /// Prüft den nächsten fälligen Blueprint-Sync für einen Character und
    /// führt ihn als BackgroundJob aus. Reihenfolge: zuerst ein noch aktiver
    /// Lauf (Running/Interrupted → Resume; Paused → Cancel), sonst nur bei
    /// manuellem Force-Flag oder abgelaufenem 24h-Intervall ein neuer Lauf.
    /// Der Sync selbst ist idempotent ((CharacterId, ItemId)-Dedup), daher ist
    /// ein Resume ein erneuter, konfliktfreier Lauf.
    /// </summary>
    public async Task RunAsync(int characterId, CancellationToken ct)
    {
        // 1. Aktiver oder fortzusetzender Lauf (App-Neustart hinterlässt Interrupted)
        var activeJob = await _db.BackgroundJobs
            .Where(j => j.JobType == JobType && j.CharacterId == characterId
                     && (j.Status == BackgroundJobStatus.Running
                      || j.Status == BackgroundJobStatus.Interrupted
                      || j.Status == BackgroundJobStatus.Paused))
            .OrderByDescending(j => j.UpdatedAt)
            .FirstOrDefaultAsync(ct);

        if (activeJob != null)
        {
            if (activeJob.Status == BackgroundJobStatus.Paused)
            {
                _logger.LogInformation("Blueprint sync: job {JobId} paused — skipping", activeJob.Id);
                return;
            }

            _logger.LogInformation("Blueprint sync: job {JobId} is {Status} — resuming",
                activeJob.Id, activeJob.Status);
            await RunCoreAsync(activeJob, characterId, ct);
            return;
        }

        // 2. Kein aktiver Lauf: nur bei manuellem Force-Flag oder fälligem Intervall
        var forced = await _triggers.ConsumeForceAsync(characterId, JobType);

        var lastCompleted = await _db.BackgroundJobs
            .Where(j => j.JobType == JobType && j.CharacterId == characterId
                     && j.Status == BackgroundJobStatus.Completed)
            .OrderByDescending(j => j.CompletedAt)
            .Select(j => j.CompletedAt)
            .FirstOrDefaultAsync(ct);

        var now = DateTime.UtcNow;
        if (!forced && lastCompleted.HasValue && now - lastCompleted.Value < IdleInterval)
        {
            _logger.LogInformation("Blueprint sync: last run {LastRun}, next run due {NextRun}",
                lastCompleted.Value, lastCompleted.Value + IdleInterval);
            return;
        }

        var job = await _jobManager.CreateJobAsync(JobType, DisplayName, characterId, total: 0);
        _logger.LogInformation("Blueprint sync: started (job {JobId})", job.Id);
        await RunCoreAsync(job, characterId, ct);
    }

    private async Task RunCoreAsync(BackgroundJob job, int characterId, CancellationToken ct)
    {
        try
        {
            var result = await _sync.SynchronizeAsync(characterId, ct);
            if (!result.Success)
            {
                await _jobManager.MarkFailedAsync(job.Id, result.Error ?? "Blueprint-Sync fehlgeschlagen.");
                return;
            }

            await _jobManager.UpdateProgressAsync(job.Id, result.Total, result.Total);
            await _jobManager.MarkCompletedAsync(job.Id);
            _logger.LogInformation(
                "Blueprint sync (job {JobId}) done: {Inserted} new, {Updated} updated, {Total} blueprints",
                job.Id, result.Inserted, result.Updated, result.Total);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown: Status bleibt Running — der nächste Start behandelt ihn
            // als Interrupted und setzt den Lauf idempotent fort (Resume).
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Blueprint sync failed (job {JobId})", job.Id);
            await _jobManager.MarkFailedAsync(job.Id, ex.Message);
        }
    }
}