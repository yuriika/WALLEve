using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using WALLEve.Data;
using WALLEve.Models.Database;
using WALLEve.Models.Market;
using WALLEve.Models.Trading;
using WALLEve.Services.Market;
using WALLEve.Services.Market.Interfaces;
using WALLEve.Services.Trading.Interfaces;

namespace WALLEve.Services.Trading;

/// <summary>
/// Evidenzbasierte Zuordnung von Wallet-Ergebnissen zu Empfehlungen (Issue #60).
///
/// - Verglichen werden Owner, Typ, Seite, Menge und Zeit; die Zuordnung wird über
///   explizite Links dokumentiert, nie nur behauptet.
/// - Mehrdeutige Lagen bleiben offen: keine zugerechnete Menge, kein tatsächliches
///   Netto (<see cref="AttributionMatchState.Ambiguous"/>).
/// - Ein partieller Match rechnet nur die tatsächlich belegte Menge zu.
/// - Bereits beanspruchte Mengen (Links anderer/älterer Zuordnungen desselben Owners)
///   stehen nicht erneut zur Verfügung — eine Transaktion wird nie zwei Empfehlungen
///   voll zugerechnet. Wiederholte identische Läufe sind idempotent.
/// - Erwartete Spanne und tatsächliches Netto werden getrennt gespeichert; eine
///   manuelle Korrektur überschreibt nur das Netto (mit Historie) und die Erwartung nie.
/// - Ohne belegbare Gebührenherkunft (Issue #46) bleibt das Netto unbekannt (<c>null</c>)
///   statt künstlich exakt zu sein. Kein FIFO-Lot-Verbrauch (Nicht-Ziel des Issues).
/// </summary>
public sealed class RecommendationAttributionService : IRecommendationAttributionService
{
    private const string MissingNote =
        "Kein Kandidat passt zu Owner/Typ/Seite/Menge/Zeit (oder alle Kandidaten sind bereits beansprucht).";

    private const string AmbiguousFullNote =
        "Mehrere Transaktionen passen vollständig — die Zuordnung bleibt offen, es wird keine Menge zugerechnet.";

    private const string AmbiguousSplitNote =
        "Kein Einzelbeleg deckt die erwartete Menge; eine Aufteilung wäre nicht belegbar — Zuordnung bleibt offen.";

    private const string UnknownFeesNote =
        "Gebührenherkunft ohne belegte Sätze (geschätzt oder unbekannt) — das tatsächliche Netto bleibt unbekannt.";

    private readonly WalletDbContext _db;
    private readonly IFeeCalculatorService _fees;

    public RecommendationAttributionService(WalletDbContext db, IFeeCalculatorService fees)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _fees = fees ?? throw new ArgumentNullException(nameof(fees));
    }

    /// <inheritdoc />
    public async Task<AttributionResult> AttributeAsync(
        int opportunityId,
        int characterId,
        string side,
        int expectedQuantity,
        decimal expectedNetMin,
        decimal expectedNetMax,
        DateTime windowStartUtc,
        DateTime windowEndUtc,
        IReadOnlyCollection<WalletTransactionRecord>? transactions,
        CancellationToken cancellationToken = default)
    {
        // Unzulässige Aufträge ändern nichts an der Persistenz.
        if (!TradeSide.All.Contains(side))
            return AttributionResult.Failed("Ungültige Handelsseite: erwartet \"buy\" oder \"sell\".");
        if (expectedQuantity <= 0)
            return AttributionResult.Failed("Die erwartete Menge muss größer als 0 sein.");
        if (expectedNetMax < expectedNetMin)
            return AttributionResult.Failed("Die erwartete Spanne ist ungültig (obere Grenze unter unterer Grenze).");
        if (windowEndUtc <= windowStartUtc)
            return AttributionResult.Failed("Das Zeitfenster ist ungültig (Ende nicht nach dem Start).");
        if (transactions is null)
            return AttributionResult.Failed("Es wurden keine Transaktionen übergeben.");

        var opportunity = await _db.TradingOpportunities
            .SingleOrDefaultAsync(o => o.Id == opportunityId, cancellationToken);
        if (opportunity is null)
            return AttributionResult.Failed("Empfehlung nicht gefunden.");
        if (opportunity.CharacterId != characterId)
            return AttributionResult.Failed("Fremde Empfehlung: Zuordnung nur durch den Owner.");

        var isBuy = side == TradeSide.Buy;

        // Kandidaten: Owner + Typ + Seite + Zeit.
        var candidates = transactions
            .Where(t => t.CharacterId == characterId
                        && t.TypeId == opportunity.TypeId
                        && t.IsBuy == isBuy
                        && t.Date >= windowStartUtc
                        && t.Date <= windowEndUtc)
            .ToList();

        // Eigene Zuordnung des Laufs: ihre Links werden neu geschrieben und dürfen die
        // eigene Replay-Auswertung nicht als "verbraucht" blockieren (Idempotenz).
        var ownAttributionId = await _db.RecommendationAttributions
            .Where(a => a.TradingOpportunityId == opportunity.Id && a.CharacterId == characterId)
            .Select(a => (int?)a.Id)
            .SingleOrDefaultAsync(cancellationToken);

        var consumed = await LoadConsumedQuantitiesAsync(
            characterId, candidates.Select(t => t.TransactionId), ownAttributionId, cancellationToken);

        var free = candidates
            .Select(t => new FreeCandidate(t, t.Quantity - consumed.GetValueOrDefault(t.TransactionId)))
            .Where(c => c.Remaining > 0)
            .ToList();

        var allocation = new List<(WalletTransactionRecord Transaction, int Quantity)>();
        var full = free.Where(c => c.Remaining >= expectedQuantity).ToList();

        string state;
        string? note;
        int attributedQuantity;

        if (free.Count == 0)
        {
            state = AttributionMatchState.Missing;
            note = MissingNote;
            attributedQuantity = 0;
        }
        else if (full.Count == 1)
        {
            state = AttributionMatchState.Unique;
            note = null;
            attributedQuantity = expectedQuantity;
            allocation.Add((full[0].Transaction, expectedQuantity));
        }
        else if (full.Count > 1)
        {
            state = AttributionMatchState.Ambiguous;
            note = AmbiguousFullNote;
            attributedQuantity = 0;
        }
        else
        {
            var freeTotal = free.Sum(c => c.Remaining);
            if (freeTotal < expectedQuantity)
            {
                state = AttributionMatchState.Partial;
                attributedQuantity = freeTotal;
                note = $"Partieller Match: nur {freeTotal} von {expectedQuantity} Einheiten sind belegt.";
                allocation.AddRange(free.Select(c => (c.Transaction, c.Remaining)));
            }
            else
            {
                state = AttributionMatchState.Ambiguous;
                note = AmbiguousSplitNote;
                attributedQuantity = 0;
            }
        }

        return await PersistAsync(
            opportunity, characterId, side, expectedQuantity, expectedNetMin, expectedNetMax,
            windowStartUtc, windowEndUtc, state, note, attributedQuantity, allocation, cancellationToken);
    }

    /// <summary>
    /// Persistiert die Zuordnung idempotent: eine Zeile je Empfehlung und Owner,
    /// Links nur bei tatsächlicher Änderung.
    /// </summary>
    private async Task<AttributionResult> PersistAsync(
        TradingOpportunity opportunity,
        int characterId,
        string side,
        int expectedQuantity,
        decimal expectedNetMin,
        decimal expectedNetMax,
        DateTime windowStartUtc,
        DateTime windowEndUtc,
        string state,
        string? note,
        int attributedQuantity,
        List<(WalletTransactionRecord Transaction, int Quantity)> allocation,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        var attribution = await _db.RecommendationAttributions
            .SingleOrDefaultAsync(a => a.TradingOpportunityId == opportunity.Id && a.CharacterId == characterId,
                cancellationToken);

        if (attribution is null)
        {
            attribution = new RecommendationAttribution
            {
                TradingOpportunityId = opportunity.Id,
                CharacterId = characterId,
                CreatedAt = now
            };
            _db.RecommendationAttributions.Add(attribution);
        }

        attribution.TypeId = opportunity.TypeId;
        attribution.Side = side;
        attribution.MatchState = state;
        attribution.ExpectedQuantity = expectedQuantity;
        attribution.AttributedQuantity = attributedQuantity;
        attribution.ExpectedNetMin = expectedNetMin;
        attribution.ExpectedNetMax = expectedNetMax;
        attribution.WindowStartUtc = windowStartUtc;
        attribution.WindowEndUtc = windowEndUtc;
        attribution.Note = note;
        attribution.UpdatedAt = now;

        // Tatsächliches Netto: eine manuelle Korrektur des Nutzers bleibt erhalten
        // und wird von einem erneuten Analyse-Lauf nie überschrieben.
        if (attribution.Source != AttributionSource.User)
        {
            var feesKnown = HasEvidencedFees(opportunity, side);
            decimal? actualNet = null;

            if (attributedQuantity > 0 && allocation.Count > 0)
            {
                if (feesKnown)
                    actualNet = CalculateActualNet(opportunity, side, allocation);
                else
                    attribution.Note = ComposeNote(note, UnknownFeesNote);
            }

            attribution.ActualNet = actualNet;
            attribution.FeeKnowledge = actualNet.HasValue ? AttributionFeeKnowledge.Known : AttributionFeeKnowledge.Unknown;
        }

        await _db.SaveChangesAsync(cancellationToken);

        await SyncLinksAsync(attribution, allocation, side, cancellationToken);
        return AttributionResult.Ok(attribution);
    }

    /// <summary>
    /// Legt die expliziten Transaktions-Links neu an, wenn sich die Zuordnung
    /// geändert hat; identische Läufe schreiben nichts (Idempotenz).
    /// </summary>
    private async Task SyncLinksAsync(
        RecommendationAttribution attribution,
        List<(WalletTransactionRecord Transaction, int Quantity)> allocation,
        string side,
        CancellationToken cancellationToken)
    {
        var existing = await _db.AttributionTransactionLinks
            .Where(l => l.AttributionId == attribution.Id)
            .ToListAsync(cancellationToken);

        var desired = allocation
            .GroupBy(a => a.Transaction.TransactionId)
            .Select(g => new LinkSpec(
                g.Key,
                g.Sum(a => a.Quantity),
                g.First().Transaction.TypeId,
                g.First().Transaction.Date,
                g.First().Transaction.UnitPrice))
            .OrderBy(s => s.TransactionId)
            .ToList();

        var current = existing
            .GroupBy(l => l.TransactionId)
            .Select(g => new LinkSpec(g.Key, g.Sum(l => l.Quantity), g.First().TypeId, g.First().TransactionDate, g.First().UnitPrice))
            .OrderBy(s => s.TransactionId)
            .ToList();

        var unchanged = desired.Count == current.Count
            && desired.Zip(current).All(pair =>
                pair.First.TransactionId == pair.Second.TransactionId
                && pair.First.Quantity == pair.Second.Quantity);

        if (unchanged)
            return;

        if (existing.Count > 0)
            _db.AttributionTransactionLinks.RemoveRange(existing);

        foreach (var spec in desired)
        {
            _db.AttributionTransactionLinks.Add(new AttributionTransactionLink
            {
                AttributionId = attribution.Id,
                TradingOpportunityId = attribution.TradingOpportunityId,
                CharacterId = attribution.CharacterId,
                TransactionId = spec.TransactionId,
                TypeId = spec.TypeId,
                TransactionDate = spec.TransactionDate,
                Side = side,
                Quantity = spec.Quantity,
                UnitPrice = spec.UnitPrice,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private sealed record LinkSpec(long TransactionId, int Quantity, int TypeId, DateTime TransactionDate, double UnitPrice);

    private sealed record FreeCandidate(WalletTransactionRecord Transaction, int Remaining);

    private static string ComposeNote(string? note, string addition)
        => string.IsNullOrWhiteSpace(note) ? addition : $"{note} {addition}";

    private async Task<Dictionary<long, int>> LoadConsumedQuantitiesAsync(
        int characterId,
        IEnumerable<long> transactionIds,
        int? excludeAttributionId,
        CancellationToken cancellationToken)
    {
        var ids = transactionIds.Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<long, int>();

        var query = _db.AttributionTransactionLinks
            .Where(l => l.CharacterId == characterId && ids.Contains(l.TransactionId));

        if (excludeAttributionId.HasValue)
            query = query.Where(l => l.AttributionId != excludeAttributionId.Value);

        var rows = await query
            .GroupBy(l => l.TransactionId)
            .Select(g => new { TransactionId = g.Key, Quantity = g.Sum(l => l.Quantity) })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(x => x.TransactionId, x => x.Quantity);
    }

    /// <summary>
    /// Gebühren gelten nur als belegt, wenn die Herkunft aus Issue #46 tatsächliche
    /// Sätze ausweist: nur Automatic oder ManualOverride. Eine konservative Schätzung
    /// (Estimated) oder fehlende Herkunft (Unknown) ist kein Beleg — das tatsächliche
    /// Netto bleibt dann unbekannt (keine künstlich exakte Zahl). Der Standing-Anteil
    /// ist Bestandteil des Broker-Satzes und muss deshalb auf beiden Seiten belegt
    /// sein, der Sales-Tax-Anteil nur beim Verkauf.
    /// </summary>
    private static bool HasEvidencedFees(TradingOpportunity opportunity, string side)
    {
        var brokerKnown = opportunity.BrokerFeeRate.HasValue
                          && IsEvidencedOrigin(opportunity.BrokerFeeOrigin)
                          && IsEvidencedOrigin(opportunity.StandingsOrigin);
        if (side == TradeSide.Buy)
            return brokerKnown;

        return brokerKnown
               && opportunity.SalesTaxRate.HasValue
               && IsEvidencedOrigin(opportunity.SalesTaxOrigin);
    }

    private static bool IsEvidencedOrigin(string? origin)
        => ParseOrigin(origin) is FeeInputOrigin.Automatic or FeeInputOrigin.ManualOverride;

    private static FeeInputOrigin ParseOrigin(string? origin)
    {
        if (string.Equals(origin, FeeInputOrigin.Automatic.StorageValue(), StringComparison.Ordinal))
            return FeeInputOrigin.Automatic;
        if (string.Equals(origin, FeeInputOrigin.ManualOverride.StorageValue(), StringComparison.Ordinal))
            return FeeInputOrigin.ManualOverride;
        if (string.Equals(origin, FeeInputOrigin.Estimated.StorageValue(), StringComparison.Ordinal))
            return FeeInputOrigin.Estimated;
        return FeeInputOrigin.Unknown;
    }

    /// <summary>
    /// Tatsächliches Netto der belegten Menge. Verkauf: Erlös nach Gebühren über die
    /// offizielle Formel aus Issue #46 (FeeCalculatorService). Kauf: Aufwand inkl.
    /// Broker-Fee (gleiche Formel wie CalculateBuyCost) — negativ, weil es ein Abfluss ist.
    /// </summary>
    private decimal CalculateActualNet(
        TradingOpportunity opportunity,
        string side,
        List<(WalletTransactionRecord Transaction, int Quantity)> allocation)
    {
        var brokerRate = opportunity.BrokerFeeRate ?? 0.0;
        var profile = new FeeProfile
        {
            BrokerFeeRate = brokerRate,
            SalesTaxRate = opportunity.SalesTaxRate ?? 0.0,
            BrokerRateOrigin = ParseOrigin(opportunity.BrokerFeeOrigin),
            SalesTaxOrigin = ParseOrigin(opportunity.SalesTaxOrigin),
            StandingsOrigin = ParseOrigin(opportunity.StandingsOrigin),
            EvaluatedAtUtc = opportunity.FeeEvaluatedAtUtc ?? DateTime.UtcNow
        };

        decimal total = 0m;
        foreach (var item in allocation)
        {
            if (side == TradeSide.Sell)
            {
                var proceeds = _fees.CalculateSellProceedsWithProfile(
                    item.Transaction.UnitPrice, item.Quantity, profile);
                total += (decimal)proceeds.NetAmount;
            }
            else
            {
                var gross = (decimal)item.Transaction.UnitPrice * item.Quantity;
                total -= gross + (gross * (decimal)brokerRate);
            }
        }

        return Math.Round(total, 2, MidpointRounding.AwayFromZero);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RecommendationAttribution>> GetAttributionsAsync(
        int opportunityId,
        int characterId,
        CancellationToken cancellationToken = default)
    {
        return await _db.RecommendationAttributions
            .AsNoTracking()
            .Where(a => a.TradingOpportunityId == opportunityId && a.CharacterId == characterId)
            .OrderBy(a => a.Id)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AttributionTransactionLink>> GetLinksAsync(
        int attributionId,
        int characterId,
        CancellationToken cancellationToken = default)
    {
        return await _db.AttributionTransactionLinks
            .AsNoTracking()
            .Where(l => l.AttributionId == attributionId && l.CharacterId == characterId)
            .OrderBy(l => l.TransactionId)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CorrectionResult> CorrectActualNetAsync(
        int attributionId,
        int characterId,
        decimal? newActualNet,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return CorrectionResult.Failed("Eine Begründung für die Korrektur ist erforderlich.");

        var attribution = await _db.RecommendationAttributions
            .SingleOrDefaultAsync(a => a.Id == attributionId, cancellationToken);
        if (attribution is null)
            return CorrectionResult.Failed("Zuordnung nicht gefunden.");
        if (attribution.CharacterId != characterId)
            return CorrectionResult.Failed("Fremde Zuordnung: Korrektur nur durch den Owner.");

        // Idempotent: unveränderter Wert erzeugt keinen Historieneintrag.
        if (attribution.ActualNet == newActualNet)
            return CorrectionResult.AlreadySet(attribution);

        var correction = new ActualNetCorrection
        {
            AttributionId = attribution.Id,
            TradingOpportunityId = attribution.TradingOpportunityId,
            CharacterId = attribution.CharacterId,
            PreviousActualNet = attribution.ActualNet,
            NewActualNet = newActualNet,
            Reason = reason,
            Source = AttributionSource.User,
            CorrectedAt = DateTime.UtcNow
        };

        attribution.ActualNet = newActualNet;
        attribution.Source = AttributionSource.User;
        attribution.FeeKnowledge = newActualNet.HasValue ? AttributionFeeKnowledge.Known : AttributionFeeKnowledge.Unknown;
        attribution.UpdatedAt = correction.CorrectedAt;

        _db.ActualNetCorrections.Add(correction);
        await _db.SaveChangesAsync(cancellationToken);

        return CorrectionResult.Ok(attribution, correction);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ActualNetCorrection>> GetCorrectionHistoryAsync(
        int attributionId,
        int characterId,
        CancellationToken cancellationToken = default)
    {
        return await _db.ActualNetCorrections
            .AsNoTracking()
            .Where(c => c.AttributionId == attributionId && c.CharacterId == characterId)
            .OrderBy(c => c.CorrectedAt)
            .ThenBy(c => c.Id)
            .ToListAsync(cancellationToken);
    }
}