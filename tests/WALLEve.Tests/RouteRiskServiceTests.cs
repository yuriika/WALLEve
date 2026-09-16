using Microsoft.Extensions.Logging.Abstractions;
using WALLEve.Models.Risk;
using WALLEve.Services.Risk;
using WALLEve.Services.Risk.Interfaces;

namespace WALLEve.Tests;

/// <summary>
/// Tests für die Routen-Risikoerhebung (#73): konservatives Unknown bei fehlendem
/// Verlustnachweis (niemals automatisch „sicher“), getrennte Evidence je Quelle,
/// UnavailableSources und Durchhaltefähigkeit der Navigation bei Provider-Fehlern.
/// Keine Live-ESI-/zKillboard-Abhängigkeit.
/// </summary>
public class RouteRiskServiceTests
{
    private const int SystemA = 30005001;
    private const int SystemB = 30005002;

    // ------------------------------------------------------------------
    // Fakes
    // ------------------------------------------------------------------

    private sealed class FakeActivitySource : IUniverseActivitySource
    {
        public IReadOnlyDictionary<int, int>? Jumps { get; set; }
        public IReadOnlyDictionary<int, int>? Kills { get; set; }
        public bool Throw { get; set; }

        public Task<IReadOnlyDictionary<int, int>?> GetJumpsBySystemAsync(CancellationToken ct = default)
        {
            if (Throw)
            {
                throw new InvalidOperationException("ESI down");
            }
            return Task.FromResult(Jumps);
        }

        public Task<IReadOnlyDictionary<int, int>?> GetKillsBySystemAsync(CancellationToken ct = default)
        {
            if (Throw)
            {
                throw new InvalidOperationException("ESI down");
            }
            return Task.FromResult(Kills);
        }
    }

    private sealed class FakeZkillboardClient : IZkillboardClient
    {
        public bool IsEnabled { get; set; } = true;
        public Dictionary<int, ZkillboardLosses?> Results { get; } = new();
        public bool Throw { get; set; }

        public Task<ZkillboardLosses?> GetLossesAsync(int systemId, CancellationToken ct = default)
        {
            if (Throw)
            {
                throw new InvalidOperationException("zKillboard down");
            }
            return Task.FromResult(Results.GetValueOrDefault(systemId));
        }
    }

    private static ZkillboardLosses Losses(int systemId, int count)
        => new()
        {
            SystemId = systemId,
            Losses = count,
            CollectedAt = DateTimeOffset.UtcNow,
            IsDelayed = true
        };

    private static RouteRiskService CreateService(
        FakeActivitySource activity,
        FakeZkillboardClient zkb)
        => new(activity, zkb, NullLogger<RouteRiskService>.Instance);

    // ------------------------------------------------------------------
    // Fehlender Verlustnachweis → Unknown, nie „sicher“
    // ------------------------------------------------------------------

    [Fact]
    public async Task Collect_NoLossEvidence_ReturnsUnknownNotSafe()
    {
        // zKillboard deaktiviert (optionale Quelle): keine Verlustnachweise.
        var activity = new FakeActivitySource
        {
            Jumps = new Dictionary<int, int> { [SystemA] = 5, [SystemB] = 3 },
            Kills = new Dictionary<int, int> { [SystemA] = 1, [SystemB] = 0 }
        };
        var zkb = new FakeZkillboardClient { IsEnabled = false };
        var service = CreateService(activity, zkb);

        var summary = await service.CollectRouteRiskAsync(new[] { SystemA, SystemB });

        // ESI-Daten allein klassifizieren nicht; fehlende Verlustnachweise → Unknown.
        Assert.Equal(RiskLevel.Unknown, summary.RouteLevel);
        // ESI wurde erfolgreich erhoben → Erhebungszeitpunkt vorhanden.
        Assert.NotNull(summary.LastCollectedAt);
        Assert.Contains(RiskEvidenceSource.Zkillboard, summary.UnavailableSources);
        Assert.DoesNotContain(RiskEvidenceSource.EsiJumpsKills, summary.UnavailableSources);
        // ESI-Evidence ist verfügbar (getrennte Führung), die zKillboard-Evidence nicht.
        Assert.All(summary.BySystem.Values, evidence =>
        {
            Assert.Contains(evidence, e => e.Source == RiskEvidenceSource.EsiJumpsKills && e.IsAvailable);
            Assert.Contains(evidence, e => e.Source == RiskEvidenceSource.Zkillboard && !e.IsAvailable);
        });
    }

    [Fact]
    public async Task Collect_AllSourcesUnavailable_ReturnsUnknownWithSources()
    {
        var activity = new FakeActivitySource { Jumps = null, Kills = null };
        var zkb = new FakeZkillboardClient();
        var service = CreateService(activity, zkb);

        var summary = await service.CollectRouteRiskAsync(new[] { SystemA });

        Assert.Equal(RiskLevel.Unknown, summary.RouteLevel);
        Assert.Null(summary.LastCollectedAt);
        Assert.Contains(RiskEvidenceSource.EsiJumpsKills, summary.UnavailableSources);
        Assert.Contains(RiskEvidenceSource.Zkillboard, summary.UnavailableSources);
        Assert.All(summary.BySystem[SystemA], e => Assert.False(e.IsAvailable));
    }

    // ------------------------------------------------------------------
    // Vorhandene Verlustnachweise → abgestufte Stufen
    // ------------------------------------------------------------------

    [Fact]
    public async Task Collect_ZeroLossesWithActivity_IsLowNotUnknown()
    {
        var activity = new FakeActivitySource
        {
            Jumps = new Dictionary<int, int> { [SystemA] = 2 },
            Kills = new Dictionary<int, int> { [SystemA] = 0 }
        };
        var zkb = new FakeZkillboardClient
        {
            Results = { [SystemA] = Losses(SystemA, 0) }
        };
        var service = CreateService(activity, zkb);

        var summary = await service.CollectRouteRiskAsync(new[] { SystemA });

        // Verlustnachweis vorhanden (0 Verluste) → System bekannt, Stufe Niedrig.
        Assert.Equal(RiskLevel.Low, summary.RouteLevel);
        Assert.NotNull(summary.LastCollectedAt);
        Assert.Empty(summary.UnavailableSources);
    }

    [Fact]
    public async Task Collect_LossesElevateLevel()
    {
        var activity = new FakeActivitySource
        {
            Jumps = new Dictionary<int, int> { [SystemA] = 1 },
            Kills = new Dictionary<int, int> { [SystemA] = 0 }
        };
        var zkb = new FakeZkillboardClient
        {
            Results = { [SystemA] = Losses(SystemA, 1) }
        };
        var service = CreateService(activity, zkb);

        var summary = await service.CollectRouteRiskAsync(new[] { SystemA });

        Assert.Equal(RiskLevel.Elevated, summary.RouteLevel);
    }

    [Fact]
    public async Task Collect_HighLosses_AreHigh()
    {
        var activity = new FakeActivitySource
        {
            Jumps = new Dictionary<int, int> { [SystemA] = 0 },
            Kills = new Dictionary<int, int> { [SystemA] = 0 }
        };
        var zkb = new FakeZkillboardClient
        {
            Results = { [SystemA] = Losses(SystemA, 12) }
        };
        var service = CreateService(activity, zkb);

        var summary = await service.CollectRouteRiskAsync(new[] { SystemA });

        Assert.Equal(RiskLevel.High, summary.RouteLevel);
    }

    [Fact]
    public async Task Collect_EsiActivityContributesWhenLossEvidenceExists()
    {
        var activity = new FakeActivitySource
        {
            Jumps = new Dictionary<int, int> { [SystemA] = 30 },
            Kills = new Dictionary<int, int> { [SystemA] = 0 }
        };
        var zkb = new FakeZkillboardClient
        {
            Results = { [SystemA] = Losses(SystemA, 0) }
        };
        var service = CreateService(activity, zkb);

        var summary = await service.CollectRouteRiskAsync(new[] { SystemA });

        // Hohes Sprungaufkommen hebt die Stufe trotz 0 Verlusten an.
        Assert.Equal(RiskLevel.Elevated, summary.RouteLevel);
    }

    // ------------------------------------------------------------------
    // Provider-Ausfälle: Navigation bleibt funktionsfähig
    // ------------------------------------------------------------------

    [Fact]
    public async Task Collect_ThrowingProviders_ReturnUnknownWithoutThrowing()
    {
        var activity = new FakeActivitySource { Throw = true };
        var zkb = new FakeZkillboardClient { Throw = true };
        var service = CreateService(activity, zkb);

        var summary = await service.CollectRouteRiskAsync(new[] { SystemA, SystemB });

        // Keine Exception: Route/Analyse bleibt funktionsfähig, Lage konservativ unbekannt.
        Assert.Equal(RiskLevel.Unknown, summary.RouteLevel);
        Assert.Contains(RiskEvidenceSource.EsiJumpsKills, summary.UnavailableSources);
        Assert.Contains(RiskEvidenceSource.Zkillboard, summary.UnavailableSources);
    }

    [Fact]
    public async Task Collect_OneUnknownSystem_KeepsRouteUnknown()
    {
        var activity = new FakeActivitySource
        {
            Jumps = new Dictionary<int, int> { [SystemA] = 0, [SystemB] = 0 },
            Kills = new Dictionary<int, int> { [SystemA] = 0, [SystemB] = 0 }
        };
        // System A hat Verlustnachweis, System B nicht (Adapter liefert null).
        var zkb = new FakeZkillboardClient
        {
            Results = { [SystemA] = Losses(SystemA, 0) }
        };
        var service = CreateService(activity, zkb);

        var summary = await service.CollectRouteRiskAsync(new[] { SystemA, SystemB });

        // Ein System ohne Verlustnachweis macht die gesamte Route konservativ unbekannt.
        Assert.Equal(RiskLevel.Unknown, summary.RouteLevel);
        Assert.Contains(RiskEvidenceSource.Zkillboard, summary.UnavailableSources);
    }

    [Fact]
    public async Task Collect_EsiFailsButLossEvidenceExists_LevelFromLosses()
    {
        var activity = new FakeActivitySource { Jumps = null, Kills = null };
        var zkb = new FakeZkillboardClient
        {
            Results = { [SystemA] = Losses(SystemA, 0) }
        };
        var service = CreateService(activity, zkb);

        var summary = await service.CollectRouteRiskAsync(new[] { SystemA });

        // Verlustnachweis vorhanden → System bekannt; ESI-Ausfall nur Quelle in Unavailable.
        Assert.Equal(RiskLevel.Low, summary.RouteLevel);
        Assert.Contains(RiskEvidenceSource.EsiJumpsKills, summary.UnavailableSources);
        Assert.DoesNotContain(RiskEvidenceSource.Zkillboard, summary.UnavailableSources);
        Assert.NotNull(summary.LastCollectedAt);
    }
}