namespace WALLEve.Models.Trading;

/// <summary>
/// Eine getrennt bewertete Rang-Dimension eines Kandidaten (Issue #54):
/// Wert, Einheit, Gewicht und die deterministische Erklärung stammen
/// ausschließlich aus den Eingaben und Annahmen der Bewertung.
/// </summary>
/// <param name="Name">Dimensionsname (Deutsch, UI-konform).</param>
/// <param name="Value">Dimensionswert; null = nicht berechenbar (z. B. fehlende Zeitannahme).</param>
/// <param name="Unit">Einheit des Werts („ISK", „%", „ISK-Tage", „Score 0-1").</param>
/// <param name="Weight">Gewicht dieser Dimension in der Gesamtbewertung (Summe aktiver Gewichte = 1).</param>
/// <param name="HigherIsBetter">true = höherer Wert ist besser fürs Ranking, false = umgekehrt.</param>
/// <param name="Explanation">Begründung (Deutsch), deterministisch aus Eingaben/Annahmen gebildet.</param>
public sealed record TradeRankingDimension(
    string Name,
    double? Value,
    string Unit,
    double Weight,
    bool HigherIsBetter,
    string Explanation);