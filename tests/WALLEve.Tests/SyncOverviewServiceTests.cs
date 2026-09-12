using Microsoft.EntityFrameworkCore;
using WALLEve.Data;
using WALLEve.Models.Database;
using WALLEve.Services.Market;
using WALLEve.Services.Market.Interfaces;

namespace WALLEve.Tests;

/// <summary>
/// Tests für die Sync-Übersicht auf der Character-Seite.
/// </summary>
public class SyncOverviewServiceTests
{
    private const int CharacterId = 90073315;

    private static (SyncOverviewService Service, WalletDbContext Db) CreateSut()
    {
        var db = TestDb.Create();
        return (new SyncOverviewService(db), db);
    }

    [Fact]
    public async Task NoRunsYet_ReturnsAllKnownSyncs_AsNeverRun()
    {
        var (service, _) = CreateSut();

        var syncs = await service.GetSyncOverviewAsync(CharacterId);

        Assert.Equal(4, syncs.Count);
        Assert.All(syncs, s =>
        {
            Assert.False(string.IsNullOrEmpty(s.Name));
            Assert.False(string.IsNullOrEmpty(s.Description));
            Assert.Null(s.LastCompletedAt);
            Assert.Null(s.ActiveStatus);
        });
        Assert.Contains(syncs, s => s.JobType == "CostBasisSink");
        Assert.Contains(syncs, s => s.JobType == "CostBasisDeduction");
        Assert.Contains(syncs, s => s.JobType == "CostBasisEstimate");
        Assert.Contains(syncs, s => s.JobType == "InventoryScan");
    }

    [Fact]
    public async Task CompletedJob_ShowsLastRunAndProgress()
    {
        var (service, db) = CreateSut();
        var completed = new BackgroundJob
        {
            JobType = "CostBasisSink",
            DisplayName = "Wallet-Transaktionen spiegeln",
            CharacterId = CharacterId,
            Status = BackgroundJobStatus.Completed,
            Current = 1,
            Total = 1,
            StartedAt = DateTime.UtcNow.AddHours(-2),
            CompletedAt = DateTime.UtcNow.AddHours(-1),
            UpdatedAt = DateTime.UtcNow.AddHours(-1)
        };
        db.BackgroundJobs.Add(completed);
        await db.SaveChangesAsync();

        var syncs = await service.GetSyncOverviewAsync(CharacterId);

        var sink = syncs.First(s => s.JobType == "CostBasisSink");
        Assert.NotNull(sink.LastCompletedAt);
        Assert.Equal(1, sink.LastCurrent);
        Assert.Equal(1, sink.LastTotal);
        Assert.Null(sink.ActiveStatus);

        // Andere Syncs des Chars bleiben "nie gelaufen"
        Assert.All(syncs.Where(s => s.JobType != "CostBasisSink"),
            s => Assert.Null(s.LastCompletedAt));
    }

    [Fact]
    public async Task RunningJob_ShowsActiveStatus()
    {
        var (service, db) = CreateSut();
        db.BackgroundJobs.Add(new BackgroundJob
        {
            JobType = "InventoryScan",
            DisplayName = "Komplett-Scan",
            CharacterId = CharacterId,
            Status = BackgroundJobStatus.Running,
            Current = 12,
            Total = 548,
            StartedAt = DateTime.UtcNow.AddMinutes(-5),
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var syncs = await service.GetSyncOverviewAsync(CharacterId);

        var scan = syncs.First(s => s.JobType == "InventoryScan");
        Assert.Equal(BackgroundJobStatus.Running, scan.ActiveStatus);
    }

    [Fact]
    public async Task OtherCharactersJobs_AreNotCounted()
    {
        var (service, db) = CreateSut();
        db.BackgroundJobs.Add(new BackgroundJob
        {
            JobType = "CostBasisSink",
            DisplayName = "Wallet-Transaktionen spiegeln",
            CharacterId = 999, // anderer Char
            Status = BackgroundJobStatus.Completed,
            Current = 1,
            Total = 1,
            StartedAt = DateTime.UtcNow.AddHours(-2),
            CompletedAt = DateTime.UtcNow.AddHours(-1),
            UpdatedAt = DateTime.UtcNow.AddHours(-1)
        });
        await db.SaveChangesAsync();

        var syncs = await service.GetSyncOverviewAsync(CharacterId);

        Assert.Null(syncs.First(s => s.JobType == "CostBasisSink").LastCompletedAt);
    }

    [Fact]
    public async Task LastFailedJob_ExposesErrorAndTime_WhileLastSuccessStaysVisible()
    {
        var (service, db) = CreateSut();

        // Erfolg vor 2 Tagen
        db.BackgroundJobs.Add(new BackgroundJob
        {
            JobType = "CostBasisSink",
            DisplayName = "Wallet-Transaktionen spiegeln",
            CharacterId = CharacterId,
            Status = BackgroundJobStatus.Completed,
            Current = 1,
            Total = 1,
            StartedAt = DateTime.UtcNow.AddDays(-2),
            CompletedAt = DateTime.UtcNow.AddDays(-2),
            UpdatedAt = DateTime.UtcNow.AddDays(-2)
        });

        // Fehlschlag vor 1 Stunde mit LastError (stale: Fehler + Alter sichtbar)
        db.BackgroundJobs.Add(new BackgroundJob
        {
            JobType = "CostBasisSink",
            DisplayName = "Wallet-Transaktionen spiegeln",
            CharacterId = CharacterId,
            Status = BackgroundJobStatus.Failed,
            LastError = "ESI-Abruf fehlgeschlagen — vorheriger Stand bleibt erhalten",
            Current = 0,
            Total = 1,
            StartedAt = DateTime.UtcNow.AddHours(-2),
            CompletedAt = DateTime.UtcNow.AddHours(-1),
            UpdatedAt = DateTime.UtcNow.AddHours(-1)
        });
        await db.SaveChangesAsync();

        var syncs = await service.GetSyncOverviewAsync(CharacterId);

        var sink = syncs.First(s => s.JobType == "CostBasisSink");
        Assert.NotNull(sink.LastError);
        Assert.Equal("ESI-Abruf fehlgeschlagen — vorheriger Stand bleibt erhalten", sink.LastError);
        Assert.NotNull(sink.LastFailedAt);
        // Der letzte erfolgreiche Abschluss bleibt weiterhin sichtbar
        Assert.NotNull(sink.LastCompletedAt);
        Assert.Null(sink.ActiveStatus); // kein laufender Zustand
    }
}