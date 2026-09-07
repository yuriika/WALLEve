using WALLEve.Services.Market;

namespace WALLEve.Tests;

/// <summary>
/// Test für die Push-Aktivierung des Collektors (Wake statt Warte-Rhythmus).
/// </summary>
public class SyncWakeServiceTests
{
    [Fact]
    public async Task Signal_WakesWaitingLoop_Immediately()
    {
        var wake = new SyncWakeService();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var waitTask = wake.WaitForWorkAsync(cts.Token);
        await Task.Delay(50);
        Assert.False(waitTask.IsCompleted); // wartet noch (kein Signal bisher)

        wake.Signal();
        Assert.True(await waitTask); // sofort geweckt
    }

    [Fact]
    public async Task Consume_TakesOneSignal_NextWaitNeedsNewSignal()
    {
        var wake = new SyncWakeService();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        wake.Signal();
        Assert.True(await wake.WaitForWorkAsync(cts.Token)); // konsumiert
        wake.Consume();

        // Danach kein Signal mehr offen → nächste Warte darf nicht sofort feuern.
        using var shortCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var running = wake.WaitForWorkAsync(shortCts.Token);
        Assert.False(running.IsCompleted); // wartet weiter (kein sofortiges Signal)
    }
}