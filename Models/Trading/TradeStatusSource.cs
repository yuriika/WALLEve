namespace WALLEve.Models.Trading;

/// <summary>
/// Quelle einer Statusänderung einer Trading-Empfehlung (Issue #45):
/// <see cref="User"/> = manuelle Markierung durch den Nutzer (als Nutzerangabe
/// gekennzeichnet), <see cref="System"/> = automatischer Ablauf/Invalidierung.
/// Eine automatische Ausführung wird nie behauptet: "executed" ist nur als
/// Nutzerangabe zulässig.
/// </summary>
public enum TradeStatusSource
{
    /// <summary>Manuelle Markierung durch den Nutzer (Nutzerangabe).</summary>
    User = 0,

    /// <summary>Automatische Markierung durch Ablauf/Invalidierung.</summary>
    System = 1
}