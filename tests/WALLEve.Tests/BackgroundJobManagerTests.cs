using WALLEve.Models.Database;
using WALLEve.Services.Market;

namespace WALLEve.Tests;

/// <summary>
/// Tests für den persistierten Job-Zustand (Status-Übergänge, Fortschritt,
/// Pause/Resume/Neustart) — Grundlage der Hintergrund-Task-Übersicht.
/// </summary>
public class BackgroundJobManagerTests
{
    [Fact]
    public async Task CreateJob_StartsRunningWithZeroProgress()
    {
        using var db = TestDb.Create();
        var manager = new BackgroundJobManager(db);

        var job = await manager.CreateJobAsync("CostBasisEstimate", "Schätzen", 42, total: 10);

        Assert.True(job.Id > 0);
        Assert.Equal(BackgroundJobStatus.Running, job.Status);
        Assert.Equal(0, job.Current);
        Assert.Equal(10, job.Total);
        Assert.Equal("CostBasisEstimate", job.JobType);
    }

    [Fact]
    public async Task UpdateProgress_SetsCurrentAndTotal()
    {
        using var db = TestDb.Create();
        var manager = new BackgroundJobManager(db);
        var job = await manager.CreateJobAsync("CostBasisDeduction", "Ableitung", 42, total: 50);

        await manager.UpdateProgressAsync(job.Id, 23, 50);

        var reloaded = await manager.GetJobAsync(job.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(23, reloaded!.Current);
        Assert.Equal(50, reloaded.Total);
    }

    [Fact]
    public async Task MarkCompleted_ClosesJobWithTimestamp()
    {
        using var db = TestDb.Create();
        var manager = new BackgroundJobManager(db);
        var job = await manager.CreateJobAsync("CostBasisSink", "Sink", 42, total: 5);

        await manager.MarkCompletedAsync(job.Id);

        var reloaded = await manager.GetJobAsync(job.Id);
        Assert.Equal(BackgroundJobStatus.Completed, reloaded!.Status);
        Assert.NotNull(reloaded.CompletedAt);
        Assert.Equal(5, reloaded.Current); // Current wird auf Total gesetzt
    }

    [Fact]
    public async Task MarkFailed_StoresErrorMessage()
    {
        using var db = TestDb.Create();
        var manager = new BackgroundJobManager(db);
        var job = await manager.CreateJobAsync("CostBasisEstimate", "Schätzen", 42);

        await manager.MarkFailedAsync(job.Id, "ESI 420: Error Limited");

        var reloaded = await manager.GetJobAsync(job.Id);
        Assert.Equal(BackgroundJobStatus.Failed, reloaded!.Status);
        Assert.Equal("ESI 420: Error Limited", reloaded.LastError);
    }

    [Fact]
    public async Task Pause_TogglesRunningToPaused()
    {
        using var db = TestDb.Create();
        var manager = new BackgroundJobManager(db);
        var job = await manager.CreateJobAsync("CostBasisDeduction", "Ableitung", 42);

        await manager.PauseAsync(job.Id);

        var reloaded = await manager.GetJobAsync(job.Id);
        Assert.Equal(BackgroundJobStatus.Paused, reloaded!.Status);
    }

    [Fact]
    public async Task Pause_DoesNotAffectCompletedJob()
    {
        using var db = TestDb.Create();
        var manager = new BackgroundJobManager(db);
        var job = await manager.CreateJobAsync("CostBasisDeduction", "Ableitung", 42);
        await manager.MarkCompletedAsync(job.Id);

        await manager.PauseAsync(job.Id); // darf nichts ändern

        var reloaded = await manager.GetJobAsync(job.Id);
        Assert.Equal(BackgroundJobStatus.Completed, reloaded!.Status);
    }

    [Fact]
    public async Task Restart_ResetsProgressAndError()
    {
        using var db = TestDb.Create();
        var manager = new BackgroundJobManager(db);
        var job = await manager.CreateJobAsync("CostBasisDeduction", "Ableitung", 42, total: 100);
        await manager.UpdateProgressAsync(job.Id, 60, 100);
        await manager.MarkFailedAsync(job.Id, "kaputt");

        await manager.RestartAsync(job.Id);

        var reloaded = await manager.GetJobAsync(job.Id);
        Assert.Equal(BackgroundJobStatus.Running, reloaded!.Status);
        Assert.Equal(0, reloaded.Current);
        Assert.Null(reloaded.LastError);
    }

    [Fact]
    public async Task GetJobs_ReturnsNewestFirst()
    {
        using var db = TestDb.Create();
        var manager = new BackgroundJobManager(db);
        await manager.CreateJobAsync("CostBasisSink", "Sink 1", 42);
        await Task.Delay(10);
        var second = await manager.CreateJobAsync("CostBasisEstimate", "Schätzen 2", 42);

        var jobs = await manager.GetJobsAsync();

        Assert.Equal(2, jobs.Count);
        Assert.Equal(second.Id, jobs[0].Id); // neuester zuerst
    }
}