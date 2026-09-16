using WALLEve.Models.Database;
using WALLEve.Models.Trading;
using WALLEve.Services.Trading;
using WALLEve.Services.Trading.Interfaces;

namespace WALLEve.Tests;

/// <summary>
/// Tests der reinen Signalentscheidung (Issue #68): kein Meldungssturm bei
/// unveränderten Ergebnissen und über Neustarts hinweg, Cooldown-Grenzfälle
/// mit expliziter Fake-Clock (utcNow), Owner-Profile und manuelle
/// Deaktivierung. Die Entscheidung ist eine reine Funktion — kein Datenbank-
/// oder Uhrzeit-Zugriff, daher deterministisch.
/// </summary>
public class TradeSignalDecisionTests
{
    private const int OwnerA = 1001;
    private const int OwnerB = 1002;

    private static readonly DateTime Now = new(2026, 9, 16, 12, 0, 0, DateTimeKind.Utc);

    private static TradingOpportunity Opportunity(int typeId = 44992, int characterId = OwnerA, double profit = 12_345,
        double score = 80, string version = "inventory-sell-v1", long? sellLocationId = 60003760)
        => new()
        {
            CharacterId = characterId,
            TypeId = typeId,
            OpportunityType = "inventory_sell",
            BuyLocationId = null,
            SellLocationId = sellLocationId,
            BuyPrice = 100.0,
            SellPrice = 130.5,
            EstimatedProfit = profit,
            RequiredCapital = 10_000,
            Score = score,
            AlgorithmVersion = version
        };

    private static TradeProfile Profile(int characterId = OwnerA) => new()
    {
        CharacterId = characterId,
        Name = "Mein Profil"
    };

    [Fact]
    public void SameCandidates_AreSuppressedAsUnchanged_NoMessageStorm()
    {
        var service = new TradeSignalDecisionService();
        var profile = Profile();

        var first = service.Evaluate(profile, [Opportunity()], Now);
        Assert.True(first.Signal);

        service.RecordReported(profile, first.Fingerprint, Now);

        var second = service.Evaluate(profile, [Opportunity()], Now.AddMinutes(5));
        Assert.False(second.Signal);
        Assert.Equal(TradeSignalSuppression.UnchangedResult, second.Suppression);
    }

    [Fact]
    public void Restart_WithPersistedFingerprint_DoesNotReReport()
    {
        var service = new TradeSignalDecisionService();
        var beforeRestart = Profile();
        var first = service.Evaluate(beforeRestart, [Opportunity()], Now);
        service.RecordReported(beforeRestart, first.Fingerprint, Now);

        // Neustart: frisches Profil aus der Datenbank — nur die PERSISTIERTEN
        // Felder (Fingerprint, Zeitpunkt) sind wiederhergestellt, der restliche
        // In-Memory-Zustand ist verloren.
        var afterRestart = new TradeProfile
        {
            CharacterId = beforeRestart.CharacterId,
            Name = beforeRestart.Name,
            LastReportedFingerprint = beforeRestart.LastReportedFingerprint,
            LastReportedAt = beforeRestart.LastReportedAt
        };

        var decision = service.Evaluate(afterRestart, [Opportunity()], Now.AddHours(1));
        Assert.False(decision.Signal);
        Assert.Equal(TradeSignalSuppression.UnchangedResult, decision.Suppression);
    }

    [Fact]
    public void ChangedCandidates_AreNewSignal()
    {
        var service = new TradeSignalDecisionService();
        var profile = Profile();
        var first = service.Evaluate(profile, [Opportunity(typeId: 44992)], Now);
        service.RecordReported(profile, first.Fingerprint, Now);

        var second = service.Evaluate(profile, [Opportunity(typeId: 44993)], Now.AddMinutes(5));
        Assert.True(second.Signal);
        Assert.NotEqual(first.Fingerprint, second.Fingerprint);
    }

    [Fact]
    public void SubRoundingChanges_AreNotMaterial()
    {
        var service = new TradeSignalDecisionService();
        var profile = Profile();
        var first = service.Evaluate(profile, [Opportunity(profit: 12_345.0, score: 80)], Now);
        service.RecordReported(profile, first.Fingerprint, Now);

        // Gewinnschwankung unter 1 ISK ist unter der Materialitätsgrenze.
        var second = service.Evaluate(profile, [Opportunity(profit: 12_345.4, score: 80)], Now.AddMinutes(5));
        Assert.False(second.Signal);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
    }

    [Fact]
    public void Cooldown_SuppressesWithinWindow_AndAllowsExactlyAtBoundary()
    {
        var service = new TradeSignalDecisionService();
        var profile = Profile();
        profile.CooldownMinutes = 30;
        var first = service.Evaluate(profile, [Opportunity(typeId: 44992)], Now);
        service.RecordReported(profile, first.Fingerprint, Now);

        // Neue, materiell andere Kandidaten — aber innerhalb des Cooldowns.
        var within = service.Evaluate(profile, [Opportunity(typeId: 44993)], Now.AddMinutes(29));
        Assert.False(within.Signal);
        Assert.Equal(TradeSignalSuppression.CooldownActive, within.Suppression);

        // Grenze inklusiv: exakt 30 Minuten nach der Meldung ist das Signal wieder erlaubt.
        var atBoundary = service.Evaluate(profile, [Opportunity(typeId: 44993)], Now.AddMinutes(30));
        Assert.True(atBoundary.Signal);
    }

    [Fact]
    public void NoCooldown_AllowsImmediatelyAfterReport()
    {
        var service = new TradeSignalDecisionService();
        var profile = Profile(); // CooldownMinutes == 0
        var first = service.Evaluate(profile, [Opportunity(typeId: 44992)], Now);
        service.RecordReported(profile, first.Fingerprint, Now);

        var second = service.Evaluate(profile, [Opportunity(typeId: 44993)], Now.AddSeconds(1));
        Assert.True(second.Signal);
    }

    [Fact]
    public void ManualDeactivation_SuppressesEvenNewChance()
    {
        var service = new TradeSignalDecisionService();
        var profile = Profile();
        profile.SignalDeactivated = true;

        var decision = service.Evaluate(profile, [Opportunity(typeId: 44992)], Now);
        Assert.False(decision.Signal);
        Assert.Equal(TradeSignalSuppression.ManualDeactivation, decision.Suppression);
    }

    [Fact]
    public void ForeignOwnerCandidates_DoNotChangeThisProfileSignal()
    {
        var service = new TradeSignalDecisionService();
        var profile = Profile(OwnerA);
        var first = service.Evaluate(profile, [Opportunity(typeId: 44992, characterId: OwnerA)], Now);
        service.RecordReported(profile, first.Fingerprint, Now);
        var expected = first.Fingerprint;

        // Fremde Kandidaten (OwnerB) tauchen im Lauf auf, gehören aber nicht zum Profil.
        var mixed = service.Evaluate(profile,
            [Opportunity(typeId: 44992, characterId: OwnerA), Opportunity(typeId: 99999, characterId: OwnerB)],
            Now.AddMinutes(5));

        Assert.False(mixed.Signal);
        Assert.Equal(TradeSignalSuppression.UnchangedResult, mixed.Suppression);
        Assert.Equal(expected, mixed.Fingerprint);
    }

    [Fact]
    public void Fingerprint_IsOrderingIndependent()
    {
        var a = Opportunity(typeId: 44992);
        var b = Opportunity(typeId: 44993);

        var forward = TradeSignalDecisionService.ComputeFingerprint([a, b]);
        var backward = TradeSignalDecisionService.ComputeFingerprint([b, a]);

        Assert.Equal(forward, backward);
    }

    [Fact]
    public void RecordReported_PersistsFingerprintAndTimestamp()
    {
        var service = new TradeSignalDecisionService();
        var profile = Profile();
        var decision = service.Evaluate(profile, [Opportunity()], Now);

        service.RecordReported(profile, decision.Fingerprint, Now);

        Assert.Equal(decision.Fingerprint, profile.LastReportedFingerprint);
        Assert.Equal(Now, profile.LastReportedAt);
    }
}