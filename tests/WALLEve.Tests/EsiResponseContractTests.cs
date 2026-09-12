using WALLEve.Models.Esi;

namespace WALLEve.Tests;

/// <summary>
/// Tests für den vereinbarten Vollständigkeits- und Fehlervertrag von
/// <see cref="EsiResponse{T}"/>: Transportstatus und Datenqualität sind
/// getrennt; Defaults dürfen nie als vollständiges Ergebnis gelesen werden.
/// Reine Modell-Tests ohne ESI/HTTP.
/// </summary>
public class EsiResponseContractTests
{
    private static EsiResponse<List<int>> Fresh(int statusCode, List<int>? data = null)
        => new() { StatusCode = statusCode, Data = data, FetchedAt = DateTime.UtcNow };

    // ------------------------------------------------------------------
    // Datenqualität: complete-empty / complete-data / partial / stale / failed
    // ------------------------------------------------------------------

    [Fact]
    public void DefaultInstance_IsUnknown_NotComplete()
    {
        var r = new EsiResponse<List<int>>();

        Assert.Equal(EsiCompleteness.Unknown, r.Completeness);
        Assert.Equal(EsiErrorCategory.Unknown, r.ErrorCategory);
        Assert.False(r.IsSuccess);
        Assert.False(r.IsNotModified);
    }

    [Fact]
    public void Http200_WithoutData_IsCompleteEmpty()
    {
        var r = Fresh(200, null);

        Assert.Equal(EsiCompleteness.CompleteEmpty, r.Completeness);
        Assert.Equal(EsiErrorCategory.None, r.ErrorCategory);
    }

    [Fact]
    public void Http200_WithData_IsCompleteData()
    {
        var r = Fresh(200, new List<int> { 1, 2 });

        Assert.Equal(EsiCompleteness.CompleteData, r.Completeness);
    }

    [Fact]
    public void Http200_FewerPagesFetchedThanTotal_IsPartial()
    {
        var r = Fresh(200, new List<int> { 1 });
        r.TotalPages = 5;
        r.PagesFetched = 3;

        Assert.Equal(EsiCompleteness.Partial, r.Completeness);
    }

    [Fact]
    public void Http200_CurrentPageBelowTotalPages_IsPartial()
    {
        var r = Fresh(200, new List<int> { 1 });
        r.TotalPages = 4;
        r.Page = 2;

        Assert.Equal(EsiCompleteness.Partial, r.Completeness);
    }

    [Fact]
    public void Http200_AllPagesFetched_IsCompleteData()
    {
        var r = Fresh(200, new List<int> { 1 });
        r.TotalPages = 5;
        r.PagesFetched = 5;

        Assert.Equal(EsiCompleteness.CompleteData, r.Completeness);

        var single = Fresh(200, new List<int> { 1 });
        single.TotalPages = 1;

        Assert.Equal(EsiCompleteness.CompleteData, single.Completeness);
    }

    [Fact]
    public void Http304_WithCachedData_IsStale_NotEmptySuccess()
    {
        var r = Fresh(304, new List<int> { 1 });

        // 304 referenziert vorhandene Daten und ist kein leerer Erfolg
        Assert.Equal(EsiCompleteness.Stale, r.Completeness);
        Assert.Equal(EsiErrorCategory.None, r.ErrorCategory);
        Assert.NotEqual(EsiCompleteness.CompleteEmpty, r.Completeness);
        Assert.NotEqual(EsiCompleteness.Failed, r.Completeness);
    }

    [Fact]
    public void Http304_WithoutCache_IsFailed()
    {
        var r = Fresh(304, null);

        Assert.Equal(EsiCompleteness.Failed, r.Completeness);
    }

    [Fact]
    public void Http500_WithoutData_IsFailed()
    {
        var r = Fresh(500, null);

        Assert.Equal(EsiCompleteness.Failed, r.Completeness);
        Assert.Equal(EsiErrorCategory.ServerError, r.ErrorCategory);
    }

    [Fact]
    public void Http500_WithOldSnapshot_IsStale_ErrorAccompaniesSnapshot()
    {
        var r = Fresh(500, new List<int> { 1 });

        // Transportstatus und Datenqualität getrennt: der Fehler begleitet
        // einen vorhandenen, aber stale Snapshot.
        Assert.Equal(EsiErrorCategory.ServerError, r.ErrorCategory);
        Assert.Equal(EsiCompleteness.Stale, r.Completeness);
    }

    // ------------------------------------------------------------------
    // Fehlerkategorie: vereinbartes Mapping der Fixture-Statuscodes
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(200, EsiErrorCategory.None)]
    [InlineData(304, EsiErrorCategory.None)]
    [InlineData(401, EsiErrorCategory.AuthenticationFailed)]
    [InlineData(403, EsiErrorCategory.AuthenticationFailed)]
    [InlineData(404, EsiErrorCategory.NotFound)]
    [InlineData(420, EsiErrorCategory.RateLimited)]
    [InlineData(429, EsiErrorCategory.RateLimited)]
    [InlineData(500, EsiErrorCategory.ServerError)]
    [InlineData(502, EsiErrorCategory.ServerError)]
    [InlineData(503, EsiErrorCategory.ServerError)]
    public void StatusCode_MapsToErrorCategory(int statusCode, EsiErrorCategory expected)
    {
        var r = new EsiResponse<List<int>> { StatusCode = statusCode };

        Assert.Equal(expected, r.ErrorCategory);
    }

    [Fact]
    public void Cancelled_WithoutData_IsFailed()
    {
        var r = Fresh(200, null);
        r.ErrorCategory = EsiErrorCategory.Cancelled;

        Assert.Equal(EsiCompleteness.Failed, r.Completeness);
    }

    [Fact]
    public void Cancelled_WithSnapshot_IsStale()
    {
        var r = Fresh(200, new List<int> { 1 });
        r.ErrorCategory = EsiErrorCategory.Cancelled;

        Assert.Equal(EsiErrorCategory.Cancelled, r.ErrorCategory);
        Assert.Equal(EsiCompleteness.Stale, r.Completeness);
    }

    [Fact]
    public void MalformedPayload_IsFailed_NotCompleteEmpty()
    {
        // 2xx-Status, aber Payload unlesbar → kein (leeres) Erfolgs-Ergebnis
        var r = Fresh(200, null);
        r.ErrorCategory = EsiErrorCategory.Malformed;

        Assert.Equal(EsiCompleteness.Failed, r.Completeness);
        Assert.NotEqual(EsiCompleteness.CompleteEmpty, r.Completeness);
    }

    // ------------------------------------------------------------------
    // Metadaten: Datenzeit und Ablauf gehen bei der Abbildung nicht verloren
    // ------------------------------------------------------------------

    [Fact]
    public void Metadata_ExpiresAndFetchedAt_ArePreserved()
    {
        var expires = DateTime.UtcNow.AddHours(1);
        var fetchedAt = DateTime.UtcNow.AddMinutes(-5);

        var r = new EsiResponse<List<int>>
        {
            StatusCode = 200,
            Data = new List<int> { 1 },
            ETag = "abc",
            Expires = expires,
            FetchedAt = fetchedAt
        };

        Assert.Equal(expires, r.Expires);
        Assert.Equal(fetchedAt, r.FetchedAt);
        Assert.Equal("abc", r.ETag);
        Assert.Equal(EsiCompleteness.CompleteData, r.Completeness);
        Assert.Equal(EsiErrorCategory.None, r.ErrorCategory);
    }
}