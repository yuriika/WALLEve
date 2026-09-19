using WALLEve.Services.Market.Interfaces;

namespace WALLEve.Services.Market;

/// <summary>
/// Prozessweiter, zustandsloser Auslöser für UI-Read-Models zu BackgroundJobs.
/// </summary>
public sealed class BackgroundJobStatusNotifier : IBackgroundJobStatusNotifier
{
    public event Action? StatusChanged;

    public void NotifyStatusChanged()
    {
        StatusChanged?.Invoke();
    }
}
