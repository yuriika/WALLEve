namespace WALLEve.Models.Trading;

/// <summary>
/// Ein Füllzeit-Szenario der Rang-Bewertung (Issue #54): konservativ/
/// realistisch/optimistisch. Die angenommene Füllzeit und die daraus
/// resultierende geschätzte ISK/Stunde sind explizit Szenario-Werte —
/// es wird keine exakte Füllzeit behauptet.
/// </summary>
/// <param name="Name">Szenarioname („Konservativ", „Realistisch", „Optimistisch").</param>
/// <param name="FillTimeMultiplier">Faktor, mit dem die Basis-Füllzeit skaliert wird.</param>
/// <param name="AssumedFillHours">Angenommene Füllzeit in Stunden; null = keine Zeitannahme (nicht berechenbar).</param>
/// <param name="IskPerHour">Geschätzte ISK/Stunde in diesem Szenario; null = nicht berechenbar.</param>
public sealed record TradeRankingScenario(
    string Name,
    double FillTimeMultiplier,
    double? AssumedFillHours,
    double? IskPerHour);