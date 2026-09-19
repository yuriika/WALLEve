using Microsoft.EntityFrameworkCore;
using WALLEve.Data;
using WALLEve.Models.Database;
using WALLEve.Services.Holdings.Interfaces;
using WALLEve.Services.Market.Interfaces;

namespace WALLEve.Services.Holdings;

/// <summary>
/// Führt vollständige Asset-Snapshots als sichtbare, persistierte Hintergrundjobs aus.
/// Ein erfolgreicher Lauf erzeugt über <see cref="IHoldingsSyncService"/> zugleich
/// einen Holdings-Snapshot und einen eingefrorenen Portfolio-Historienpunkt.
/// </summary>
public sealed class HoldingsSyncExecutor
{
    public const string JobType = "HoldingsSync";
    private const string DisplayName = "Bestand und Portfolio synchronisieren";
    internal static readonly TimeSpan IdleInterval = TimeSpan.FromHours(24);

    private readonly WalletDbContext _db;
    private readonly IBackgroundJobManager _jobManager;
    private readonly ISyncTriggerService _triggers;
    private readonly IHoldingsSyncService _sync;
    private readonly ILogger<HoldingsSyncExecutor> _logger;

    public HoldingsSyncExecutor(WalletDbContext db, IBackgroundJobManager jobManager,
        ISyncTriggerService triggers, IHoldingsSyncService sync,
        ILogger<HoldingsSyncExecutor> logger)
    {
        _db = db;
        _jobManager = jobManager;
        _triggers = triggers;
        _sync = sync;
        _logger = logger;
    }

    public async Task RunAsync(int characterId, CancellationToken ct)
    {
        var active = await _db.BackgroundJobs
            .Where(j => j.JobType == JobType && j.CharacterId == characterId
                && (j.Status == BackgroundJobStatus.Running || j.Status == BackgroundJobStatus.Interrupted || j.Status == BackgroundJobStatus.Paused))
            .OrderByDescending(j => j.UpdatedAt)
            .FirstOrDefaultAsync(ct);
        if (active?.Status == BackgroundJobStatus.Paused) return;
        if (active is not null)
        {
            await RunCoreAsync(active, characterId, ct);
            return;
        }

        var forced = await _triggers.ConsumeForceAsync(characterId, JobType);
        var lastCompleted = await _db.BackgroundJobs
            .Where(j => j.JobType == JobType && j.CharacterId == characterId && j.Status == BackgroundJobStatus.Completed)
            .OrderByDescending(j => j.CompletedAt)
            .Select(j => j.CompletedAt)
            .FirstOrDefaultAsync(ct);
        if (!forced && lastCompleted.HasValue && DateTime.UtcNow - lastCompleted.Value < IdleInterval) return;

        var job = await _jobManager.CreateJobAsync(JobType, DisplayName, characterId, total: 1);
        await RunCoreAsync(job, characterId, ct);
    }

    private async Task RunCoreAsync(BackgroundJob job, int characterId, CancellationToken ct)
    {
        try
        {
            var run = await _sync.SynchronizeAsync(characterId, ct);
            if (run.Status != "completed")
            {
                await _jobManager.MarkFailedAsync(job.Id, run.Error ?? "Bestands-Sync lieferte kein vollständiges Ergebnis.");
                return;
            }

            await _jobManager.UpdateProgressAsync(job.Id, 1, 1);
            await _jobManager.MarkCompletedAsync(job.Id);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Holdings sync failed (job {JobId})", job.Id);
            await _jobManager.MarkFailedAsync(job.Id, ex.Message);
        }
    }
}
