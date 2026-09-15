using Microsoft.EntityFrameworkCore;
using WALLEve.Data;
using WALLEve.Models.Database;
using WALLEve.Models.Trading;
using WALLEve.Services.Trading;

namespace WALLEve.Tests;

/// <summary>
/// Tests der Statusverwaltung für Trading-Empfehlungen (Issue #45):
/// erlaubte/verbotene Statuswechsel, Owner-Isolation, Idempotenz und die
/// Garantie, dass Ablauf/Invalidierung die ursprüngliche Empfehlung und ihre
/// Eingaben NIE löscht sowie "executed" nie automatisch behauptet wird.
/// </summary>
public class TradeStatusServiceTests
{
    private const int OwnerA = 1001;
    private const int OwnerB = 1002;

    private static async Task<TradingOpportunity> SeedOpportunityAsync(WalletDbContext db, int characterId = OwnerA)
    {
        var opportunity = new TradingOpportunity
        {
            TypeId = 44992,
            CharacterId = characterId,
            OpportunityType = "inventory_sell",
            BuyPrice = 100,
            SellPrice = 150,
            EstimatedProfit = 48_000,
            RequiredCapital = 1_000_000,
            Score = 60,
            Provenance = TradingOpportunity.ProvenanceHeuristic,
            AlgorithmVersion = "inventory-sell-v1",
            DataQuality = "complete",
            Evidence = "Test: 10 Einheiten, Cost Basis 100 ISK, Verkauf 150 ISK",
            DetectedAt = DateTime.UtcNow.AddHours(-1),
            ExpiresAt = DateTime.UtcNow.AddHours(23),
            Status = RecommendationStatus.Active
        };
        db.TradingOpportunities.Add(opportunity);
        await db.SaveChangesAsync();
        return opportunity;
    }

    private static WalletDbContext CreateDb() => TestDb.Create();

    [Fact]
    public async Task Mark_AllowedTransitionUser_PersistsHistoryWithSourceAndTime()
    {
        await using var db = CreateDb();
        var service = new TradeStatusService(db);
        var opportunity = await SeedOpportunityAsync(db);

        var result = await service.MarkAsync(opportunity.Id, OwnerA, RecommendationStatus.Planned,
            TradeStatusSource.User, "Diesen Handel beobachten");

        Assert.True(result.Success);
        Assert.NotNull(result.Change);
        Assert.Equal(RecommendationStatus.Active, result.Change!.FromStatus);
        Assert.Equal(RecommendationStatus.Planned, result.Change.ToStatus);
        Assert.Equal(TradeStatusSource.User, result.Change.Source);
        Assert.Equal("Diesen Handel beobachten", result.Change.Note);
        Assert.Equal(OwnerA, result.Change.CharacterId);

        var stored = await db.TradingOpportunities.FindAsync(opportunity.Id);
        Assert.Equal(RecommendationStatus.Planned, stored!.Status);
        Assert.NotEqual(default, result.Change.ChangedAt);
    }

    [Fact]
    public async Task Mark_PlannedToExecuted_SetsExecutedAt()
    {
        await using var db = CreateDb();
        var service = new TradeStatusService(db);
        var opportunity = await SeedOpportunityAsync(db);

        await service.MarkAsync(opportunity.Id, OwnerA, RecommendationStatus.Planned,
            TradeStatusSource.User, null);
        var result = await service.MarkAsync(opportunity.Id, OwnerA, RecommendationStatus.Executed,
            TradeStatusSource.User, "Auftrag ausgeführt");

        Assert.True(result.Success);
        var stored = await db.TradingOpportunities.FindAsync(opportunity.Id);
        Assert.Equal(RecommendationStatus.Executed, stored!.Status);
        Assert.NotNull(stored.ExecutedAt);
        Assert.Equal(result.Change!.ChangedAt, stored.ExecutedAt);
    }

    [Fact]
    public async Task Mark_ForbiddenTransition_RejectedWithoutPersistence()
    {
        await using var db = CreateDb();
        var service = new TradeStatusService(db);
        var opportunity = await SeedOpportunityAsync(db);

        // executed ist terminal: kein Wechsel zurück zu active erlaubt.
        await service.MarkAsync(opportunity.Id, OwnerA, RecommendationStatus.Executed,
            TradeStatusSource.User, null);
        var result = await service.MarkAsync(opportunity.Id, OwnerA, RecommendationStatus.Active,
            TradeStatusSource.User, null);

        Assert.False(result.Success);
        Assert.Contains("Verbotener", result.Error);

        var stored = await db.TradingOpportunities.FindAsync(opportunity.Id);
        Assert.Equal(RecommendationStatus.Executed, stored!.Status);
        Assert.Single(await service.GetHistoryAsync(opportunity.Id, OwnerA));
    }

    [Fact]
    public async Task Mark_ExecutedBySystem_Rejected()
    {
        await using var db = CreateDb();
        var service = new TradeStatusService(db);
        var opportunity = await SeedOpportunityAsync(db);

        var result = await service.MarkAsync(opportunity.Id, OwnerA, RecommendationStatus.Executed,
            TradeStatusSource.System, null);

        Assert.False(result.Success);
        Assert.Contains("automatische", result.Error);
        var stored = await db.TradingOpportunities.FindAsync(opportunity.Id);
        Assert.Equal(RecommendationStatus.Active, stored!.Status);
        Assert.Null(stored.ExecutedAt);
        Assert.Empty(await service.GetHistoryAsync(opportunity.Id, OwnerA));
    }

    [Fact]
    public async Task Mark_ExpiredOnlyBySystem_AndKeepsRecommendationAndInputs()
    {
        await using var db = CreateDb();
        var service = new TradeStatusService(db);
        var opportunity = await SeedOpportunityAsync(db);

        // Nutzer darf NICHT als abgelaufen markieren.
        var userAttempt = await service.MarkAsync(opportunity.Id, OwnerA, RecommendationStatus.Expired,
            TradeStatusSource.User, null);
        Assert.False(userAttempt.Success);

        // System darf ablaufen lassen; Empfehlung + Eingaben bleiben vollständig erhalten.
        var result = await service.MarkAsync(opportunity.Id, OwnerA, RecommendationStatus.Expired,
            TradeStatusSource.System, "Ablaufzeit überschritten");

        Assert.True(result.Success);
        var stored = await db.TradingOpportunities.FindAsync(opportunity.Id);
        Assert.NotNull(stored);
        Assert.Equal(RecommendationStatus.Expired, stored.Status);
        Assert.Equal("inventory_sell", stored.OpportunityType);
        Assert.Equal(1_000_000, stored.RequiredCapital);
        Assert.Equal("inventory-sell-v1", stored.AlgorithmVersion);
        Assert.Equal("Test: 10 Einheiten, Cost Basis 100 ISK, Verkauf 150 ISK", stored.Evidence);
        Assert.Equal(60, stored.Score);
    }

    [Fact]
    public async Task Mark_Invalidation_DoesNotDeleteRecommendationOrInputs()
    {
        await using var db = CreateDb();
        var service = new TradeStatusService(db);
        var opportunity = await SeedOpportunityAsync(db);

        var result = await service.MarkAsync(opportunity.Id, OwnerA, RecommendationStatus.Invalid,
            TradeStatusSource.System, "Berechnung veraltet");

        Assert.True(result.Success);
        Assert.Equal(1, db.TradingOpportunities.Count(o => o.Id == opportunity.Id));
        var stored = await db.TradingOpportunities.FindAsync(opportunity.Id);
        Assert.Equal(RecommendationStatus.Invalid, stored!.Status);
        Assert.Equal(44992, stored.TypeId);
    }

    [Fact]
    public async Task Mark_OwnerIsolation_RejectsForeignOwner()
    {
        await using var db = CreateDb();
        var service = new TradeStatusService(db);
        var opportunity = await SeedOpportunityAsync(db, OwnerA);

        var result = await service.MarkAsync(opportunity.Id, OwnerB, RecommendationStatus.Planned,
            TradeStatusSource.User, null);

        Assert.False(result.Success);
        Assert.Contains("Fremde", result.Error);
        var stored = await db.TradingOpportunities.FindAsync(opportunity.Id);
        Assert.Equal(RecommendationStatus.Active, stored!.Status);
        Assert.Empty(await service.GetHistoryAsync(opportunity.Id, OwnerA));
    }

    [Fact]
    public async Task Mark_SameStatusAgain_IsIdempotentWithoutDuplicateHistory()
    {
        await using var db = CreateDb();
        var service = new TradeStatusService(db);
        var opportunity = await SeedOpportunityAsync(db);

        await service.MarkAsync(opportunity.Id, OwnerA, RecommendationStatus.Planned,
            TradeStatusSource.User, null);
        var repeat = await service.MarkAsync(opportunity.Id, OwnerA, RecommendationStatus.Planned,
            TradeStatusSource.User, "nochmal");

        Assert.True(repeat.Success);
        Assert.Equal(RecommendationStatus.Planned, repeat.Error);
        Assert.Single(await service.GetHistoryAsync(opportunity.Id, OwnerA));
    }

    [Fact]
    public async Task GetHistory_OwnerIsolation_OnlyOwnersSeeHistory()
    {
        await using var db = CreateDb();
        var service = new TradeStatusService(db);
        var opportunity = await SeedOpportunityAsync(db, OwnerA);

        await service.MarkAsync(opportunity.Id, OwnerA, RecommendationStatus.Planned,
            TradeStatusSource.User, null);

        var historyOfOwner = await service.GetHistoryAsync(opportunity.Id, OwnerA);
        var historyOfForeigner = await service.GetHistoryAsync(opportunity.Id, OwnerB);

        Assert.Single(historyOfOwner);
        Assert.Empty(historyOfForeigner);
    }

    [Fact]
    public async Task Mark_ChainActivePlannedDismissed_HistoryInOrder()
    {
        await using var db = CreateDb();
        var service = new TradeStatusService(db);
        var opportunity = await SeedOpportunityAsync(db);

        await service.MarkAsync(opportunity.Id, OwnerA, RecommendationStatus.Planned,
            TradeStatusSource.User, null);
        await service.MarkAsync(opportunity.Id, OwnerA, RecommendationStatus.Dismissed,
            TradeStatusSource.User, "Doch nicht");

        var history = await service.GetHistoryAsync(opportunity.Id, OwnerA);
        Assert.Equal(2, history.Count);
        Assert.Equal(RecommendationStatus.Active, history[0].FromStatus);
        Assert.Equal(RecommendationStatus.Planned, history[0].ToStatus);
        Assert.Equal(RecommendationStatus.Planned, history[1].FromStatus);
        Assert.Equal(RecommendationStatus.Dismissed, history[1].ToStatus);
    }

    [Fact]
    public async Task ApplyExpiry_MarksExpiredAndKeepsRow_WritesSystemHistory_IsIdempotent()
    {
        await using var db = CreateDb();
        var service = new TradeStatusService(db);
        var opportunity = await SeedOpportunityAsync(db);
        opportunity.ExpiresAt = DateTime.UtcNow.AddHours(-2); // abgelaufen
        await db.SaveChangesAsync();

        var marked = await service.ApplyExpiryAsync(DateTime.UtcNow);
        Assert.Equal(1, marked);

        // Empfehlung/Inputs bleiben erhalten — nur der Status ändert sich.
        var stored = await db.TradingOpportunities.SingleAsync(o => o.Id == opportunity.Id);
        Assert.Equal(RecommendationStatus.Expired, stored.Status);
        Assert.Equal("Test: 10 Einheiten, Cost Basis 100 ISK, Verkauf 150 ISK", stored.Evidence);

        var change = await db.TradeStatusChanges.SingleAsync(c => c.TradingOpportunityId == opportunity.Id);
        Assert.Equal(RecommendationStatus.Active, change.FromStatus); // Legacy-Wert unverändert dokumentiert
        Assert.Equal(RecommendationStatus.Expired, change.ToStatus);
        Assert.Equal(TradeStatusSource.System, change.Source);
        Assert.False(string.IsNullOrWhiteSpace(change.Note));

        // Wiederholung ist idempotent: keine zweite Markierung, keine neue Historie.
        var again = await service.ApplyExpiryAsync(DateTime.UtcNow);
        Assert.Equal(0, again);
        Assert.Single(await service.GetHistoryAsync(opportunity.Id, OwnerA));
    }

    [Fact]
    public async Task InvalidateStaged_MarksInvalid_KeepsRowAndEvidence_IsIdempotent()
    {
        await using var db = CreateDb();
        var service = new TradeStatusService(db);
        var opportunity = await SeedOpportunityAsync(db);

        var result = await service.InvalidateStagedAsync(opportunity, "Empfehlung fällt weg", DateTime.UtcNow);
        Assert.True(result);
        await db.SaveChangesAsync(); // der Aufrufer persistiert im eigenen Zyklus (eine Transaktion)

        // Empfehlung bleibt vollständig erhalten, nur der Status wechselt.
        var stored = await db.TradingOpportunities.SingleAsync(o => o.Id == opportunity.Id);
        Assert.Equal(RecommendationStatus.Invalid, stored.Status);
        Assert.Equal("Test: 10 Einheiten, Cost Basis 100 ISK, Verkauf 150 ISK", stored.Evidence);

        var change = await db.TradeStatusChanges.SingleAsync(c => c.TradingOpportunityId == opportunity.Id);
        Assert.Equal(RecommendationStatus.Active, change.FromStatus);
        Assert.Equal(RecommendationStatus.Invalid, change.ToStatus);
        Assert.Equal(TradeStatusSource.System, change.Source);

        // Wiederholung ist idempotent: bereits invalid → true, keine neue Historie.
        var again = await service.InvalidateStagedAsync(opportunity, "erneut", DateTime.UtcNow);
        Assert.True(again);
        await db.SaveChangesAsync();
        Assert.Single(await service.GetHistoryAsync(opportunity.Id, OwnerA));

        // Terminale Zustände (z. B. executed) werden nicht invalidisiert.
        var executed = await SeedOpportunityAsync(db, OwnerB);
        executed.Status = RecommendationStatus.Executed;
        await db.SaveChangesAsync();
        Assert.False(await service.InvalidateStagedAsync(executed, "spät", DateTime.UtcNow));
        Assert.Equal(RecommendationStatus.Executed, executed.Status);
    }
}