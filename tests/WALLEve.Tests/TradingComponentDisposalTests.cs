using System.Reflection;
using TradingPage = WALLEve.Components.Pages.Trading;
using TradingNotificationCenter = WALLEve.Components.Trading.TradingNotificationCenter;

namespace WALLEve.Tests;

/// <summary>
/// Lifecycle-Vertrag der Trading-Komponenten (#70): Beide definieren <c>Dispose()</c>
/// für ihre Polling-Schleifen, werden aber nur dann von Blazor verworfen, wenn die
/// Komponentenklasse <see cref="IDisposable"/> tatsächlich implementiert. Fehlt die
/// Schnittstelle, laufen die Polling-Loops nach Navigation/Disconnect weiter und
/// halten Circuits fest. Das Repository hat kein bUnit; dieser Reflection-Test pinnt
/// deshalb den Vertrag direkt am kompilierten Komponententyp.
/// </summary>
public class TradingComponentDisposalTests
{
    [Theory]
    [InlineData(typeof(TradingPage))]
    [InlineData(typeof(TradingNotificationCenter))]
    public void TradingComponents_ImplementIDisposable_SoBlazorCancelsPolling(Type componentType)
    {
        Assert.True(
            typeof(IDisposable).IsAssignableFrom(componentType),
            $"{componentType.FullName} muss IDisposable implementieren, sonst ruft Blazor Dispose() nie auf.");

        var dispose = componentType.GetMethod(nameof(IDisposable.Dispose), BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(dispose);
        Assert.Equal(typeof(void), dispose!.ReturnType);
        Assert.Empty(dispose.GetParameters());
    }
}
