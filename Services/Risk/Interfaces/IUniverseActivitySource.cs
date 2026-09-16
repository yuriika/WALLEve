using WALLEve.Models.Esi.Universe;
using WALLEve.Services.Esi.Interfaces;

namespace WALLEve.Services.Risk.Interfaces;

/// <summary>
/// Schmale Quelle für System-Aktivität (ESU universe/system_jumps und
/// universe/system_kills) in Dictionary-Form, damit die Risikoerhebung
/// keine ESI-Authentifizierung und keinen Paginierungs-Apparat braucht.
/// null = Quelle nicht verfügbar; ein leeres Dictionary = valide Daten.
/// </summary>
public interface IUniverseActivitySource
{
    /// <summary>System-ID → Sprunganzahl (letzte Stunde); null = Quelle nicht verfügbar.</summary>
    Task<IReadOnlyDictionary<int, int>?> GetJumpsBySystemAsync(CancellationToken ct = default);

    /// <summary>System-ID → Killanzahl (letzte Stunde); null = Quelle nicht verfügbar.</summary>
    Task<IReadOnlyDictionary<int, int>?> GetKillsBySystemAsync(CancellationToken ct = default);
}

/// <summary>
/// <see cref="IUniverseActivitySource"/> über den bestehenden
/// <see cref="IEsiApiService"/>: je Routenaufruf genau ein ESI-Request pro
/// Endpunkt (kein N+1), die Antwort deckt alle Systeme ab.
/// </summary>
public sealed class EsiUniverseActivitySource : IUniverseActivitySource
{
    private readonly IEsiApiService _esi;

    public EsiUniverseActivitySource(IEsiApiService esi)
    {
        _esi = esi;
    }

    public async Task<IReadOnlyDictionary<int, int>?> GetJumpsBySystemAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var jumps = await _esi.GetSystemJumpsAsync();
        if (jumps == null)
        {
            return null;
        }

        return jumps
            .GroupBy(j => j.SystemId)
            .ToDictionary(g => g.Key, g => g.Sum(j => j.ShipJumps));
    }

    public async Task<IReadOnlyDictionary<int, int>?> GetKillsBySystemAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var kills = await _esi.GetSystemKillsAsync();
        if (kills == null)
        {
            return null;
        }

        return kills
            .GroupBy(k => k.SystemId)
            .ToDictionary(g => g.Key, g => g.Sum(k => k.ShipKills));
    }
}