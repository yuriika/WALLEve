namespace WALLEve.Services.Market.Interfaces;

/// <summary>
/// Verteilt Änderungen persistierter BackgroundJobs an verbundene Blazor-Circuits.
/// Der Notifier enthält keinen Jobzustand; Empfänger laden ihre jeweilige Read-Model-
/// Ansicht bei einem Ereignis erneut und melden sich beim Dispose wieder ab.
/// </summary>
public interface IBackgroundJobStatusNotifier
{
    event Action? StatusChanged;

    void NotifyStatusChanged();
}
