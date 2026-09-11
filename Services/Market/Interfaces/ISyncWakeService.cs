namespace WALLEve.Services.Market.Interfaces;

/// <summary>
/// Push-basierte Aktivierung des Hintergrund-Collektors: Ein manueller Sync-Trigger
/// weckt den (Singleton-)Collektor sofort auf, statt bis zum nächsten 60s-Timer zu warten.
/// </summary>
public interface ISyncWakeService
{
    /// <summary>Signalisieren, dass sofort Arbeit ansteht (nicht blockierend).</summary>
    void Signal();

    /// <summary>Wartet, bis ein Signal eintrifft oder der Token abgebrochen wird.</summary>
    Task<bool> WaitForWorkAsync(CancellationToken ct);

    /// <summary>Konsumiert ein Signal nach dem Aufwachen.</summary>
    void Consume();
}