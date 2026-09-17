using System.Globalization;
using WALLEve.Models.Portfolio;

namespace WALLEve.Components.Portfolio;

/// <summary>
/// Reine, testbare Chart-Geometrie für den Portfolio-Wertverlauf (Issue #47).
///
/// Disziplin: Die Wert-Buckets bleiben getrennt — der Verlauf zeichnet den
/// freien Bestand (AssetsValue) und den in Sell-Orders gebundenen Bestand
/// (EscrowValue) als zwei eigene Serien mit je eigenen Lücken. Es wird nie
/// ein Wert erfunden: Fehlt zum Sync-Zeitpunkt ein As-of-Quote, bleibt die
/// Serie an dieser Stelle unterbrochen (Gap) statt interpoliert zu werden.
///
/// Alle Koordinaten werden invariant formatiert (Punkt als Dezimaltrenner),
/// damit die SVG-Ausgabe unabhängig von der Server-Kultur ist.
/// </summary>
public static class PortfolioHistoryChartData
{
    public const double Padding = 8;

    /// <summary>Ein Punkt der Serie mit berechneten Koordinaten. Y ist null,
    /// wenn für diesen Punkt kein Wert vorliegt (Lücke).</summary>
    public sealed record ChartPoint(
        long PointId,
        DateTime CapturedAt,
        double? AssetsValue,
        double? EscrowValue,
        double X,
        double? AssetsY,
        double? EscrowY);

    public sealed record ChartSeries(
        IReadOnlyList<ChartPoint> Points,
        double MinValue,
        double MaxValue,
        string AssetsPath,
        string EscrowPath,
        int AssetsGapCount,
        int EscrowGapCount,
        bool HasAssetsData,
        bool HasEscrowData)
    {
        /// <summary>Mehrere getrennte Teilpfade (Mindestens zwei "M") bedeuten
        /// eine sichtbare Lücke im Verlauf.</summary>
        public bool HasAssetsGaps => AssetsGapCount > 0;
        public bool HasEscrowGaps => EscrowGapCount > 0;
    }

    /// <summary>
    /// Baut aus den persistierten, eingefrorenen Punkten (Issue #38) die
    /// Chart-Serie: zeitlich sortiert, X über den Index, Y linear skaliert
    /// auf den gemeinsamen Min/Max beider Serien. Determineistisch und ohne
    /// Live-ESI — Eingaben sind nur die bereits persistierten Punkte.
    /// </summary>
    public static ChartSeries Build(IEnumerable<PortfolioHistoryPoint> points, double width, double height)
    {
        var ordered = points
            .OrderBy(p => p.CapturedAt)
            .ThenBy(p => p.Id)
            .ToList();

        var usableWidth = Math.Max(1, width - 2 * Padding);
        var usableHeight = Math.Max(1, height - 2 * Padding);

        var allValues = ordered
            .SelectMany(p => new double?[] { p.AssetsValue, p.EscrowValue })
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .ToList();

        double min, max;
        if (allValues.Count == 0)
        {
            min = 0;
            max = 1;
        }
        else
        {
            min = allValues.Min();
            max = allValues.Max();
        }

        var range = max - min;
        var constant = range < 0.0000001;
        // Kein erfundener Wert: Ein einzelner konstanter Wert wird vertikal
        // zentriert statt auf eine Linie gequetscht.
        var scale = constant ? 1 : range;

        double YOf(double value)
            => constant
                ? Padding + usableHeight / 2
                : Padding + (1 - (value - min) / scale) * usableHeight;

        double XOf(int index, int count, double usableWidth, double padding)
            => count <= 1 ? width / 2 : padding + usableWidth * index / (count - 1);

        var chartPoints = new List<ChartPoint>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            var p = ordered[i];
            chartPoints.Add(new ChartPoint(
                p.Id,
                p.CapturedAt,
                p.AssetsValue,
                p.EscrowValue,
                XOf(i, ordered.Count, usableWidth, Padding),
                p.AssetsValue.HasValue ? YOf(p.AssetsValue.Value) : null,
                p.EscrowValue.HasValue ? YOf(p.EscrowValue.Value) : null));
        }

        return new ChartSeries(
            chartPoints,
            min,
            max,
            BuildPath(chartPoints, p => p.AssetsY, out var assetsGaps),
            BuildPath(chartPoints, p => p.EscrowY, out var escrowGaps),
            assetsGaps,
            escrowGaps,
            chartPoints.Any(p => p.AssetsY.HasValue),
            chartPoints.Any(p => p.EscrowY.HasValue));
    }

    /// <summary>
    /// Baut den SVG-Pfad einer Serie. Bei fehlenden Werten (Y == null) wird
    /// der Pfad getrennt — es entsteht eine sichtbare Lücke statt einer
    /// Interpolation über fehlende Bewertung.
    /// </summary>
    private static string BuildPath(IReadOnlyList<ChartPoint> points,
        Func<ChartPoint, double?> ySelector, out int gapCount)
    {
        var sb = new System.Text.StringBuilder();
        var started = false;
        gapCount = 0;
        var lastHadValue = false;

        foreach (var p in points)
        {
            var y = ySelector(p);
            if (!y.HasValue)
            {
                if (started)
                {
                    // Lücke nach einem Wert: neuer Teilpfad beim nächsten Wert.
                    gapCount++;
                    started = false;
                }
                lastHadValue = false;
                continue;
            }

            var x = Fmt(p.X);
            var yv = Fmt(y.Value);
            if (!started)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append("M ").Append(x).Append(' ').Append(yv);
                started = true;
            }
            else
            {
                sb.Append(" L ").Append(x).Append(' ').Append(yv);
            }
            lastHadValue = true;
        }

        // Eine Lücke am Serienende (Wert -> null) ist kein sichtbarer Bruch
        // im Pfad, sondern nur ein fehlender Schluss — zählt nicht als Gap.
        if (gapCount > 0 && !lastHadValue) gapCount--;

        return sb.ToString();
    }

    private static string Fmt(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}