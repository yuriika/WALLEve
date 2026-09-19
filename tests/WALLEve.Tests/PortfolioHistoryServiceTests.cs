using Microsoft.EntityFrameworkCore;
using WALLEve.Data;
using WALLEve.Models.Database;
using WALLEve.Models.Holdings;
using WALLEve.Models.Portfolio;
using WALLEve.Services.Portfolio;

namespace WALLEve.Tests;

/// <summary>
/// Regression-Tests für Issue #38 (Historien-Read-Models ohne Doppelzählung):
/// <list type="bullet">
/// <item>AC #38-1: kein Punkt aus Partial-Sync; Owner und historische Marktprovenienz
/// bleiben erhalten (idempotent, spätere Preise/Hub-Wechsel überschreiben nie).</item>
/// <item>AC #38-2: Fixture mit Escrow/Sell-Orders beweist keine Doppelzählung;
/// unbekannte Basis steht separat.</item>
/// <item>AC #38-3: realisierter Gewinn nur aus belegten, reconcilierten
/// Ledger-Ereignissen; sonst unknown (null).</item>
/// </list>
/// Deterministisch, ohne Live-ESI und ohne SDE-Datei.
/// </summary>
public class PortfolioHistoryServiceTests
{
    private const int CharacterA = 90073315;
    private const int CharacterB = 90073316;
    private const int RegionJita = 10000002;

    private static readonly DateTime SyncedAt = new(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);

    private static PortfolioHistoryService CreateService(WalletDbContext db,
        Func<IReadOnlyCollection<int>, Task<Dictionary<int, string?>>>? categoryResolver = null)
        => new(db, categoryResolver);

    /// <summary>Vollständigen Character-Holdings-Snapshot anlegen; liefert seine Id.</summary>
    private static Task<long> CreateSnapshotAsync(WalletDbContext db, int ownerId,
        params (int TypeId, int Quantity, long LocationId, string LocationFlag)[] items)
        => CreateSnapshotAsync(db, ownerId, OwnerType.Character, SyncedAt, items);

    /// <summary>Vollständigen Holdings-Snapshot anlegen; liefert seine Id.</summary>
    private static async Task<long> CreateSnapshotAsync(WalletDbContext db, int ownerId,
        OwnerType ownerType, DateTime? syncedAt,
        params (int TypeId, int Quantity, long LocationId, string LocationFlag)[] items)
    {
        var run = new HoldingSyncRun
        {
            OwnerType = ownerType,
            OwnerId = ownerId,
            StartedAt = syncedAt ?? SyncedAt,
            Status = "completed",
            Snapshots =
            {
                new HoldingSnapshot
                {
                    OwnerType = ownerType,
                    OwnerId = ownerId,
                    SyncedAt = syncedAt ?? SyncedAt,
                    Source = $"esi/{(ownerType == OwnerType.Character ? "characters" : "corporations")}/{ownerId}/assets",
                    Items = items.Select((it, i) => new HoldingItem
                    {
                        ItemId = 10000 + i,
                        TypeId = it.TypeId,
                        Quantity = it.Quantity,
                        IsSingleton = false,
                        LocationId = it.LocationId,
                        LocationFlag = it.LocationFlag
                    }).ToList()
                }
            }
        };
        db.HoldingSyncRuns.Add(run);
        await db.SaveChangesAsync();
        return run.Snapshots.Single().Id;
    }

    private static async Task AddHubAsync(WalletDbContext db, int regionId, string name, bool active = true)
    {
        db.MarketHubProfiles.Add(new MarketHubProfile
        {
            Name = name,
            RegionId = regionId,
            SystemId = regionId + 9000,
            IsActiveHub = active,
            UpdatedAt = SyncedAt
        });
        await db.SaveChangesAsync();
    }

    private static void AddPrice(WalletDbContext db, int typeId, double price, DateTime timestamp)
    {
        db.MarketSnapshots.Add(new MarketSnapshot
        {
            RegionId = RegionJita,
            TypeId = typeId,
            Timestamp = timestamp,
            BestSellPrice = price
        });
    }

    private static void AddBasis(WalletDbContext db, int characterId, int typeId, double? value)
    {
        db.CostBasisEntries.Add(new CostBasisEntry
        {
            CharacterId = characterId,
            TypeId = typeId,
            Value = value,
            Source = value.HasValue ? CostBasisSource.Transaction : CostBasisSource.None,
            UpdatedAt = SyncedAt
        });
    }

    private static void AddLedgerEvent(WalletDbContext db, int characterId, int typeId,
        long sourceId, DateTime date, bool isBuy, int quantity, double unitPrice)
    {
        db.CostBasisLedgerEntries.Add(new CostBasisLedgerEntry
        {
            CharacterId = characterId,
            TypeId = typeId,
            SourceTransactionId = sourceId,
            Date = date,
            IsBuy = isBuy,
            Quantity = quantity,
            UnitPrice = unitPrice,
            ImportedAt = SyncedAt
        });
    }

    private static void AddWalletTransaction(WalletDbContext db, int characterId, int typeId,
        long transactionId, DateTime date, bool isBuy, int quantity, double unitPrice)
    {
        db.WalletTransactionRecords.Add(new WalletTransactionRecord
        {
            CharacterId = characterId,
            TypeId = typeId,
            TransactionId = transactionId,
            Date = date,
            IsBuy = isBuy,
            IsPersonal = true,
            JournalRefId = transactionId + 500000,
            LocationId = 60000000,
            Quantity = quantity,
            UnitPrice = unitPrice
        });
    }

    // ---- AC #38-1: kein Punkt aus Partial-Sync ----

    [Fact]
    public async Task Evaluate_PartialSyncRun_ThrowsWithoutCreatingPoint()
    {
        var db = TestDb.Create();
        var service = CreateService(db);

        var run = new HoldingSyncRun
        {
            OwnerType = OwnerType.Character,
            OwnerId = CharacterA,
            StartedAt = SyncedAt,
            Status = "running",
            Snapshots =
            {
                new HoldingSnapshot
                {
                    OwnerType = OwnerType.Character,
                    OwnerId = CharacterA,
                    SyncedAt = SyncedAt,
                    Source = $"esi/characters/{CharacterA}/assets",
                    Items =
                    {
                        new HoldingItem { ItemId = 1, TypeId = 34, Quantity = 5, IsSingleton = false, LocationId = 60000000, LocationFlag = "Hangar" }
                    }
                }
            }
        };
        db.HoldingSyncRuns.Add(run);
        await db.SaveChangesAsync();
        var snapshotId = run.Snapshots.Single().Id;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.EvaluateAsync(snapshotId));
        Assert.Contains("Partial-Sync", ex.Message);
        Assert.Equal(0, await db.PortfolioHistoryPoints.CountAsync());
    }

    [Fact]
    public async Task Evaluate_FailedSyncRun_ThrowsWithoutCreatingPoint()
    {
        var db = TestDb.Create();
        var service = CreateService(db);

        var run = new HoldingSyncRun
        {
            OwnerType = OwnerType.Character,
            OwnerId = CharacterA,
            StartedAt = SyncedAt,
            Status = "failed",
            Snapshots =
            {
                new HoldingSnapshot
                {
                    OwnerType = OwnerType.Character,
                    OwnerId = CharacterA,
                    SyncedAt = SyncedAt,
                    Source = $"esi/characters/{CharacterA}/assets",
                    Items =
                    {
                        new HoldingItem { ItemId = 2, TypeId = 34, Quantity = 5, IsSingleton = false, LocationId = 60000000, LocationFlag = "Hangar" }
                    }
                }
            }
        };
        db.HoldingSyncRuns.Add(run);
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.EvaluateAsync(run.Snapshots.Single().Id));
        Assert.Contains("Partial-Sync", ex.Message);
        Assert.Equal(0, await db.PortfolioHistoryPoints.CountAsync());
    }

    [Fact]
    public async Task Evaluate_MissingSnapshot_Throws()
    {
        var db = TestDb.Create();
        var service = CreateService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EvaluateAsync(424242));
        Assert.Equal(0, await db.PortfolioHistoryPoints.CountAsync());
    }

    // ---- AC #38-1: Owner und historische Marktprovenienz bleiben erhalten ----

    [Fact]
    public async Task Evaluate_CapturesOwnerAndImmutablyFreezesMarketProvenance()
    {
        var db = TestDb.Create();
        await AddHubAsync(db, RegionJita, "Jita");
        AddPrice(db, 34, 100.0, SyncedAt.AddHours(-1));
        await db.SaveChangesAsync();

        var sourceId = await CreateSnapshotAsync(db, CharacterA, items: (34, 5, 60000000, "Hangar"));
        var service = CreateService(db);

        var first = await service.EvaluateAsync(sourceId);

        Assert.Equal(CharacterA, first.Point.OwnerId);
        Assert.Equal(OwnerType.Character, first.Point.OwnerType);
        Assert.Equal(SyncedAt, first.Point.CapturedAt);          // historischer Anker = Sync-Zeit
        Assert.Equal(RegionJita, first.Point.ValuationRegionId); // Provenienz: Hub-Region
        Assert.Equal("Jita", first.Point.ValuationHubName);
        Assert.Equal(500.0, first.Point.AssetsValue);
        Assert.Equal(1, first.Point.ValuatedTypeCount);

        // Späterer Preis im selben Markt darf den eingefrorenen Punkt nicht ändern.
        AddPrice(db, 34, 999.0, SyncedAt.AddHours(1));
        await db.SaveChangesAsync();

        var again = await service.EvaluateAsync(sourceId);
        Assert.Equal(first.Point.Id, again.Point.Id);
        Assert.Equal(500.0, again.Point.AssetsValue);
        Assert.Equal(1, await db.PortfolioHistoryPoints.CountAsync());
    }

    [Fact]
    public async Task Evaluate_HubSwitchDoesNotOverwriteHistoricalProvenance()
    {
        var db = TestDb.Create();
        await AddHubAsync(db, RegionJita, "Jita");
        AddPrice(db, 34, 100.0, SyncedAt.AddHours(-1));
        await db.SaveChangesAsync();

        var sourceId = await CreateSnapshotAsync(db, CharacterA, items: (34, 5, 60000000, "Hangar"));
        var service = CreateService(db);

        var first = await service.EvaluateAsync(sourceId);
        Assert.Equal(RegionJita, first.Point.ValuationRegionId);
        Assert.Equal(500.0, first.Point.AssetsValue);

        // Marktwechsel: alter Hub inaktiv, neuer Hub aktiv → bestehender Punkt
        // behält seine eingefrorene Region und seinen Wert (AC: Marktwechsel
        // überschreibt historische Herkunft nicht).
        var oldHub = await db.MarketHubProfiles.SingleAsync();
        oldHub.IsActiveHub = false;
        await db.SaveChangesAsync();
        await AddHubAsync(db, 10000003, "Amarr");
        db.MarketSnapshots.Add(new MarketSnapshot
        {
            RegionId = 10000003,
            TypeId = 34,
            Timestamp = SyncedAt.AddHours(-1),
            BestSellPrice = 7.0
        });
        await db.SaveChangesAsync();

        var after = await service.EvaluateAsync(sourceId);
        Assert.Equal(first.Point.Id, after.Point.Id);
        Assert.Equal(RegionJita, after.Point.ValuationRegionId);
        Assert.Equal("Jita", after.Point.ValuationHubName);
        Assert.Equal(500.0, after.Point.AssetsValue);
        Assert.Equal(1, await db.PortfolioHistoryPoints.CountAsync());
    }

    [Fact]
    public async Task Evaluate_UsesLatestAsOfPriceNotLaterQuotes()
    {
        var db = TestDb.Create();
        await AddHubAsync(db, RegionJita, "Jita");
        AddPrice(db, 34, 90.0, SyncedAt.AddDays(-2));
        AddPrice(db, 34, 100.0, SyncedAt.AddHours(-1));
        AddPrice(db, 34, 200.0, SyncedAt.AddHours(2)); // nach CapturedAt → irrelevant
        await db.SaveChangesAsync();

        var sourceId = await CreateSnapshotAsync(db, CharacterA, items: (34, 5, 60000000, "Hangar"));
        var point = (await CreateService(db).EvaluateAsync(sourceId)).Point;

        Assert.Equal(500.0, point.AssetsValue); // 5 × 100 (neuester As-of-Quote)
        Assert.Equal(1, point.ValuatedTypeCount);
        Assert.Equal(0, point.UnknownValuationTypeCount);
    }

    [Fact]
    public async Task Evaluate_NoActiveHub_UsesDefaultReferenceMarket()
    {
        var db = TestDb.Create();
        AddPrice(db, 34, 100.0, SyncedAt.AddHours(-1));
        await db.SaveChangesAsync();

        var sourceId = await CreateSnapshotAsync(db, CharacterA, items: (34, 5, 60000000, "Hangar"));
        var point = (await CreateService(db).EvaluateAsync(sourceId)).Point;

        Assert.Equal(RegionJita, point.ValuationRegionId);
        Assert.Equal("Referenzmarkt (Region 10000002)", point.ValuationHubName);
        Assert.Equal(500.0, point.AssetsValue);
        Assert.Equal(0, point.UnknownValuationQuantity);
        Assert.Equal(0, point.UnknownValuationItemCount);
        Assert.Equal(0, point.UnknownValuationTypeCount);
    }

    // ---- AC #38-2: Escrow/Sell-Orders keine Doppelzählung; unbekannte Basis separat ----

    [Fact]
    public async Task Evaluate_EscrowSellOrderItems_NoDoubleCounting()
    {
        var db = TestDb.Create();
        await AddHubAsync(db, RegionJita, "Jita");
        AddPrice(db, 34, 100.0, SyncedAt.AddHours(-1));
        await db.SaveChangesAsync();

        // Fixture: 120 physisch (Hangar) + 30 in Sell-Order gebunden (CorpSellOrder).
        var sourceId = await CreateSnapshotAsync(db, CharacterA,
            (34, 120, 60000000, "Hangar"),
            (34, 30, 60000001, "CorpSellOrder"));
        var eval = await CreateService(db).EvaluateAsync(sourceId);
        var point = eval.Point;

        // Mengengleichheit: kein Item doppelt gezählt (120 frei + 30 gebunden = 150).
        Assert.Equal(120, point.AssetsQuantity);
        Assert.Equal(12000.0, point.AssetsValue);
        Assert.Equal(30, point.EscrowQuantity);
        Assert.Equal(3000.0, point.EscrowValue);
        Assert.Equal(1, point.EscrowItemCount);
        Assert.Equal(150, point.AssetsQuantity + point.EscrowQuantity);

        // Auch die Ort-/Kategorie-Projektionen bleiben mengengleich: Summe aller
        // Zeilen (frei + gebunden) == Gesamtmenge des Snapshots.
        Assert.Equal(150, eval.Locations.Sum(l => l.Quantity + l.EscrowQuantity));
        Assert.Equal(150, eval.Categories.Sum(c => c.Quantity + c.EscrowQuantity));
        Assert.Equal(1, eval.Locations.Count(l => l.LocationFlag == "CorpSellOrder"));
        Assert.Equal(30, eval.Locations.Single(l => l.LocationFlag == "CorpSellOrder").EscrowQuantity);
    }

    [Fact]
    public async Task Evaluate_UnknownBasisIsSeparateBucket()
    {
        var db = TestDb.Create();
        await AddHubAsync(db, RegionJita, "Jita");
        AddPrice(db, 34, 100.0, SyncedAt.AddHours(-1));
        AddPrice(db, 35, 20.0, SyncedAt.AddHours(-1));
        AddBasis(db, CharacterA, 34, 50.0); // Typ 34 hat Basis, Typ 35 nicht
        await db.SaveChangesAsync();

        var sourceId = await CreateSnapshotAsync(db, CharacterA, (34, 10, 60000000, "Hangar"), (35, 5, 60000000, "Hangar"));
        var point = (await CreateService(db).EvaluateAsync(sourceId)).Point;

        Assert.Equal(500.0, point.KnownBasisValue);           // 10 × 50
        Assert.Equal(1, point.CostBasisKnownItemCount);
        Assert.Equal(1, point.UnknownCostBasisItemCount);     // Typ 35 separat
        Assert.Equal(5, point.UnknownBasisQuantity);
        Assert.Equal(100.0, point.UnknownBasisMarketValue);   // Marktwert ohne Basis: 5 × 20
        Assert.Equal(1100.0, point.AssetsValue);              // Bewertung unabhängig von der Basis: 10×100 + 5×20
    }

    [Fact]
    public async Task Evaluate_CorporationOwner_EverythingUnknownBasisRealizedAndCashflow()
    {
        var db = TestDb.Create();
        await AddHubAsync(db, RegionJita, "Jita");
        AddPrice(db, 34, 100.0, SyncedAt.AddHours(-1));
        AddBasis(db, CharacterA, 34, 50.0); // Basis eines ANDEREN (Character-)Owners
        AddLedgerEvent(db, CharacterA, 34, 1001, SyncedAt.AddDays(-1), true, 10, 50.0);
        await db.SaveChangesAsync();

        var sourceId = await CreateSnapshotAsync(db, 98000001, OwnerType.Corporation, SyncedAt,
            (34, 5, 60000000, "CorpSAG2"));
        var point = (await CreateService(db).EvaluateAsync(sourceId)).Point;

        Assert.Equal(500.0, point.AssetsValue);      // Bewertung funktioniert weiter
        Assert.Equal(0, point.CostBasisKnownItemCount);
        Assert.Equal(1, point.UnknownCostBasisItemCount);  // Basis-Konzept nur für Character
        Assert.Equal(5, point.UnknownBasisQuantity);
        Assert.Null(point.KnownBasisValue);
        Assert.Null(point.RealizedProfit);           // kein belegter Character-Ledger
        Assert.Null(point.WalletCashInflow);
        Assert.Null(point.WalletCashOutflow);
    }

    // ---- AC #38-3: realisierter Gewinn nur aus belegten reconcilierten Daten ----

    [Fact]
    public async Task Evaluate_RealizedProfit_OnlyFromEvidencedLedgerReplay()
    {
        var db = TestDb.Create();
        await AddHubAsync(db, RegionJita, "Jita");
        AddPrice(db, 34, 100.0, SyncedAt.AddHours(-1));
        // Belegte, reconciliierte Ereignisse: Kauf 10 @ 100, Verkauf 4 @ 150
        // → realisiert = 4 × (150 − 100) = 200.
        AddLedgerEvent(db, CharacterA, 34, 1001, SyncedAt.AddDays(-2), true, 10, 100.0);
        AddLedgerEvent(db, CharacterA, 34, 1002, SyncedAt.AddDays(-1), false, 4, 150.0);
        await db.SaveChangesAsync();

        var sourceId = await CreateSnapshotAsync(db, CharacterA, (34, 10, 60000000, "Hangar"));
        var point = (await CreateService(db).EvaluateAsync(sourceId)).Point;

        Assert.NotNull(point.RealizedProfit);
        Assert.Equal(200.0, point.RealizedProfit);
    }

    [Fact]
    public async Task Evaluate_NoEvidencedEvents_RealizedProfitIsUnknown()
    {
        var db = TestDb.Create();
        await AddHubAsync(db, RegionJita, "Jita");
        AddPrice(db, 34, 100.0, SyncedAt.AddHours(-1));
        await db.SaveChangesAsync();

        var sourceId = await CreateSnapshotAsync(db, CharacterA, (34, 10, 60000000, "Hangar"));
        var point = (await CreateService(db).EvaluateAsync(sourceId)).Point;

        Assert.Null(point.RealizedProfit); // kein belegtes Ereignis → unknown, nicht 0

        // Ereignis NACH CapturedAt zählt für den historischen Punkt nicht.
        AddLedgerEvent(db, CharacterA, 34, 1999, SyncedAt.AddDays(1), false, 4, 150.0);
        await db.SaveChangesAsync();
        var again = (await CreateService(db).EvaluateAsync(sourceId)).Point;
        Assert.Null(again.RealizedProfit);
    }

    // ---- Cashflow getrennt von Bewertung/realisiertem Ergebnis ----

    [Fact]
    public async Task Evaluate_WalletCashflowSeparateFromValuation()
    {
        var db = TestDb.Create();
        await AddHubAsync(db, RegionJita, "Jita");
        AddPrice(db, 34, 100.0, SyncedAt.AddHours(-1));
        AddWalletTransaction(db, CharacterA, 34, 7001, SyncedAt.AddDays(-3), isBuy: true, 10, 90.0);
        AddWalletTransaction(db, CharacterA, 34, 7002, SyncedAt.AddDays(-2), isBuy: false, 2, 150.0);
        await db.SaveChangesAsync();

        var sourceId = await CreateSnapshotAsync(db, CharacterA, (34, 8, 60000000, "Hangar"));
        var point = (await CreateService(db).EvaluateAsync(sourceId)).Point;

        Assert.Equal(900.0, point.WalletCashOutflow);            // 10 × 90 (Kauf)
        Assert.Equal(300.0, point.WalletCashInflow);             // 2 × 150 (Verkauf)
        Assert.Equal(2, point.WalletTransactionCount);
        Assert.Equal(800.0, point.AssetsValue);                  // Bewertung bleibt 8 × 100
    }

    // ---- Idempotenz + Owner-Isolation ----

    [Fact]
    public async Task Evaluate_SameSourceSnapshot_DoesNotDuplicateHistory()
    {
        var db = TestDb.Create();
        await AddHubAsync(db, RegionJita, "Jita");
        AddPrice(db, 34, 100.0, SyncedAt.AddHours(-1));
        await db.SaveChangesAsync();

        var sourceId = await CreateSnapshotAsync(db, CharacterA, (34, 1, 60000000, "Hangar"));
        var service = CreateService(db);

        var first = await service.EvaluateAsync(sourceId);
        var second = await service.EvaluateAsync(sourceId);

        Assert.Equal(first.Point.Id, second.Point.Id);
        Assert.Equal(1, await db.PortfolioHistoryPoints.CountAsync());
    }

    [Fact]
    public async Task Evaluate_OwnerIsolation_BasisAndLedgerDoNotLeak()
    {
        var db = TestDb.Create();
        await AddHubAsync(db, RegionJita, "Jita");
        AddPrice(db, 34, 100.0, SyncedAt.AddHours(-1));
        AddBasis(db, CharacterA, 34, 50.0);
        AddLedgerEvent(db, CharacterA, 34, 1001, SyncedAt.AddDays(-2), true, 10, 100.0);
        AddLedgerEvent(db, CharacterA, 34, 1002, SyncedAt.AddDays(-1), false, 4, 150.0);
        await db.SaveChangesAsync();

        var sourceB = await CreateSnapshotAsync(db, CharacterB, (34, 5, 60000000, "Hangar"));
        var pointB = (await CreateService(db).EvaluateAsync(sourceB)).Point;

        Assert.Equal(CharacterB, pointB.OwnerId);
        Assert.Null(pointB.KnownBasisValue);      // Basis von A zählt nicht für B
        Assert.Equal(1, pointB.UnknownCostBasisItemCount);
        Assert.Null(pointB.RealizedProfit);       // Ledger von A zählt nicht für B
    }

    // ---- Kategorie-Projektion ----

    [Fact]
    public async Task Evaluate_CategoryProjection_UsesResolverLabelsAndUnknownFallback()
    {
        var db = TestDb.Create();
        await AddHubAsync(db, RegionJita, "Jita");
        AddPrice(db, 34, 100.0, SyncedAt.AddHours(-1));
        AddPrice(db, 35, 20.0, SyncedAt.AddHours(-1));
        await db.SaveChangesAsync();

        var sourceId = await CreateSnapshotAsync(db, CharacterA, (34, 10, 60000000, "Hangar"), (35, 5, 60000000, "Hangar"));
        var eval = await CreateService(db,
                typeIds => Task.FromResult(typeIds.Where(id => id == 34).ToDictionary(id => id, _ => (string?)"Rohstoffe")))
            .EvaluateAsync(sourceId);

        Assert.Equal(2, eval.Categories.Count);
        var raw = eval.Categories.Single(c => c.Category == "Rohstoffe");
        Assert.Equal(10, raw.Quantity);
        Assert.Equal(1000.0, raw.Value);
        var unknown = eval.Categories.Single(c => c.Category == "Unbekannt");
        Assert.Equal(5, unknown.Quantity);
        Assert.Equal(100.0, unknown.Value);
    }

    // ---- Review #157, Blocker 2: Mengen unabhängig vom Quote-Bestand ----

    [Fact]
    public async Task Evaluate_QuantityCountedWithoutQuote_OnlyValuesUnknown()
    {
        var db = TestDb.Create();
        await AddHubAsync(db, RegionJita, "Jita");
        AddPrice(db, 35, 20.0, SyncedAt.AddHours(-1)); // Quote nur für Typ 35
        await db.SaveChangesAsync();

        var sourceId = await CreateSnapshotAsync(db, CharacterA,
            (34, 5, 60000000, "Hangar"),
            (34, 3, 60000001, "CorpSellOrder"),
            (35, 2, 60000000, "Hangar"));
        var point = (await CreateService(db).EvaluateAsync(sourceId)).Point;

        // Mengen zählen immer — unabhängig davon, ob ein As-of-Quote existiert.
        Assert.Equal(7, point.AssetsQuantity);          // 5 + 2 frei
        Assert.Equal(3, point.EscrowQuantity);          // gebunden ohne Quote zählt
        Assert.Equal(10, point.AssetsQuantity + point.EscrowQuantity); // Gesamtmenge rekonstruierbar

        // Nur die Wertfelder bleiben unknown.
        Assert.Equal(40.0, point.AssetsValue);           // nur 2 × 20 bewertbar
        Assert.Null(point.EscrowValue);
        Assert.Equal(8, point.UnknownValuationQuantity); // 5 + 3 ohne Quote
        Assert.Equal(2, point.UnknownValuationItemCount);
        Assert.Equal(1, point.UnknownValuationTypeCount);
    }

    // ---- Review #157, Blocker 1: Projektionen eingefrorener Punkte ----

    [Fact]
    public async Task Evaluate_BackfilledAsOfQuote_DoesNotChangeFrozenProjections()
    {
        var db = TestDb.Create();
        await AddHubAsync(db, RegionJita, "Jita");
        AddPrice(db, 34, 100.0, SyncedAt.AddHours(-1));
        await db.SaveChangesAsync();

        var sourceId = await CreateSnapshotAsync(db, CharacterA,
            (34, 10, 60000000, "Hangar"),
            (34, 5, 60000001, "CorpSellOrder"));
        var service = CreateService(db);

        var first = await service.EvaluateAsync(sourceId);
        Assert.Equal(1000.0, first.Locations.Single(l => l.LocationFlag == "Hangar").Value);
        Assert.Equal(500.0, first.Locations.Single(l => l.LocationFlag == "CorpSellOrder").EscrowValue);

        // Backfill: NEUER Quote mit Timestamp ≤ CapturedAt (zulässiger As-of)
        // und anderem Preis — darf weder Punkt noch Projektionen ändern.
        AddPrice(db, 34, 7.0, SyncedAt);
        await db.SaveChangesAsync();

        var again = await service.EvaluateAsync(sourceId);
        Assert.Equal(first.Point.Id, again.Point.Id);
        Assert.Equal(1000.0, again.Locations.Single(l => l.LocationFlag == "Hangar").Value);
        Assert.Equal(10, again.Locations.Single(l => l.LocationFlag == "Hangar").Quantity);
        Assert.Equal(500.0, again.Locations.Single(l => l.LocationFlag == "CorpSellOrder").EscrowValue);
        Assert.Equal(1000.0, again.Point.AssetsValue); // Punkt-Aggregat ebenfalls eingefroren
        Assert.Equal(2, await db.PortfolioHistoryLocations.CountAsync()); // keine neuen Zeilen
    }

    // ---- Review #157, Zusätzlich: gebündelte Kategorieauflösung (kein N+1) ----

    [Fact]
    public async Task Evaluate_CategoryResolver_InvokedOnceWithAllTypeIds()
    {
        var db = TestDb.Create();
        await AddHubAsync(db, RegionJita, "Jita");
        AddPrice(db, 34, 100.0, SyncedAt.AddHours(-1));
        AddPrice(db, 35, 20.0, SyncedAt.AddHours(-1));
        await db.SaveChangesAsync();

        var sourceId = await CreateSnapshotAsync(db, CharacterA,
            (34, 10, 60000000, "Hangar"),
            (35, 5, 60000000, "Hangar"));

        var invocationCount = 0;
        var eval = await CreateService(db,
                typeIds =>
                {
                    invocationCount++;
                    return Task.FromResult(typeIds.Where(id => id == 34).ToDictionary(id => id, _ => (string?)"Rohstoffe"));
                })
            .EvaluateAsync(sourceId);

        Assert.Equal(1, invocationCount); // EIN gebündelter Aufruf für alle TypeIds
        Assert.Equal(2, eval.Categories.Count);
        Assert.Equal(10, eval.Categories.Single(c => c.Category == "Rohstoffe").Quantity);
        Assert.Equal(5, eval.Categories.Single(c => c.Category == "Unbekannt").Quantity);
    }

    // ---- Issue #47: Wertverlauf (GetHistoryAsync) und Drill-down (GetPointAsync) ----

    [Fact]
    public async Task History_ReturnsFrozenPointsInTimeOrder_GapsStayVisible()
    {
        var db = TestDb.Create();
        await AddHubAsync(db, RegionJita, "Jita");
        AddPrice(db, 34, 100.0, SyncedAt.AddDays(-4));
        AddPrice(db, 34, 120.0, SyncedAt.AddHours(-1));
        await db.SaveChangesAsync();

        // Zwei vollständige Snapshots mit 3 Tagen Sync-Abstand — dazwischen gibt
        // es bewusst keinen Punkt: fehlende Zeiträume bleiben als Lücke sichtbar.
        var early = await CreateSnapshotAsync(db, CharacterA, OwnerType.Character, SyncedAt.AddDays(-3),
            (34, 5, 60000000, "Hangar"));
        var late = await CreateSnapshotAsync(db, CharacterA, OwnerType.Character, SyncedAt,
            (34, 5, 60000000, "Hangar"));
        var service = CreateService(db);
        var p1 = (await service.EvaluateAsync(early)).Point;
        var p2 = (await service.EvaluateAsync(late)).Point;

        var history = await service.GetHistoryAsync(OwnerType.Character, CharacterA);

        Assert.Equal(2, history.Count); // exakt die persistierten Punkte — nichts erfunden
        Assert.Equal(p1.Id, history[0].Id);
        Assert.Equal(p2.Id, history[1].Id);
        Assert.True(history[0].CapturedAt < history[1].CapturedAt); // älteste zuerst
        Assert.Equal(SyncedAt.AddDays(-3), history[0].CapturedAt);  // historischer Anker bleibt
    }

    [Fact]
    public async Task History_FiltersByOwner_OtherOwnersNotVisible()
    {
        var db = TestDb.Create();
        await AddHubAsync(db, RegionJita, "Jita");
        AddPrice(db, 34, 100.0, SyncedAt.AddHours(-1));
        await db.SaveChangesAsync();

        var sourceA = await CreateSnapshotAsync(db, CharacterA, (34, 5, 60000000, "Hangar"));
        var sourceB = await CreateSnapshotAsync(db, CharacterB, (34, 2, 60000000, "Hangar"));
        var service = CreateService(db);
        var pA = (await service.EvaluateAsync(sourceA)).Point;
        var pB = (await service.EvaluateAsync(sourceB)).Point;

        // Charakterfilter: die Historie von A enthält ausschließlich A-Punkte.
        var history = await service.GetHistoryAsync(OwnerType.Character, CharacterA);

        Assert.Single(history);
        Assert.Equal(pA.Id, history[0].Id);
        Assert.NotEqual(pA.Id, pB.Id);
    }

    [Fact]
    public async Task History_MarketSwitch_KeepsOldPointsFrozenAndShowsNewOnes()
    {
        var db = TestDb.Create();
        await AddHubAsync(db, RegionJita, "Jita");
        AddPrice(db, 34, 100.0, SyncedAt.AddDays(-4)); // As-of ≤ CapturedAt des alten Punkts
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var oldSource = await CreateSnapshotAsync(db, CharacterA, OwnerType.Character, SyncedAt.AddDays(-3),
            (34, 5, 60000000, "Hangar"));
        var old = (await service.EvaluateAsync(oldSource)).Point;
        Assert.Equal(RegionJita, old.ValuationRegionId);
        Assert.Equal(500.0, old.AssetsValue);

        // Marktwechsel: Jita inaktiv, Amarr aktiv — ein späterer Sync bewertet neu,
        // darf aber die eingefrorene Provenienz des alten Punkts nie umschreiben.
        var jita = await db.MarketHubProfiles.SingleAsync();
        jita.IsActiveHub = false;
        await db.SaveChangesAsync();
        await AddHubAsync(db, 10000003, "Amarr");
        db.MarketSnapshots.Add(new MarketSnapshot
        {
            RegionId = 10000003,
            TypeId = 34,
            Timestamp = SyncedAt.AddHours(-1),
            BestSellPrice = 7.0
        });
        await db.SaveChangesAsync();

        var newSource = await CreateSnapshotAsync(db, CharacterA, OwnerType.Character, SyncedAt,
            (34, 5, 60000000, "Hangar"));
        var fresh = (await service.EvaluateAsync(newSource)).Point;
        Assert.Equal(10000003, fresh.ValuationRegionId);
        Assert.Equal(35.0, fresh.AssetsValue); // 5 × 7 am neuen Markt

        // Die Historie zeigt beide Punkte; der alte behält Markt, Region und Wert.
        var history = await service.GetHistoryAsync(OwnerType.Character, CharacterA);
        Assert.Equal(2, history.Count);
        Assert.Equal(old.Id, history[0].Id);
        Assert.Equal(RegionJita, history[0].ValuationRegionId);
        Assert.Equal("Jita", history[0].ValuationHubName);
        Assert.Equal(500.0, history[0].AssetsValue);
        Assert.Equal(10000003, history[1].ValuationRegionId);
    }

    [Fact]
    public async Task Point_DrillDown_ReturnsFrozenPointWithLocationAndCategoryProjections()
    {
        var db = TestDb.Create();
        await AddHubAsync(db, RegionJita, "Jita");
        AddPrice(db, 34, 100.0, SyncedAt.AddHours(-1));
        await db.SaveChangesAsync();

        var sourceId = await CreateSnapshotAsync(db, CharacterA,
            (34, 10, 60000000, "Hangar"),
            (34, 5, 60000001, "CorpSellOrder"));
        var evaluated = await CreateService(db,
                typeIds => Task.FromResult(typeIds.Where(id => id == 34).ToDictionary(id => id, _ => (string?)"Rohstoffe")))
            .EvaluateAsync(sourceId);

        // Frischer Service: der Drill-down liest die PERSISTIERTEN Projektionen
        // aus der Datenbank — nicht den Evaluations-Rückgabewert.
        var detail = await CreateService(db).GetPointAsync(evaluated.Point.Id);

        Assert.NotNull(detail);
        Assert.Equal(evaluated.Point.Id, detail!.Point.Id);
        Assert.NotNull(detail.Point.SourceSnapshot); // Evidenz: zum Quell-Snapshot navigierbar
        Assert.Equal(2, detail.Locations.Count);
        var hangar = detail.Locations.Single(l => l.LocationFlag == "Hangar");
        Assert.Equal(10, hangar.Quantity);
        Assert.Equal(1000.0, hangar.Value);
        var escrow = detail.Locations.Single(l => l.LocationFlag == "CorpSellOrder");
        Assert.Equal(5, escrow.EscrowQuantity);
        Assert.Equal(500.0, escrow.EscrowValue);
        Assert.Equal(15, detail.Locations.Sum(l => l.Quantity + l.EscrowQuantity)); // mengengleich
        var category = detail.Categories.Single(c => c.Category == "Rohstoffe");
        Assert.Equal(10, category.Quantity);            // freier Bestand
        Assert.Equal(5, category.EscrowQuantity);       // gebunden, getrennt geführt
        Assert.Equal(1000.0, category.Value);
        Assert.Equal(500.0, category.EscrowValue);
        Assert.Equal(15, category.Quantity + category.EscrowQuantity); // mengengleich
        Assert.Single(detail.Categories);               // kein erfundener Kategorie-Eintrag
    }

    [Fact]
    public async Task Point_MissingPoint_ReturnsNull()
    {
        var db = TestDb.Create();
        var service = CreateService(db);

        Assert.Null(await service.GetPointAsync(424242));
    }
}