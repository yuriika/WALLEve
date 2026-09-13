using Microsoft.EntityFrameworkCore;
using WALLEve.Data;
using WALLEve.Models.Database;
using WALLEve.Models.Trading;
using WALLEve.Services.Trading;
using WALLEve.Services.Trading.Interfaces;
using Xunit;

namespace WALLEve.Tests;

/// <summary>
/// Issue #45 — Empfehlungen markieren und Historie erhalten:
/// erlaubte/verbotene Statuswechsel, Owner-Isolation, Idempotenz und die
/// Bewahrung der ursprünglichen Empfehlung samt Inputs bei Ablauf/Invalidierung.
/// </summary>
public class TradeStatusServiceTests
{
    private const int OwnerId = 90073315;
    private const int OtherOwnerId = 999;

    private static TradingOpportunity SeedOpportunity(WalletDbContext db, int typeId, string status,
        DateTime expiresAt, int characterId = OwnerId)
    {
        var opportunity = new TradingOpportunity
        {
            CharacterId = characterId,
            TypeId = typeId,
            OpportunityType = "inventory_sell",
            BuyPrice = 90, SellPrice = 110,
            EstimatedProfit = 8450, RequiredCapital = 90_000, Score = 80,
            Provenance = "heuristic", Evidence = "ursprüngliche Empfehlung",
            DetectedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt,
            Status = status
        };
        db.TradingOpportunities.Add(opportunity);
        db.SaveChanges();
        return opportunity;
    }

    private static ITradeStatusService CreateService(WalletDbContext db) => new TradeStatusService(db);

    // ------------------------------------------------------------------
    // Erlaubte/verbotene Übergänge
    // ------------------------------------------------------------------

    [Fact]
    public async Task Mark_PlannedToExecuted_SetsStatusTimeAndUserSource()
    {
        using var db = TestDb.Create();
        var opp = SeedOpportunity(db, 1, "planned", DateTime.UtcNow.AddHours(1));

        var result = await CreateService(db).MarkAsync(OwnerId, opp.Id, TradeStatus.Executed);

        Assert.True(result.Success);
        Assert.True(result.Changed);

        var reloaded = await db.TradingOpportunities.SingleAsync(o => o.Id == opp.Id);
        Assert.Equal("executed", reloaded.Status);
        Assert.NotNull(reloaded.ExecutedAt);

        // Historie dokumentiert Zeit + Quelle (manuelle Nutzerangabe, kein Automatismus).
        var change = Assert.Single(await db.TradeStatusChanges.Where(c => c.TradingOpportunityId == opp.Id).ToListAsync());
        Assert.Equal("planned", change.FromStatus);
        Assert.Equal("executed", change.ToStatus);
        Assert.Equal("user", change.Source);
    }

    [Fact]
    public async Task Mark_LegacyActiveNormalized_PlannedToExecutedAllowed()
    {
        using var db = TestDb.Create();
        var opp = SeedOpportunity(db, 1, "active", DateTime.UtcNow.AddHours(1)); // Legacy-Vor-45-Wert

        var result = await CreateService(db).MarkAsync(OwnerId, opp.Id, TradeStatus.Executed);

        Assert.True(result.Success);
        Assert.Equal("executed", (await db.TradingOpportunities.SingleAsync(o => o.Id == opp.Id)).Status);
    }

    [Fact]
    public async Task Mark_FromTerminalState_Forbidden()
    {
        using var db = TestDb.Create();
        var executed = SeedOpportunity(db, 1, "executed", DateTime.UtcNow.AddHours(1));
        var dismissed = SeedOpportunity(db, 2, "dismissed", DateTime.UtcNow.AddHours(1));
        var expired = SeedOpportunity(db, 3, "expired", DateTime.UtcNow.AddHours(-1));
        var invalid = SeedOpportunity(db, 4, "invalid", DateTime.UtcNow.AddHours(1));
        var service = CreateService(db);

        // Terminale Zustände sind unveränderlich: keine Wechsel heraus, kein Historie-Eintrag.
        foreach (var opp in new[] { executed, dismissed, expired, invalid })
        {
            var back = await service.MarkAsync(OwnerId, opp.Id, TradeStatus.Planned);
            Assert.False(back.Success);
            Assert.Contains("nicht erlaubt", back.Error);
            Assert.Equal(0, await db.TradeStatusChanges.CountAsync(c => c.TradingOpportunityId == opp.Id));
        }
    }

    [Fact]
    public async Task Mark_SameStatusTwice_IdempotentWithoutNewHistory()
    {
        using var db = TestDb.Create();
        var opp = SeedOpportunity(db, 1, "planned", DateTime.UtcNow.AddHours(1));
        var service = CreateService(db);

        await service.MarkAsync(OwnerId, opp.Id, TradeStatus.Dismissed);
        var second = await service.MarkAsync(OwnerId, opp.Id, TradeStatus.Dismissed);

        Assert.True(second.Success);
        Assert.False(second.Changed); // idempotent: kein Fehler, aber keine neue Historie
        Assert.Equal(1, await db.TradeStatusChanges.CountAsync(c => c.TradingOpportunityId == opp.Id));
        Assert.Equal("dismissed", (await db.TradingOpportunities.SingleAsync(o => o.Id == opp.Id)).Status);
    }

    [Fact]
    public async Task Mark_UnknownStoredStatus_NoSilentFallback()
    {
        using var db = TestDb.Create();
        var opp = SeedOpportunity(db, 1, "irgendwas", DateTime.UtcNow.AddHours(1));

        var result = await CreateService(db).MarkAsync(OwnerId, opp.Id, TradeStatus.Executed);

        Assert.False(result.Success);
        Assert.Contains("Unbekannter Status", result.Error);
        Assert.Equal(0, await db.TradeStatusChanges.CountAsync());
    }

    // ------------------------------------------------------------------
    // Owner-Isolation
    // ------------------------------------------------------------------

    [Fact]
    public async Task Mark_ForeignCharacter_RejectedWithoutChanges()
    {
        using var db = TestDb.Create();
        var opp = SeedOpportunity(db, 1, "planned", DateTime.UtcNow.AddHours(1));

        var result = await CreateService(db).MarkAsync(OtherOwnerId, opp.Id, TradeStatus.Executed);

        Assert.False(result.Success);
        Assert.Contains("Fremde Opportunity", result.Error);
        Assert.Equal("planned", (await db.TradingOpportunities.SingleAsync(o => o.Id == opp.Id)).Status);
        Assert.Equal(0, await db.TradeStatusChanges.CountAsync());
    }

    [Fact]
    public async Task Mark_UnknownOpportunity_Failed()
    {
        using var db = TestDb.Create();

        var result = await CreateService(db).MarkAsync(OwnerId, 42_424, TradeStatus.Executed);

        Assert.False(result.Success);
        Assert.Contains("nicht gefunden", result.Error);
    }

    // ------------------------------------------------------------------
    // Ablauf: immer Verlauf erhalten, nie löschen
    // ------------------------------------------------------------------

    [Fact]
    public async Task ApplyExpiry_MarksPlannedAsExpired_KeepsRowsAndWritesSystemHistory()
    {
        using var db = TestDb.Create();
        var now = DateTime.UtcNow;
        var planned = SeedOpportunity(db, 1, "planned", now.AddHours(-1));    // abgelaufen
        var legacy = SeedOpportunity(db, 2, "active", now.AddHours(-1));      // abgelaufen (Legacy)
        var executed = SeedOpportunity(db, 3, "executed", now.AddHours(-1));  // terminal: unberührt
        var dismissed = SeedOpportunity(db, 4, "dismissed", now.AddHours(-1));// terminal: unberührt
        var future = SeedOpportunity(db, 5, "planned", now.AddHours(1));      // noch gültig

        var marked = await CreateService(db).ApplyExpiryAsync(now);

        Assert.Equal(2, marked); // nur planned + active-Legacy
        Assert.Equal("expired", (await db.TradingOpportunities.SingleAsync(o => o.Id == planned.Id)).Status);
        Assert.Equal("expired", (await db.TradingOpportunities.SingleAsync(o => o.Id == legacy.Id)).Status);
        Assert.Equal("executed", (await db.TradingOpportunities.SingleAsync(o => o.Id == executed.Id)).Status);
        Assert.Equal("dismissed", (await db.TradingOpportunities.SingleAsync(o => o.Id == dismissed.Id)).Status);
        Assert.Equal("planned", (await db.TradingOpportunities.SingleAsync(o => o.Id == future.Id)).Status);

        // Alle 5 Zeilen existieren weiter — Ablauf löscht die Empfehlung nicht.
        Assert.Equal(5, await db.TradingOpportunities.CountAsync());
        var changes = await db.TradeStatusChanges.ToListAsync();
        Assert.Equal(2, changes.Count);
        Assert.All(changes, c =>
        {
            Assert.Equal("expired", c.ToStatus);
            Assert.Equal("system", c.Source);
            Assert.Equal(now, c.ChangedAt); // Zeitpunkt dokumentiert
        });
    }

    [Fact]
    public async Task ApplyExpiry_SecondRun_IsIdempotent()
    {
        using var db = TestDb.Create();
        var now = DateTime.UtcNow;
        var opp = SeedOpportunity(db, 1, "active", now.AddHours(-1));
        var service = CreateService(db);

        Assert.Equal(1, await service.ApplyExpiryAsync(now));
        Assert.Equal(0, await service.ApplyExpiryAsync(now)); // nichts mehr zu markieren
        Assert.Equal(1, await db.TradeStatusChanges.CountAsync());
    }

    // ------------------------------------------------------------------
    // Invalidierung (Analyse-Hygiene): stagen ohne Delete
    // ------------------------------------------------------------------

    [Fact]
    public void InvalidateStaged_Planned_SetsInvalidAndStagesHistory()
    {
        using var db = TestDb.Create();
        var opp = SeedOpportunity(db, 1, "planned", DateTime.UtcNow.AddHours(1));

        var ok = CreateService(db).InvalidateStaged(opp, "kein ausführbarer Quote", DateTime.UtcNow);

        Assert.True(ok);
        Assert.Equal("invalid", opp.Status);
        Assert.Equal("kein ausführbarer Quote", Assert.Single(db.TradeStatusChanges.Local).Reason);
    }

    [Fact]
    public void InvalidateStaged_ExecutedTerminal_Rejected()
    {
        using var db = TestDb.Create();
        var opp = SeedOpportunity(db, 1, "executed", DateTime.UtcNow.AddHours(-2));

        var ok = CreateService(db).InvalidateStaged(opp, "unsinnig", DateTime.UtcNow);

        Assert.False(ok);
        Assert.Equal("executed", opp.Status); // unverändert
        Assert.Empty(db.TradeStatusChanges.Local);
    }

    [Fact]
    public void InvalidateStaged_AlreadyInvalid_IdempotentNoDuplicate()
    {
        using var db = TestDb.Create();
        var opp = SeedOpportunity(db, 1, "invalid", DateTime.UtcNow.AddHours(1));

        var ok = CreateService(db).InvalidateStaged(opp, "nochmal", DateTime.UtcNow);

        Assert.True(ok); // idempotent: kein Fehler, aber kein zweiter Historie-Eintrag gestaged
        Assert.Empty(db.TradeStatusChanges.Local);
    }

    [Fact]
    public void InvalidateStaged_UnknownStatus_Rejected()
    {
        using var db = TestDb.Create();
        var opp = SeedOpportunity(db, 1, "kaputt", DateTime.UtcNow.AddHours(1));

        var ok = CreateService(db).InvalidateStaged(opp, "x", DateTime.UtcNow);

        Assert.False(ok);
        Assert.Equal("kaputt", opp.Status);
        Assert.Empty(db.TradeStatusChanges.Local);
    }
}