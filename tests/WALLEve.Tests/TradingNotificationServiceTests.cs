using WALLEve.Models.Database;
using WALLEve.Services.Notifications;

namespace WALLEve.Tests;

/// <summary>
/// In-App-Meldungsfeed (#70): Dedupe/Cooldown auch bei parallelem Polling und
/// Reconnect, Owner-Isolation, Datenalter. Browser-Berechtigung spielt für den
/// Feed keine Rolle — verweigerte/fehlende Berechtigung beeinträchtigt die
/// In-App-Meldungen nie (eigene Tests für <see cref="BrowserNotificationState"/>).
/// Deterministisch: keine Timer, keine Live-ESI, feste utcNow-Werte.
/// </summary>
public class TradingNotificationServiceTests
{
    private static readonly DateTime UtcNow = new(2026, 9, 16, 12, 0, 0, DateTimeKind.Utc);

    private static TradingNotificationService CreateService(int cooldownMinutes = 30, int maxFeedSize = 60)
        => new(new TradingNotificationOptions { CooldownMinutes = cooldownMinutes, MaxFeedSize = maxFeedSize });

    private static TradingOpportunity CreateOpportunity(
        int id,
        int characterId = 1,
        string type = "inventory_sell",
        int typeId = 34_000_001,
        long? buyLocation = 60000001,
        long? sellLocation = 60000002,
        double score = 80,
        double profit = 1_000_000,
        DateTime? detectedAt = null)
        => new()
        {
            Id = id,
            CharacterId = characterId,
            OpportunityType = type,
            TypeId = typeId,
            BuyLocationId = buyLocation,
            SellLocationId = sellLocation,
            Score = score,
            EstimatedProfit = profit,
            DetectedAt = detectedAt ?? UtcNow,
        };

    // ------------------------------------------------------------------
    // Veröffentlichen & Grundverhalten
    // ------------------------------------------------------------------

    [Fact]
    public void Publish_CreatesUnseenNotificationWithLinkAndDataAge()
    {
        var service = CreateService();
        var opp = CreateOpportunity(id: 7, score: 91, profit: 2_500_000);

        var published = service.Publish(1, new[] { opp }, UtcNow);

        Assert.Equal(1, published);
        var all = service.GetAll(1);
        var notification = Assert.Single(all);
        Assert.False(notification.IsSeen);
        Assert.Equal(7, notification.OpportunityId);
        Assert.Equal("/trading#opp-7", notification.LinkUrl);
        Assert.Contains("2.500.000", notification.Message);
        Assert.Equal(UtcNow, notification.DetectedAtUtc);
        Assert.Equal("Verkaufschance erkannt", notification.Title);
        Assert.Equal("Datenalter: gerade eben", "Datenalter: " + TradingNotificationFormat.FormatDataAge(notification.DetectedAtUtc, UtcNow));
    }

    [Fact]
    public void GetUnseen_OnlyReturnsUnseen_UntilMarkAllSeen()
    {
        var service = CreateService();
        service.Publish(1, new[] { CreateOpportunity(id: 1) }, UtcNow);

        Assert.Single(service.GetUnseen(1));

        service.MarkAllSeen(1);

        Assert.Empty(service.GetUnseen(1));
        Assert.Single(service.GetAll(1)); // Feed bleibt erhalten, nur gelesen.
    }

    // ------------------------------------------------------------------
    // Dedupe / Cooldown
    // ------------------------------------------------------------------

    [Fact]
    public void Publish_SameContent_Deduplicated_EvenWithDifferentDbId()
    {
        var service = CreateService();
        // Erneute Analyse derselben Chance: neue DB-Id, gleicher Inhalt.
        service.Publish(1, new[] { CreateOpportunity(id: 10) }, UtcNow);

        var republished = service.Publish(1, new[] { CreateOpportunity(id: 99, score: 80, profit: 1_000_000) }, UtcNow.AddSeconds(5));

        Assert.Equal(0, republished);
        Assert.Single(service.GetAll(1));
    }

    [Fact]
    public void Publish_AfterCooldown_SameContentIsNewSignalAgain()
    {
        var service = CreateService(cooldownMinutes: 30);
        service.Publish(1, new[] { CreateOpportunity(id: 1) }, UtcNow);

        var duringCooldown = service.Publish(1, new[] { CreateOpportunity(id: 2) }, UtcNow.AddMinutes(29));
        Assert.Equal(0, duringCooldown);

        // Grenze inklusiv (wie #68): exakt zum Ablauf ist ein neues Signal erlaubt.
        var afterCooldown = service.Publish(1, new[] { CreateOpportunity(id: 3) }, UtcNow.AddMinutes(30));
        Assert.Equal(1, afterCooldown);
        Assert.Equal(2, service.GetAll(1).Count);
    }

    [Fact]
    public void Publish_MaterialChange_IsNewNotification()
    {
        var service = CreateService();
        service.Publish(1, new[] { CreateOpportunity(id: 1, score: 60) }, UtcNow);

        // Score über Materialitätsgrenze (gerundet) geändert → neue Meldung.
        var republished = service.Publish(1, new[] { CreateOpportunity(id: 2, score: 61) }, UtcNow.AddMinutes(1));

        Assert.Equal(1, republished);
        Assert.Equal(2, service.GetAll(1).Count);
    }

    // ------------------------------------------------------------------
    // Paralleles Polling
    // ------------------------------------------------------------------

    [Fact]
    public async Task ParallelPolling_ProducesExactlyOneNotification()
    {
        var service = CreateService();
        var opps = new[]
        {
            CreateOpportunity(id: 1, typeId: 34_000_001),
            CreateOpportunity(id: 2, typeId: 34_000_002),
            CreateOpportunity(id: 3, typeId: 34_000_003),
        };
        // Publish im Feed: wenige Chancen, damit die Off-by-one-Grenze sichtbar wird.
        service.Publish(1, opps, UtcNow);

        // Simuliert parallele Polling-Circuits (mehrere Tabs, doppelte Timer,
        // Reconnect-Race): alle liefern denselben Kandidatenstand erneut.
        var polls = Enumerable.Range(0, 32)
            .Select(_ => Task.Run(() => service.Publish(1, opps, UtcNow.AddMilliseconds(5))))
            .ToArray();
        await Task.WhenAll(polls);

        var totalPublished = polls.Sum(t => t.Result);
        Assert.Equal(0, totalPublished); // alles bereits dedupliziert
        Assert.Equal(3, service.GetAll(1).Count); // keine Duplikate entstanden
    }

    [Fact]
    public async Task ParallelFirstPoll_PublishesOnce()
    {
        var service = CreateService();
        var opps = new[] { CreateOpportunity(id: 1) };

        var polls = Enumerable.Range(0, 32)
            .Select(_ => Task.Run(() => service.Publish(1, opps, UtcNow)))
            .ToArray();
        await Task.WhenAll(polls);

        Assert.Equal(1, polls.Sum(t => t.Result));
        Assert.Single(service.GetAll(1));
    }

    // ------------------------------------------------------------------
    // Reconnect
    // ------------------------------------------------------------------

    [Fact]
    public void Reconnect_NewCircuitSeesSameFeed_AndDedupePersists()
    {
        var service = CreateService();
        service.Publish(1, new[] { CreateOpportunity(id: 1) }, UtcNow);

        // „Alter Circuit" liest und bestätigt.
        Assert.Single(service.GetUnseen(1));
        service.MarkAllSeen(1);

        // „Neuer Circuit" (nach Reconnect): Feed-Zustand ist erhalten.
        var all = service.GetAll(1);
        var notification = Assert.Single(all);
        Assert.True(notification.IsSeen);

        // Reconnect-Poll mit demselben Kandidatenstand erzeugt kein Duplikat,
        // auch wenn der neue Circuit nichts vom alten weiß.
        var republished = service.Publish(1, new[] { CreateOpportunity(id: 2) }, UtcNow.AddSeconds(10));
        Assert.Equal(0, republished);
        Assert.Single(service.GetAll(1));
    }

    // ------------------------------------------------------------------
    // Owner-Isolation & Feed-Begrenzung
    // ------------------------------------------------------------------

    [Fact]
    public void Publish_IgnoresOpportunitiesOfOtherCharacters()
    {
        var service = CreateService();
        var foreign = CreateOpportunity(id: 1, characterId: 2);

        var published = service.Publish(1, new[] { foreign }, UtcNow);

        Assert.Equal(0, published);
        Assert.Empty(service.GetAll(1));
        Assert.Empty(service.GetAll(2)); // nichts wurde einem anderen Charakter gutgeschrieben
    }

    [Fact]
    public void Feed_CapsAtMaxSize_NewestWin()
    {
        var service = CreateService(maxFeedSize: 3);
        for (var i = 1; i <= 5; i++)
            service.Publish(1, new[] { CreateOpportunity(id: i, typeId: 34_000_000 + i) }, UtcNow.AddMinutes(i));

        var all = service.GetAll(1);
        Assert.Equal(3, all.Count);
        Assert.Equal(new[] { 5, 4, 3 }, all.Select(n => n.OpportunityId)); // neueste zuerst
    }

    // ------------------------------------------------------------------
    // Datenalter-Formatierung (Akzeptanzkriterium „neue Meldung zeigt Datenalter")
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(0, "gerade eben")]
    [InlineData(30, "gerade eben")]
    [InlineData(70, "vor 1 Min")]
    [InlineData(60 * 5, "vor 5 Min")]
    [InlineData(60 * 60 * 3, "vor 3 Std")]
    [InlineData(60 * 60 * 24 * 2, "vor 2 Tag(en)")]
    public void FormatDataAge_ProducesGermanLabels(int ageSeconds, string expected)
    {
        var detected = UtcNow.AddSeconds(-ageSeconds);
        Assert.Equal(expected, TradingNotificationFormat.FormatDataAge(detected, UtcNow));
    }
}

/// <summary>
/// Browser-Berechtigungszustand (#70): Verweigerte/fehlende Berechtigung oder
/// nicht unterstützter Browser darf die In-App-Meldungen nie beeinträchtigen —
/// der Feed-Service kennt diesen Zustand gar nicht; der Zustand selbst liefert
/// nur Hinweise und schaltet den optionalen JS-Seitenkanal ab.
/// </summary>
public class BrowserNotificationStateTests
{
    [Fact]
    public void UnsupportedBrowser_DisablesBrowserChannel_KeepsInAppHint()
    {
        var state = new BrowserNotificationState();
        state.ApplySupport(false);

        Assert.False(state.IsSupported);
        Assert.False(state.CanSendBrowserNotifications);
        Assert.Contains("In-App-Meldungen bleiben aktiv", state.Hint);
    }

    [Fact]
    public void DeniedPermission_DisablesBrowserChannel_KeepsInAppHint()
    {
        var state = new BrowserNotificationState();
        state.ApplySupport(true);
        state.ApplyPermission(BrowserNotificationState.PermissionDenied);

        Assert.True(state.IsSupported);
        Assert.False(state.CanSendBrowserNotifications);
        Assert.Contains("In-App-Meldungen bleiben trotzdem aktiv", state.Hint);
        Assert.Equal("denied", state.Permission);
    }

    [Fact]
    public void DeniedResult_AfterOptIn_DoesNotEnableBrowserChannel()
    {
        var state = new BrowserNotificationState();
        state.ApplySupport(true);
        state.ApplyPermission(BrowserNotificationState.PermissionDefault);
        state.ToggleOptIn(true);
        state.ApplyRequestResult(granted: false);

        Assert.False(state.CanSendBrowserNotifications);
        Assert.Contains("In-App-Meldungen bleiben trotzdem aktiv", state.Hint);
    }

    [Fact]
    public void GrantedPermission_WithOptIn_EnablesBrowserChannel()
    {
        var state = new BrowserNotificationState();
        state.ApplySupport(true);
        state.ApplyPermission(BrowserNotificationState.PermissionGranted);
        state.ToggleOptIn(true);

        Assert.True(state.CanSendBrowserNotifications);
        Assert.Contains("In-App-Meldungen bleiben zusätzlich bestehen", state.Hint);
    }

    [Fact]
    public void GrantedWithoutOptIn_DoesNotSendBrowserNotifications()
    {
        var state = new BrowserNotificationState();
        state.ApplySupport(true);
        state.ApplyPermission(BrowserNotificationState.PermissionGranted);

        Assert.False(state.CanSendBrowserNotifications); // Freiwilligkeit: erst Opt-in.
    }
}