namespace WALLEve.Models.Portfolio;

/// <summary>
/// Ergebnis der Auswertung EINES vollständigen Holdings-Snapshots (Issue #38):
/// der persistierte, eingefrorene <see cref="PortfolioHistoryPoint"/> plus die
/// deterministisch aus den eingefrorenen Eingaben (Snapshot, Bewertungsregion,
/// CapturedAt) abgeleiteten Dimensions-Projektionen Ort und Kategorie.
///
/// Alle Mengen der Projektionen sind mengengleich zum Snapshot: jedes Item
/// erscheint genau einmal — entweder im freien Bestand oder im Escrow-Bucket
/// (keine Doppelzählung, AC #38-2).
/// </summary>
public sealed class PortfolioHistoryEvaluation
{
    /// <summary>Eingefrorener Punkt (persistiert, idempotent je Quell-Snapshot).</summary>
    public PortfolioHistoryPoint Point { get; init; } = null!;

    /// <summary>Orts-Projektion: eine Zeile je (LocationId, LocationFlag) mit
    /// getrennten Mengen/Werten für freien Bestand und Escrow.</summary>
    public IReadOnlyList<PortfolioLocationValue> Locations { get; init; } = Array.Empty<PortfolioLocationValue>();

    /// <summary>Kategorie-Projektion: eine Zeile je Kategorie-Label mit
    /// getrennten Mengen/Werten für freien Bestand und Escrow.</summary>
    public IReadOnlyList<PortfolioCategoryValue> Categories { get; init; } = Array.Empty<PortfolioCategoryValue>();
}