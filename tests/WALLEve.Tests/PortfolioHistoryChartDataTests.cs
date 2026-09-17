using WALLEve.Components.Portfolio;
using WALLEve.Models.Portfolio;

namespace WALLEve.Tests;

/// <summary>
/// Regression-Tests für die Chart-Geometrie der Portfolio-Seite (Issue #47,
/// Review #159, Blocker: UI/Chart-Fehlteile). Nur die reine, deterministische
/// Geometrie wird getestet; ohne Live-ESI und ohne SDE-Datei.
/// <list type="bullet">
/// <item>AC #47-1: Lücken bleiben sichtbar — fehlende Bewertung wird nie
/// interpoliert, der SVG-Pfad bricht an der Lücke.</item>
/// <item>AC #47-2: keine erfundenen Werte — null bleibt null (Y null, kein
/// Pfadsegment), Unknown-Mengen werden separat ausgewiesen.</item>
/// <item>Buckets bleiben getrennt: Assets- und Escrow-Serie werden nie
/// vermischt oder summiert.</item>
/// </list>
/// </summary>
public class PortfolioHistoryChartDataTests
{
    private const double W = 900;
    private const double H = 240;
    private static readonly DateTime T0 = new(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);

    private static PortfolioHistoryPoint Point(long id, DateTime? at = null, double? assets = null, double? escrow = null)
        => new()
        {
            Id = id,
            HoldingSnapshotId = id + 1000,
            OwnerType = WALLEve.Models.Holdings.OwnerType.Character,
            OwnerId = 90073315,
            CapturedAt = at ?? T0,
            AssetsValue = assets,
            EscrowValue = escrow
        };

    [Fact]
    public void Build_ReturnsPointsSortedByCapturedAt()
    {
        var input = new[]
        {
            Point(3, T0.AddHours(2), assets: 30),
            Point(1, T0, assets: 10),
            Point(2, T0.AddHours(1), assets: 20)
        };

        var series = PortfolioHistoryChartData.Build(input, W, H);

        Assert.Equal(new[] { 1L, 2L, 3L }, series.Points.Select(p => p.PointId));
        Assert.True(series.Points.Select(p => p.CapturedAt).SequenceEqual(series.Points.Select(p => p.CapturedAt).OrderBy(d => d)));
    }

    [Fact]
    public void Build_KeepsGapsVisible_NoInterpolationOverMissingValue()
    {
        // 10 -> null -> 30: der Pfad muss an der Lücke brechen (zwei "M"),
        // es darf kein "L" über die fehlende Bewertung laufen.
        var input = new[]
        {
            Point(1, T0, assets: 10),
            Point(2, T0.AddHours(1), assets: null),
            Point(3, T0.AddHours(2), assets: 30)
        };

        var series = PortfolioHistoryChartData.Build(input, W, H);

        Assert.Equal(1, series.AssetsGapCount);
        Assert.True(series.HasAssetsGaps);
        Assert.Equal(2, CountOccurrences(series.AssetsPath, "M "));
        Assert.Equal(0, CountOccurrences(series.AssetsPath, " L "));
        Assert.Null(series.Points[1].AssetsY); // null bleibt null — nichts erfunden
    }

    [Fact]
    public void Build_TrailingMissingValue_IsNotCountedAsGap()
    {
        var input = new[]
        {
            Point(1, T0, assets: 10),
            Point(2, T0.AddHours(1), assets: null)
        };

        var series = PortfolioHistoryChartData.Build(input, W, H);

        Assert.Equal(0, series.AssetsGapCount);
        Assert.True(series.HasAssetsData);
    }

    [Fact]
    public void Build_AllMissingValues_ProducesEmptyPathsWithoutCrash()
    {
        var input = new[]
        {
            Point(1, T0, assets: null, escrow: null),
            Point(2, T0.AddHours(1), assets: null, escrow: null)
        };

        var series = PortfolioHistoryChartData.Build(input, W, H);

        Assert.False(series.HasAssetsData);
        Assert.False(series.HasEscrowData);
        Assert.Equal(string.Empty, series.AssetsPath);
        Assert.Equal(string.Empty, series.EscrowPath);
        Assert.All(series.Points, p => Assert.Null(p.AssetsY));
    }

    [Fact]
    public void Build_ScalesValuesIntoChartBounds()
    {
        var input = new[]
        {
            Point(1, T0, assets: 10),
            Point(2, T0.AddHours(1), assets: 30)
        };

        var series = PortfolioHistoryChartData.Build(input, W, H);

        Assert.Equal(10, series.MinValue);
        Assert.Equal(30, series.MaxValue);
        Assert.Equal(PortfolioHistoryChartData.Padding, series.Points[1].AssetsY!.Value, 3); // max -> oben
        Assert.Equal(H - PortfolioHistoryChartData.Padding, series.Points[0].AssetsY!.Value, 3); // min -> unten
        Assert.Equal(PortfolioHistoryChartData.Padding, series.Points[0].X, 3);
        Assert.Equal(W - PortfolioHistoryChartData.Padding, series.Points[1].X, 3);
    }

    [Fact]
    public void Build_SinglePoint_IsCentered()
    {
        var series = PortfolioHistoryChartData.Build(new[] { Point(1, T0, assets: 42.5) }, W, H);

        Assert.Equal(W / 2, series.Points[0].X, 3);
        Assert.Equal(H / 2, series.Points[0].AssetsY!.Value, 3);
    }

    [Fact]
    public void Build_FormatsCoordinatesInvariant_IndependentOfServerCulture()
    {
        // Werte 0 / 33.5 / 100 -> mittlerer Y-Wert 8 + (1 - 0.335) * 224 = 156.96:
        // der Pfad muss den Dezimalpunkt enthalten, auch unter de-DE-Serverkultur.
        var input = new[]
        {
            Point(1, T0, assets: 0),
            Point(2, T0.AddHours(1), assets: 33.5),
            Point(3, T0.AddHours(2), assets: 100)
        };

        var series = PortfolioHistoryChartData.Build(input, W, H);

        Assert.Contains("156.96", series.AssetsPath);
        Assert.DoesNotContain("156,96", series.AssetsPath);
    }

    [Fact]
    public void Build_KeepsEscrowSeriesSeparate_NeverMixedWithAssets()
    {
        var input = new[]
        {
            Point(1, T0, escrow: 50),
            Point(2, T0.AddHours(1), assets: 100, escrow: 60)
        };

        var series = PortfolioHistoryChartData.Build(input, W, H);

        // Punkt 1 hat keinen Asset-Wert — es wird keiner erfunden.
        Assert.Null(series.Points[0].AssetsY);
        Assert.True(series.HasEscrowData);
        Assert.NotNull(series.Points[1].AssetsY);
        Assert.NotNull(series.Points[1].EscrowY);
        // Getrennte Serien: Asset- und Escrow-Pfad sind unterschiedlich.
        Assert.NotEqual(series.AssetsPath, series.EscrowPath);
    }

    [Fact]
    public void Build_IsDeterministic()
    {
        var input = new[]
        {
            Point(2, T0.AddHours(1), assets: 20),
            Point(1, T0, assets: 10)
        };

        var a = PortfolioHistoryChartData.Build(input, W, H);
        var b = PortfolioHistoryChartData.Build(input, W, H);

        Assert.Equal(a.AssetsPath, b.AssetsPath);
        Assert.Equal(a.EscrowPath, b.EscrowPath);
    }

    private static int CountOccurrences(string haystack, string needle)
        => haystack.Split(needle, StringSplitOptions.None).Length - 1;
}