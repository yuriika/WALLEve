using Microsoft.EntityFrameworkCore;
using WALLEve.Data;
using WALLEve.Models.Database;
using WALLEve.Models.Market;
using WALLEve.Models.Trading;
using WALLEve.Services.Market;
using WALLEve.Services.Trading;

namespace WALLEve.Tests;

/// <summary>
/// Tests der evidenzbasierten Zuordnung von Wallet-Ergebnissen zu Empfehlungen
/// (Issue #60): eindeutiger, mehrdeutiger, partieller und fehlender Match,
/// explizite Links, keine vollständige Doppelzurechnung, idempotenter Replay,
/// getrennte Speicherung von erwarteter Spanne und tatsächlichem Netto,
/// unbekannte Gebühren ohne künstlich exakten Wert sowie manuelle Korrektur
/// mit Historie.
/// </summary>
public class RecommendationAttributionTests
{
    private const int OwnerA = 2001;
    private const int OwnerB = 2002;
    private const int ItemTypeId = 44992;

    private static readonly DateTime WindowStart = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime WindowEnd = WindowStart.AddDays(1);

    private static WalletDbContext CreateDb() => TestDb.Create();

    private static RecommendationAttributionService CreateService(WalletDbContext db)
        => new(db, new FeeCalculatorService());

    /// <summary>
    /// Empfehlung mit belegten Gebühren (Herkunft automatic, inkl. Standings) oder mit
    /// nicht belegter Gebührenherkunft — je nach Testfall. Einzelne Origins können für
    /// Regressionstests gezielt überschrieben werden (z. B. estimated).
    /// </summary>
    private static async Task<TradingOpportunity> SeedOpportunityAsync(
        WalletDbContext db,
        int characterId = OwnerA,
        bool evidencedFees = true,
        FeeInputOrigin? brokerOrigin = null,
        FeeInputOrigin? salesTaxOrigin = null,
        FeeInputOrigin? standingsOrigin = null)
    {
        var broker = brokerOrigin ?? (evidencedFees ? FeeInputOrigin.Automatic : FeeInputOrigin.Unknown);
        var salesTax = salesTaxOrigin ?? (evidencedFees ? FeeInputOrigin.Automatic : FeeInputOrigin.Unknown);
        var standings = standingsOrigin ?? (evidencedFees ? FeeInputOrigin.Automatic : FeeInputOrigin.Unknown);

        var opportunity = new TradingOpportunity
        {
            TypeId = ItemTypeId,
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
            Evidence = "Test: 10 Einheiten, Verkauf 150 ISK",
            DetectedAt = WindowStart,
            ExpiresAt = WindowEnd,
            Status = RecommendationStatus.Active,
            BrokerFeeRate = evidencedFees ? 0.015 : null,
            SalesTaxRate = evidencedFees ? 0.0337 : null,
            BrokerFeeOrigin = broker.StorageValue(),
            SalesTaxOrigin = salesTax.StorageValue(),
            StandingsOrigin = standings.StorageValue(),
            FeeEvaluatedAtUtc = DateTime.UtcNow
        };

        db.TradingOpportunities.Add(opportunity);
        await db.SaveChangesAsync();
        return opportunity;
    }

    private static WalletTransactionRecord Tx(
        long transactionId,
        int quantity,
        double unitPrice = 150,
        bool isBuy = false,
        int typeId = ItemTypeId,
        int characterId = OwnerA,
        DateTime? date = null)
        => new()
        {
            CharacterId = characterId,
            TransactionId = transactionId,
            TypeId = typeId,
            Date = date ?? WindowStart.AddHours(2),
            IsBuy = isBuy,
            IsPersonal = true,
            JournalRefId = 800 + transactionId,
            LocationId = 60003760,
            Quantity = quantity,
            UnitPrice = unitPrice
        };

    [Fact]
    public async Task Attribute_UniqueMatch_LinksTransactionAndKeepsExpectedRangeSeparate()
    {
        await using var db = CreateDb();
        var service = CreateService(db);
        var opportunity = await SeedOpportunityAsync(db);

        var result = await service.AttributeAsync(
            opportunity.Id, OwnerA, TradeSide.Sell, 10, 400m, 600m,
            WindowStart, WindowEnd, [Tx(5001, 10)]);

        Assert.True(result.Success);
        var attribution = result.Attribution!;
        Assert.Equal(AttributionMatchState.Unique, attribution.MatchState);
        Assert.Equal(10, attribution.AttributedQuantity);

        // Erwartete Spanne bleibt gespeichert, das tatsächliche Netto steht daneben.
        Assert.Equal(400m, attribution.ExpectedNetMin);
        Assert.Equal(600m, attribution.ExpectedNetMax);

        // 1500 ISK brutto − 1,5 % Broker (22,50) − 3,37 % Sales Tax (50,55).
        Assert.Equal(1426.95m, attribution.ActualNet!.Value, 2);
        Assert.Equal(AttributionFeeKnowledge.Known, attribution.FeeKnowledge);

        var links = await service.GetLinksAsync(attribution.Id, OwnerA);
        var link = Assert.Single(links);
        Assert.Equal(5001, link.TransactionId);
        Assert.Equal(10, link.Quantity);
        Assert.Equal(TradeSide.Sell, link.Side);
    }

    [Fact]
    public async Task Attribute_PartialMatch_AttributesOnlyEvidencedQuantity()
    {
        await using var db = CreateDb();
        var service = CreateService(db);
        var opportunity = await SeedOpportunityAsync(db);

        var result = await service.AttributeAsync(
            opportunity.Id, OwnerA, TradeSide.Sell, 10, 400m, 600m,
            WindowStart, WindowEnd, [Tx(5002, 4)]);

        var attribution = result.Attribution!;
        Assert.Equal(AttributionMatchState.Partial, attribution.MatchState);
        Assert.Equal(4, attribution.AttributedQuantity);
        Assert.Contains("Partieller Match", attribution.Note);

        // Nur die belegten 4 Einheiten: 600 − 1,5 % (9,00) − 3,37 % (20,22).
        Assert.Equal(570.78m, attribution.ActualNet!.Value, 2);
        Assert.Equal(4, (await service.GetLinksAsync(attribution.Id, OwnerA)).Single().Quantity);
    }

    [Fact]
    public async Task Attribute_AmbiguousMatch_StaysOpenWithoutQuantityOrNet()
    {
        await using var db = CreateDb();
        var service = CreateService(db);
        var opportunity = await SeedOpportunityAsync(db);

        var result = await service.AttributeAsync(
            opportunity.Id, OwnerA, TradeSide.Sell, 10, 400m, 600m,
            WindowStart, WindowEnd, [Tx(5003, 10), Tx(5004, 10)]);

        var attribution = result.Attribution!;
        Assert.Equal(AttributionMatchState.Ambiguous, attribution.MatchState);
        Assert.Equal(0, attribution.AttributedQuantity);
        Assert.Null(attribution.ActualNet);
        Assert.Empty(await service.GetLinksAsync(attribution.Id, OwnerA));
        Assert.Equal(400m, attribution.ExpectedNetMin);
    }

    [Fact]
    public async Task Attribute_MissingMatch_RecordsNoLinksAndNoNet()
    {
        await using var db = CreateDb();
        var service = CreateService(db);
        var opportunity = await SeedOpportunityAsync(db);

        var result = await service.AttributeAsync(
            opportunity.Id, OwnerA, TradeSide.Sell, 10, 400m, 600m, WindowStart, WindowEnd,
            [
                Tx(5005, 10, isBuy: true),                                   // falsche Seite
                Tx(5006, 10, typeId: 34),                                    // falscher Typ
                Tx(5007, 10, characterId: OwnerB),                            // fremder Owner
                Tx(5008, 10, date: WindowEnd.AddHours(1))                    // außerhalb des Zeitfensters
            ]);

        var attribution = result.Attribution!;
        Assert.Equal(AttributionMatchState.Missing, attribution.MatchState);
        Assert.Equal(0, attribution.AttributedQuantity);
        Assert.Null(attribution.ActualNet);
        Assert.Empty(await service.GetLinksAsync(attribution.Id, OwnerA));
    }

    [Fact]
    public async Task Attribute_DoesNotAttributeOneTransactionFullyToTwoRecommendations()
    {
        await using var db = CreateDb();
        var service = CreateService(db);
        var first = await SeedOpportunityAsync(db);
        var second = await SeedOpportunityAsync(db);

        var firstResult = await service.AttributeAsync(
            first.Id, OwnerA, TradeSide.Sell, 10, 400m, 600m, WindowStart, WindowEnd, [Tx(5009, 10)]);
        var secondResult = await service.AttributeAsync(
            second.Id, OwnerA, TradeSide.Sell, 10, 400m, 600m, WindowStart, WindowEnd, [Tx(5009, 10)]);

        Assert.Equal(AttributionMatchState.Unique, firstResult.Attribution!.MatchState);
        // Die Transaktion ist vollständig beansprucht — die zweite Empfehlung bekommt nichts.
        Assert.Equal(AttributionMatchState.Missing, secondResult.Attribution!.MatchState);
        Assert.Equal(0, secondResult.Attribution!.AttributedQuantity);
        Assert.Null(secondResult.Attribution!.ActualNet);

        var links = await db.AttributionTransactionLinks.ToListAsync();
        Assert.Equal(10, links.Sum(l => l.Quantity));
        Assert.Equal(10, await db.RecommendationAttributions.SumAsync(a => a.AttributedQuantity));
    }

    [Fact]
    public async Task Attribute_SecondOpportunityGetsOnlyTheRemainingQuantity()
    {
        await using var db = CreateDb();
        var service = CreateService(db);
        var first = await SeedOpportunityAsync(db);
        var second = await SeedOpportunityAsync(db);

        await service.AttributeAsync(
            first.Id, OwnerA, TradeSide.Sell, 6, 200m, 300m, WindowStart, WindowEnd, [Tx(5010, 10)]);
        var secondResult = await service.AttributeAsync(
            second.Id, OwnerA, TradeSide.Sell, 6, 200m, 300m, WindowStart, WindowEnd, [Tx(5010, 10)]);

        // Restmenge 4 deckt die erwartete Menge 6 nicht mehr vollständig ab.
        Assert.Equal(AttributionMatchState.Partial, secondResult.Attribution!.MatchState);
        Assert.Equal(4, secondResult.Attribution!.AttributedQuantity);
        Assert.Equal(10, await db.AttributionTransactionLinks.SumAsync(l => l.Quantity));
    }

    [Fact]
    public async Task Attribute_ReplayIsIdempotentAndWritesNoDuplicateLinks()
    {
        await using var db = CreateDb();
        var service = CreateService(db);
        var opportunity = await SeedOpportunityAsync(db);
        var transactions = new[] { Tx(5011, 10) };

        var first = await service.AttributeAsync(
            opportunity.Id, OwnerA, TradeSide.Sell, 10, 400m, 600m, WindowStart, WindowEnd, transactions);
        var second = await service.AttributeAsync(
            opportunity.Id, OwnerA, TradeSide.Sell, 10, 400m, 600m, WindowStart, WindowEnd, transactions);

        Assert.Equal(first.Attribution!.Id, second.Attribution!.Id);
        Assert.Equal(1, await db.RecommendationAttributions.CountAsync());
        Assert.Equal(1, await db.AttributionTransactionLinks.CountAsync());
        Assert.Equal(first.Attribution!.ActualNet, second.Attribution!.ActualNet);
        Assert.Equal(AttributionMatchState.Unique, second.Attribution!.MatchState);
    }

    [Fact]
    public async Task Attribute_WithoutEvidencedFees_KeepsActualNetUnknown()
    {
        await using var db = CreateDb();
        var service = CreateService(db);
        var opportunity = await SeedOpportunityAsync(db, evidencedFees: false);

        var result = await service.AttributeAsync(
            opportunity.Id, OwnerA, TradeSide.Sell, 10, 400m, 600m, WindowStart, WindowEnd, [Tx(5012, 10)]);

        var attribution = result.Attribution!;
        Assert.Equal(AttributionMatchState.Unique, attribution.MatchState);
        Assert.Equal(10, attribution.AttributedQuantity);
        Assert.Null(attribution.ActualNet);                                   // keine künstlich exakte Zahl
        Assert.Equal(AttributionFeeKnowledge.Unknown, attribution.FeeKnowledge);
        Assert.Contains("unbekannt", attribution.Note);
        Assert.Equal(400m, attribution.ExpectedNetMin);                        // Erwartung bleibt erhalten
        Assert.Equal(600m, attribution.ExpectedNetMax);
    }

    [Fact]
    public async Task Attribute_EstimatedFees_KeepActualNetUnknown_ForEstimatedOrigin()
    {
        // Review #133: Estimated ist eine konservative Schätzung, kein Beleg —
        // Broker-Origin estimated → kein exaktes Netto, auch wenn die Rate gesetzt ist.
        await using var db = CreateDb();
        var service = CreateService(db);
        var opportunity = await SeedOpportunityAsync(db, brokerOrigin: FeeInputOrigin.Estimated);

        var result = await service.AttributeAsync(
            opportunity.Id, OwnerA, TradeSide.Sell, 10, 400m, 600m, WindowStart, WindowEnd, [Tx(5017, 10)]);

        var attribution = result.Attribution!;
        Assert.Equal(AttributionMatchState.Unique, attribution.MatchState);
        Assert.Equal(10, attribution.AttributedQuantity);
        Assert.Null(attribution.ActualNet);                                   // keine künstlich exakte Zahl
        Assert.Equal(AttributionFeeKnowledge.Unknown, attribution.FeeKnowledge);
        Assert.Contains("geschätzt", attribution.Note);
    }

    [Fact]
    public async Task Attribute_EstimatedSalesTaxOrigin_KeepsActualNetUnknown()
    {
        // Verkauf: Sales-Tax-Origin estimated — der Steuersatz ist nur geschätzt.
        await using var db = CreateDb();
        var service = CreateService(db);
        var opportunity = await SeedOpportunityAsync(db, salesTaxOrigin: FeeInputOrigin.Estimated);

        var result = await service.AttributeAsync(
            opportunity.Id, OwnerA, TradeSide.Sell, 10, 400m, 600m, WindowStart, WindowEnd, [Tx(5018, 10)]);

        var attribution = result.Attribution!;
        Assert.Equal(AttributionMatchState.Unique, attribution.MatchState);
        Assert.Null(attribution.ActualNet);
        Assert.Equal(AttributionFeeKnowledge.Unknown, attribution.FeeKnowledge);
        Assert.Contains("geschätzt", attribution.Note);
    }

    [Fact]
    public async Task Attribute_EstimatedStandingsOrigin_KeepsBuyActualNetUnknown()
    {
        // Review #133: Der Kauf-Pfad ignorierte StandingsOrigin. Standings sind Teil
        // des Broker-Satzes — estimated Standings bei automatic Broker-Rate → kein
        // exaktes Netto.
        await using var db = CreateDb();
        var service = CreateService(db);
        var opportunity = await SeedOpportunityAsync(db, standingsOrigin: FeeInputOrigin.Estimated);

        var result = await service.AttributeAsync(
            opportunity.Id, OwnerA, TradeSide.Buy, 10, 400m, 600m, WindowStart, WindowEnd, [Tx(5019, 10, isBuy: true)]);

        var attribution = result.Attribution!;
        Assert.Equal(AttributionMatchState.Unique, attribution.MatchState);
        Assert.Equal(10, attribution.AttributedQuantity);
        Assert.Null(attribution.ActualNet);
        Assert.Equal(AttributionFeeKnowledge.Unknown, attribution.FeeKnowledge);
        Assert.Contains("geschätzt", attribution.Note);
    }

    [Fact]
    public async Task Attribute_BuyWithAllEvidencedFees_ComputesExactActualNet()
    {
        // Positivfall Kauf: alle Origins automatic inkl. Standings → exaktes Netto.
        // 10 × 150 ISK = 1500 brutto + 1,5 % Broker (22,50) = 1522,50 Abfluss.
        await using var db = CreateDb();
        var service = CreateService(db);
        var opportunity = await SeedOpportunityAsync(db);

        var result = await service.AttributeAsync(
            opportunity.Id, OwnerA, TradeSide.Buy, 10, 400m, 600m, WindowStart, WindowEnd, [Tx(5020, 10, isBuy: true)]);

        var attribution = result.Attribution!;
        Assert.Equal(AttributionMatchState.Unique, attribution.MatchState);
        Assert.Equal(-1522.50m, attribution.ActualNet!.Value, 2);
        Assert.Equal(AttributionFeeKnowledge.Known, attribution.FeeKnowledge);
    }

    [Fact]
    public async Task Attribute_RejectsInvalidRequestsAndForeignOwners()
    {
        await using var db = CreateDb();
        var service = CreateService(db);
        var opportunity = await SeedOpportunityAsync(db);
        var transactions = new[] { Tx(5013, 10) };

        var invalidSide = await service.AttributeAsync(
            opportunity.Id, OwnerA, "hold", 10, 400m, 600m, WindowStart, WindowEnd, transactions);
        var invalidQuantity = await service.AttributeAsync(
            opportunity.Id, OwnerA, TradeSide.Sell, 0, 400m, 600m, WindowStart, WindowEnd, transactions);
        var invalidRange = await service.AttributeAsync(
            opportunity.Id, OwnerA, TradeSide.Sell, 10, 600m, 400m, WindowStart, WindowEnd, transactions);
        var invalidWindow = await service.AttributeAsync(
            opportunity.Id, OwnerA, TradeSide.Sell, 10, 400m, 600m, WindowEnd, WindowStart, transactions);
        var foreignOwner = await service.AttributeAsync(
            opportunity.Id, OwnerB, TradeSide.Sell, 10, 400m, 600m, WindowStart, WindowEnd, transactions);

        Assert.False(invalidSide.Success);
        Assert.False(invalidQuantity.Success);
        Assert.False(invalidRange.Success);
        Assert.False(invalidWindow.Success);
        Assert.False(foreignOwner.Success);
        Assert.Contains("Owner", foreignOwner.Error);

        Assert.Equal(0, await db.RecommendationAttributions.CountAsync());
        Assert.Equal(0, await db.AttributionTransactionLinks.CountAsync());
    }

    [Fact]
    public async Task CorrectActualNet_WritesHistoryAndPreservesExpectedRange()
    {
        await using var db = CreateDb();
        var service = CreateService(db);
        var opportunity = await SeedOpportunityAsync(db);
        var attribution = (await service.AttributeAsync(
            opportunity.Id, OwnerA, TradeSide.Sell, 10, 400m, 600m, WindowStart, WindowEnd, [Tx(5014, 10)])).Attribution!;

        var correction = await service.CorrectActualNetAsync(attribution.Id, OwnerA, 1200m, "Gebühr manuell geprüft");
        Assert.True(correction.Success);
        Assert.Equal(1426.95m, correction.Correction!.PreviousActualNet!.Value, 2);
        Assert.Equal(1200m, correction.Correction!.NewActualNet!.Value);
        Assert.Equal(AttributionSource.User, correction.Attribution!.Source);

        // Erwartete Spanne bleibt unangetastet, das Netto ist der Nutzerwert.
        Assert.Equal(400m, correction.Attribution!.ExpectedNetMin);
        Assert.Equal(600m, correction.Attribution!.ExpectedNetMax);
        Assert.Equal(1200m, correction.Attribution!.ActualNet!.Value);
        Assert.Equal(AttributionFeeKnowledge.Known, correction.Attribution!.FeeKnowledge);

        // Idempotente Wiederholung erzeugt keine zweite Historie.
        var repeated = await service.CorrectActualNetAsync(attribution.Id, OwnerA, 1200m, "gleicher Wert");
        Assert.True(repeated.Success);
        Assert.Null(repeated.Correction);
        Assert.Single(await service.GetCorrectionHistoryAsync(attribution.Id, OwnerA));

        // Zurück auf "unbekannt" ist erlaubt und wird historisiert.
        var reset = await service.CorrectActualNetAsync(attribution.Id, OwnerA, null, "Belege fehlen weiterhin");
        Assert.True(reset.Success);
        Assert.Null(reset.Attribution!.ActualNet);
        Assert.Equal(AttributionFeeKnowledge.Unknown, reset.Attribution!.FeeKnowledge);
        Assert.Equal(2, (await service.GetCorrectionHistoryAsync(attribution.Id, OwnerA)).Count);
    }

    [Fact]
    public async Task CorrectActualNet_RejectsMissingReasonForeignOwnerAndUnknownAttribution()
    {
        await using var db = CreateDb();
        var service = CreateService(db);
        var opportunity = await SeedOpportunityAsync(db);
        var attribution = (await service.AttributeAsync(
            opportunity.Id, OwnerA, TradeSide.Sell, 10, 400m, 600m, WindowStart, WindowEnd, [Tx(5015, 10)])).Attribution!;

        Assert.False((await service.CorrectActualNetAsync(attribution.Id, OwnerA, 900m, "   ")).Success);
        Assert.False((await service.CorrectActualNetAsync(attribution.Id, OwnerB, 900m, "fremd")).Success);
        Assert.False((await service.CorrectActualNetAsync(999_999, OwnerA, 900m, "unbekannt")).Success);
        Assert.Empty(await service.GetCorrectionHistoryAsync(attribution.Id, OwnerA));
    }

    [Fact]
    public async Task Attribute_ReplayPreservesManualCorrection()
    {
        await using var db = CreateDb();
        var service = CreateService(db);
        var opportunity = await SeedOpportunityAsync(db);
        var transactions = new[] { Tx(5016, 10) };

        var first = await service.AttributeAsync(
            opportunity.Id, OwnerA, TradeSide.Sell, 10, 400m, 600m, WindowStart, WindowEnd, transactions);
        await service.CorrectActualNetAsync(first.Attribution!.Id, OwnerA, 1234m, "manuell nachgezogen");

        var replay = await service.AttributeAsync(
            opportunity.Id, OwnerA, TradeSide.Sell, 10, 450m, 650m, WindowStart, WindowEnd, transactions);

        Assert.Equal(1234m, replay.Attribution!.ActualNet!.Value);            // Nutzerwert bleibt
        Assert.Equal(AttributionSource.User, replay.Attribution!.Source);
        Assert.Equal(450m, replay.Attribution!.ExpectedNetMin);                // Erwartung wird aktualisiert
        Assert.Equal(650m, replay.Attribution!.ExpectedNetMax);
        Assert.Single(await service.GetCorrectionHistoryAsync(first.Attribution!.Id, OwnerA));
    }
}
