using WALLEve.Models.Map;
using WALLEve.Services.Map;
using WALLEve.Services.Map.Interfaces;

namespace WALLEve.Tests;

/// <summary>
/// Tests für den Routen-Flow der Karte (#72, Review-Fix): Der Flow konsumiert den
/// gemeinsamen Routenvertrag (IRouteCalculationService) über RoutePlanState —
/// effektiver Ursprung (Charakterposition vs. manuell), Validierung ohne
/// Service-Aufruf und Invalidierung der aktiven Route bei Ursprungs-/Ziel-/
/// Präferenzwechsel („Charakterwechsel invalidiert Ursprung“).
/// </summary>
public class RoutePlanStateTests
{
    private sealed class FakeRouteService : IRouteCalculationService
    {
        public List<(int Origin, int Destination, RoutingPreference Preference)> Calls { get; } = new();

        public RouteResult Result { get; set; } = new()
        {
            Success = true,
            Path = new List<int> { 1, 2 },
            Systems = new List<MapSolarSystemNode>
            {
                new() { SolarSystemId = 1, Name = "Alpha", Security = 0.9f },
                new() { SolarSystemId = 2, Name = "Beta", Security = 0.6f }
            },
            TotalJumps = 1,
            AverageSecurity = 0.75f,
            HighSecJumps = 1
        };

        public Task<RouteResult> CalculateRouteLocalAsync(int originId, int destinationId, RoutingPreference preference)
        {
            Calls.Add((originId, destinationId, preference));
            return Task.FromResult(Result);
        }

        public Task<RouteResult> CalculateRouteEsiAsync(int originId, int destinationId, RoutingPreference preference)
            => Task.FromResult(new RouteResult { Success = false, Error = "not implemented" });

        public Task<RouteComparisonResult> CalculateRouteComparisonAsync(int originId, int destinationId, RoutingPreference preference)
            => Task.FromResult(new RouteComparisonResult());
    }

    // ------------------------------------------------------------------
    // Effektiver Ursprung
    // ------------------------------------------------------------------

    [Fact]
    public void EffectiveOrigin_UsesCharacterPositionByDefault()
    {
        var state = new RoutePlanState();
        state.UpdateCharacterOrigin(1);
        state.SetManualOrigin(2);

        Assert.Equal(1, state.EffectiveOriginId);
        Assert.True(state.HasOrigin);
        Assert.True(state.UseCharacterOrigin);
    }

    [Fact]
    public void EffectiveOrigin_ManualOverridesWhenCharacterOriginDisabled()
    {
        var state = new RoutePlanState();
        state.UpdateCharacterOrigin(1);
        state.SetManualOrigin(2);
        state.SetUseCharacterOrigin(false);

        Assert.Equal(2, state.EffectiveOriginId);
    }

    // ------------------------------------------------------------------
    // Berechnung und Charakterwechsel-Invalidierung (Akzeptanzkriterium)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Calculate_UsesCurrentEffectiveOrigin_AndCharacterSwitchInvalidatesRoute()
    {
        var service = new FakeRouteService();
        var state = new RoutePlanState();
        state.UpdateCharacterOrigin(1);
        state.SetDestination(3);

        var first = await state.CalculateAsync(service);

        Assert.True(first.Success);
        Assert.Same(service.Result, state.ActiveRoute);
        Assert.Equal((1, 3, RoutingPreference.Safer), service.Calls[0]);

        // Charakterwechsel: Ursprung invalidiert, aktive Route wird verworfen.
        state.UpdateCharacterOrigin(7);
        Assert.Null(state.ActiveRoute);

        var second = await state.CalculateAsync(service);
        Assert.True(second.Success);
        Assert.Equal((7, 3, RoutingPreference.Safer), service.Calls[1]);
    }

    [Fact]
    public async Task Calculate_ManualOriginChange_InvalidatesRoute()
    {
        var service = new FakeRouteService();
        var state = new RoutePlanState();
        state.SetManualOrigin(2);
        state.SetUseCharacterOrigin(false);
        state.SetDestination(3);

        await state.CalculateAsync(service);
        Assert.NotNull(state.ActiveRoute);

        state.SetManualOrigin(9);
        Assert.Null(state.ActiveRoute);
    }

    [Fact]
    public async Task Calculate_DestinationChange_InvalidatesRoute()
    {
        var service = new FakeRouteService();
        var state = new RoutePlanState();
        state.UpdateCharacterOrigin(1);
        state.SetDestination(3);

        await state.CalculateAsync(service);
        Assert.NotNull(state.ActiveRoute);

        state.SetDestination(4);
        Assert.Null(state.ActiveRoute);
    }

    [Fact]
    public async Task Calculate_PreferenceChange_InvalidatesRoute()
    {
        var service = new FakeRouteService();
        var state = new RoutePlanState();
        state.UpdateCharacterOrigin(1);
        state.SetDestination(3);

        await state.CalculateAsync(service);
        Assert.NotNull(state.ActiveRoute);

        state.SetPreference(RoutingPreference.Shorter);
        Assert.Null(state.ActiveRoute);
    }

    // ------------------------------------------------------------------
    // Validierung ohne Service-Aufruf
    // ------------------------------------------------------------------

    [Fact]
    public async Task Calculate_MissingOrigin_ReturnsErrorWithoutServiceCall()
    {
        var service = new FakeRouteService();
        var state = new RoutePlanState();
        state.SetDestination(3);

        var result = await state.CalculateAsync(service);

        Assert.False(result.Success);
        Assert.Equal(state.Error, result.Error);
        Assert.Null(state.ActiveRoute);
        Assert.Empty(service.Calls);
    }

    [Fact]
    public async Task Calculate_MissingDestination_ReturnsErrorWithoutServiceCall()
    {
        var service = new FakeRouteService();
        var state = new RoutePlanState();
        state.UpdateCharacterOrigin(1);

        var result = await state.CalculateAsync(service);

        Assert.False(result.Success);
        Assert.Equal(state.Error, result.Error);
        Assert.Null(state.ActiveRoute);
        Assert.Empty(service.Calls);
    }

    // ------------------------------------------------------------------
    // Fehler-Durchreichung des Vertrags
    // ------------------------------------------------------------------

    [Fact]
    public async Task Calculate_ServiceFailure_IsPassedThrough()
    {
        var service = new FakeRouteService
        {
            Result = new RouteResult { Success = false, Error = "Kein Pfad gefunden" }
        };
        var state = new RoutePlanState();
        state.UpdateCharacterOrigin(1);
        state.SetDestination(3);

        var result = await state.CalculateAsync(service);

        Assert.False(result.Success);
        Assert.False(state.ActiveRoute?.Success);
        Assert.Equal("Kein Pfad gefunden", state.ActiveRoute?.Error);
        Assert.Equal((1, 3, RoutingPreference.Safer), service.Calls[0]);
    }
}