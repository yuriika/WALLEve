using System.Reflection;
using MainLayout = WALLEve.Components.Layout.MainLayout;
using AccountSwitcher = WALLEve.Components.Shared.AccountSwitcher;
using LogoutButton = WALLEve.Components.Shared.LogoutButton;
using Stockpiles = WALLEve.Components.Pages.Stockpiles;
using CostBasis = WALLEve.Components.Pages.CostBasis;
using Character = WALLEve.Components.Pages.Character;
using TradingPage = WALLEve.Components.Pages.Trading;
using TradingActionCard = WALLEve.Components.Trading.TradingActionCard;
using TradingNotificationCenter = WALLEve.Components.Trading.TradingNotificationCenter;

namespace WALLEve.Tests;

/// <summary>
/// Lifecycle-Vertrag der Komponenten (#70, #181): Jede Komponente mit Dispose()
/// muss auch die entsprechende Schnittstelle implementieren, damit Blazor die
/// Bereinigung tatsächlich aufruft. Reflection-Tests statt bUnit, weil das
/// Repository kein bUnit verwendet.
/// </summary>
public class TradingComponentDisposalTests
{
    [Theory]
    [InlineData(typeof(MainLayout))]
    [InlineData(typeof(AccountSwitcher))]
    [InlineData(typeof(LogoutButton))]
    [InlineData(typeof(Stockpiles))]
    [InlineData(typeof(CostBasis))]
    [InlineData(typeof(Character))]
    [InlineData(typeof(TradingPage))]
    [InlineData(typeof(TradingNotificationCenter))]
    public void Components_ImplementIDisposable_SoBlazorCallsDispose(Type componentType)
    {
        Assert.True(
            typeof(IDisposable).IsAssignableFrom(componentType),
            $"{componentType.FullName} muss IDisposable implementieren, sonst ruft Blazor Dispose() nie auf.");

        var dispose = componentType.GetMethod(nameof(IDisposable.Dispose), BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(dispose);
        Assert.Equal(typeof(void), dispose!.ReturnType);
        Assert.Empty(dispose.GetParameters());
    }

    [Fact]
    public void TradingActionCard_ImplementsIAsyncDisposable_SoBlazorReleasesJsModule()
    {
        var type = typeof(TradingActionCard);

        Assert.True(
            typeof(IAsyncDisposable).IsAssignableFrom(type),
            $"{type.FullName} muss IAsyncDisposable implementieren, sonst gibt Blazor das JS-Modul nicht frei.");

        var disposeAsync = type.GetMethod(nameof(IAsyncDisposable.DisposeAsync), BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(disposeAsync);
        Assert.Equal(typeof(ValueTask), disposeAsync!.ReturnType);
        Assert.Empty(disposeAsync.GetParameters());
    }
}
