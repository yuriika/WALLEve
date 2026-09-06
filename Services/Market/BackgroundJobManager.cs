using Microsoft.EntityFrameworkCore;
using WALLEve.Data;
using WALLEve.Models.Database;
using WALLEve.Services.Market.Interfaces;

namespace WALLEve.Services.Market;

public class BackgroundJobManager : IBackgroundJobManager
{
    private readonly WalletDbContext _db;

    public BackgroundJobManager(WalletDbContext db)
    {
        _db = db;
    }

    public async Task<BackgroundJob> CreateJobAsync(string jobType, string displayName, int? characterId,
        int total = 0, string? parametersJson = null)
    {
        var now = DateTime.UtcNow;
        var job = new BackgroundJob
        {
            JobType = jobType,
            DisplayName = displayName,
            CharacterId = characterId,
            Status = BackgroundJobStatus.Running,
            Current = 0,
            Total = total,
            ParametersJson = parametersJson,
            StartedAt = now,
            UpdatedAt = now
        };
        _db.BackgroundJobs.Add(job);
        await _db.SaveChangesAsync();
        return job;
    }

    public async Task<BackgroundJob?> GetJobAsync(long jobId)
        => await _db.BackgroundJobs.FindAsync(jobId);

    public async Task UpdateProgressAsync(long jobId, int current, int? total = null)
    {
        var job = await _db.BackgroundJobs.FindAsync(jobId);
        if (job == null) return;
        job.Current = current;
        if (total.HasValue) job.Total = total.Value;
        job.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task MarkCompletedAsync(long jobId)
    {
        var job = await _db.BackgroundJobs.FindAsync(jobId);
        if (job == null) return;
        job.Status = BackgroundJobStatus.Completed;
        job.CompletedAt = DateTime.UtcNow;
        job.UpdatedAt = DateTime.UtcNow;
        job.Current = job.Total > 0 ? job.Total : job.Current;
        await _db.SaveChangesAsync();
    }

    public async Task MarkFailedAsync(long jobId, string error)
    {
        var job = await _db.BackgroundJobs.FindAsync(jobId);
        if (job == null) return;
        job.Status = BackgroundJobStatus.Failed;
        job.LastError = error;
        job.CompletedAt = DateTime.UtcNow;
        job.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task PauseAsync(long jobId)
    {
        var job = await _db.BackgroundJobs.FindAsync(jobId);
        if (job == null || job.Status != BackgroundJobStatus.Running) return;
        job.Status = BackgroundJobStatus.Paused;
        job.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task RestartAsync(long jobId)
    {
        var job = await _db.BackgroundJobs.FindAsync(jobId);
        if (job == null) return;
        job.Status = BackgroundJobStatus.Running;
        job.Current = 0;
        job.LastError = null;
        job.StartedAt = DateTime.UtcNow;
        job.CompletedAt = null;
        job.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task<List<BackgroundJob>> GetJobsAsync(int limit = 50)
        => await _db.BackgroundJobs
            .OrderByDescending(j => j.UpdatedAt)
            .Take(limit)
            .ToListAsync();
}