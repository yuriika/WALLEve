using System.Threading.Channels;
using WALLEve.Services.Market.Interfaces;

namespace WALLEve.Services.Market;

/// <summary>
/// Weckt den Hintergrund-Collektor sofort aus seinem Warte-Takt auf, wenn ein
/// Sync manuell angestoßen wird — statt bis zum nächsten 60s-Timer zu warten.
/// Singleton; Push-basiert (SignalR-Circuit übernimmt das Live-Update der UI).
/// </summary>
public class SyncWakeService : ISyncWakeService
{
    private readonly Channel<bool> _channel = Channel.CreateUnbounded<bool>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });

    public void Signal()
    {
        _channel.Writer.TryWrite(true);
    }

    /// <summary>Wartet, bis ein Signal eintrifft (oder der Token abgebrochen wird).</summary>
    public Task<bool> WaitForWorkAsync(CancellationToken ct) => _channel.Reader.WaitToReadAsync(ct).AsTask();

    /// <summary>Konsumiert ein Signal (nachdem es geweckt hat).</summary>
    public void Consume() => _channel.Reader.TryRead(out _);
}