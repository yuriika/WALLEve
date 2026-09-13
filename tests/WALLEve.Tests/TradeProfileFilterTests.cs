using WALLEve.Models.Database;
using WALLEve.Models.Trading;
using WALLEve.Services.Trading;

namespace WALLEve.Tests;

/// <summary>
/// Tests der harten Filterpipeline (Issue #44): Jede Grenze wird unter/an/
/// über dem Limit geprüft (Grenzen sind inklusiv), unbekannte Pflichtdaten
/// blockieren statt still durchzurutschen, Gewichte können abgelehnte
/// Kandidaten nicht wieder zulassen und Owner-Profile bleiben isoliert.
/// </summary>
public class TradeProfileFilterTests
{
    private const int OwnerId = 1001;

    private readonly TradeProfileFilter _filter = new();

    private static TradeProfile Profile(
        decimal? maxCapital = null,
        decimal? maxCargo = null,
        int? maxJumps = null,
        bool allowHighSec = true,
        bool allowLowSec = false,
        bool allowNullSec = false,
        decimal? minVolume = null,
        decimal? minProfit = null,
        int? minQuality = null,
        int characterId = OwnerId)
        => new()
        {
            CharacterId = characterId,
            Name = "Profil",
            AllowHighSec = allowHighSec,
            AllowLowSec = allowLowSec,
            AllowNullSec = allowNullSec,
            MaxCapital = maxCapital,
            MaxCargoVolume = maxCargo,
            MaxJumps = maxJumps,
            MinVolumeM3 = minVolume,
            MinProfit = minProfit,
            MinQualityScore = minQuality
        };

    private static TradingOpportunity Candidate(
        int id,
        double requiredCapital = 100_000,
        double estimatedProfit = 10_000,
        double score = 80,
        int? jumpDistance = 2,
        string? routeSecurity = "{\"highsec\": 10, \"lowsec\": 0, \"nullsec\": 0}",
        int characterId = OwnerId)
        => new()
        {
            Id = id,
            CharacterId = characterId,
            TypeId = 34,
            OpportunityType = "station_trading",
            RequiredCapital = requiredCapital,
            EstimatedProfit = estimatedProfit,
            Score = score,
            JumpDistance = jumpDistance,
            RouteSecurityAnalysis = routeSecurity,
            Status = "active",
            DetectedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(1),
            Provenance = TradingOpportunity.ProvenanceHeuristic
        };

    // --- Grenzen: unter/an/über dem Limit (inklusiv) ---

    [Fact]
    public void Capital_Boundary_BelowAtAboveLimit()
    {
        var profile = Profile(maxCapital: 1_000_000m);

        Assert.True(_filter.Evaluate(profile, Candidate(1, requiredCapital: 999_999)).Passed);
        Assert.True(_filter.Evaluate(profile, Candidate(2, requiredCapital: 1_000_000)).Passed,
            "Grenze ist inklusiv: exakt am Limit besteht der Kandidat.");
        Assert.False(_filter.Evaluate(profile, Candidate(3, requiredCapital: 1_000_001)).Passed);
    }

    [Fact]
    public void Cargo_Boundary_BelowAtAboveLimit()
    {
        var profile = Profile(maxCargo: 1_000m);

        Assert.True(_filter.Evaluate(profile, Candidate(1), volumeM3: 999).Passed);
        Assert.True(_filter.Evaluate(profile, Candidate(2), volumeM3: 1_000).Passed);
        Assert.False(_filter.Evaluate(profile, Candidate(3), volumeM3: 1_001).Passed);
    }

    [Fact]
    public void Jumps_Boundary_BelowAtAboveLimit()
    {
        var profile = Profile(maxJumps: 5);

        Assert.True(_filter.Evaluate(profile, Candidate(1, jumpDistance: 4)).Passed);
        Assert.True(_filter.Evaluate(profile, Candidate(2, jumpDistance: 5)).Passed);
        Assert.False(_filter.Evaluate(profile, Candidate(3, jumpDistance: 6)).Passed);
    }

    [Fact]
    public void MinVolume_Boundary_AboveAtBelowLimit()
    {
        var profile = Profile(minVolume: 10m);

        Assert.True(_filter.Evaluate(profile, Candidate(1), volumeM3: 11).Passed);
        Assert.True(_filter.Evaluate(profile, Candidate(2), volumeM3: 10).Passed);
        Assert.False(_filter.Evaluate(profile, Candidate(3), volumeM3: 9).Passed);
    }

    [Fact]
    public void MinProfit_Boundary_AboveAtBelowLimit()
    {
        var profile = Profile(minProfit: 5_000m);

        Assert.True(_filter.Evaluate(profile, Candidate(1, estimatedProfit: 5_001)).Passed);
        Assert.True(_filter.Evaluate(profile, Candidate(2, estimatedProfit: 5_000)).Passed);
        Assert.False(_filter.Evaluate(profile, Candidate(3, estimatedProfit: 4_999)).Passed);
    }

    [Fact]
    public void Quality_Boundary_AboveAtBelowLimit()
    {
        var profile = Profile(minQuality: 50);

        Assert.True(_filter.Evaluate(profile, Candidate(1, score: 51)).Passed);
        Assert.True(_filter.Evaluate(profile, Candidate(2, score: 50)).Passed);
        Assert.False(_filter.Evaluate(profile, Candidate(3, score: 49)).Passed);
    }

    [Fact]
    public void Security_DisallowedLowSecZone_RejectsWithZoneReason()
    {
        var profile = Profile(allowHighSec: true, allowLowSec: false, allowNullSec: false);
        var result = _filter.Evaluate(profile, Candidate(1, routeSecurity: "{\"highsec\": 5, \"lowsec\": 1, \"nullsec\": 0}"));

        Assert.False(result.Passed);
        Assert.Contains(result.RejectionReasons, r => r.Contains("lowsec", StringComparison.OrdinalIgnoreCase));
    }

    // --- unbekannte erforderliche Daten blockieren ---

    [Fact]
    public void UnknownCargoVolume_WithCargoLimit_Blocks()
    {
        var profile = Profile(maxCargo: 1_000m);
        var result = _filter.Evaluate(profile, Candidate(1), volumeM3: null);

        Assert.False(result.Passed);
        Assert.Contains(result.RejectionReasons, r => r.Contains("unbekannt", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UnknownJumpDistance_WithJumpLimit_Blocks()
    {
        var profile = Profile(maxJumps: 5);
        var result = _filter.Evaluate(profile, Candidate(1, jumpDistance: null));

        Assert.False(result.Passed);
        Assert.Contains(result.RejectionReasons, r => r.Contains("Sprungdistanz", StringComparison.Ordinal));
    }

    [Fact]
    public void UnknownRouteSecurity_WithRestrictedZones_Blocks()
    {
        var profile = Profile(allowHighSec: true, allowLowSec: false, allowNullSec: false);
        var result = _filter.Evaluate(profile, Candidate(1, routeSecurity: null));

        Assert.False(result.Passed);
        Assert.Contains(result.RejectionReasons, r => r.Contains("Sicherheitsanalyse", StringComparison.Ordinal));
    }

    [Fact]
    public void UnknownVolume_WithMinVolume_Blocks()
    {
        var profile = Profile(minVolume: 10m);
        var result = _filter.Evaluate(profile, Candidate(1), volumeM3: null);

        Assert.False(result.Passed);
        Assert.Contains(result.RejectionReasons, r => r.Contains("unbekannt", StringComparison.OrdinalIgnoreCase));
    }

    // --- Gewichte können abgelehnte Kandidaten nicht wieder zulassen ---

    [Fact]
    public void Weights_OnScore_CannotReAdmitRejectedCandidate()
    {
        var profile = Profile(minProfit: 5_000m);
        var rejected = Candidate(1, estimatedProfit: 100, score: 100); // höchster Score — jede Gewichtung würde ihn anheben
        var accepted = Candidate(2, estimatedProfit: 6_000, score: 10);

        var results = new[] { rejected, accepted }
            .Select(c => (_filter.Evaluate(profile, c, volumeM3: 1m), c))
            .ToList();

        // Ranking konsumiert ausschließlich Pipeline-Ergebnisse (Passed).
        var ranked = results
            .Where(x => x.Item1.Passed)
            .OrderByDescending(x => x.c.Score * 9999) // Gewicht verstärkt den abgelehnten Kandidaten
            .Select(x => x.c.Id)
            .ToList();

        Assert.False(results.Single(x => x.c.Id == 1).Item1.Passed);
        Assert.Equal(new[] { 2 }, ranked);
    }

    // --- Owner-Isolation ---

    [Fact]
    public void ForeignOwnerProfile_NeverEvaluatesCandidate()
    {
        var ownProfile = Profile(characterId: OwnerId, maxCapital: 1_000_000m);
        var foreignProfile = Profile(characterId: 9999, maxCapital: 1_000_000m); // identische Werte, fremder Owner

        Assert.True(_filter.Evaluate(ownProfile, Candidate(1, requiredCapital: 500_000)).Passed);
        Assert.False(_filter.Evaluate(foreignProfile, Candidate(2, requiredCapital: 500_000)).Passed);
    }

    [Fact]
    public void MultipleRejections_CollectAllIndividualReasons()
    {
        var profile = Profile(maxCapital: 100m, minProfit: 10_000m);
        var result = _filter.Evaluate(profile, Candidate(1, requiredCapital: 20_000, estimatedProfit: 500));

        Assert.False(result.Passed);
        Assert.Equal(2, result.RejectionReasons.Count);
        Assert.Contains(result.RejectionReasons, r => r.Contains("Kapital", StringComparison.Ordinal));
        Assert.Contains(result.RejectionReasons, r => r.Contains("Mindestgewinn", StringComparison.Ordinal));
    }
}