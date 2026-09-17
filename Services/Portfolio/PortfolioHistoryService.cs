using Microsoft.EntityFrameworkCore;
using WALLEve.Data;
using WALLEve.Models.Database;
using WALLEve.Models.Holdings;
using WALLEve.Models.Portfolio;
using WALLEve.Services.Portfolio.Interfaces;

namespace WALLEve.Services.Portfolio;

/// <summary>
/// Auswertung vollständiger Holdings-Snapshots zu Historien-Punkten (Issue #38).
///
/// Der Punkt friert zum Sync-Zeitpunkt des Quell-Snapshots ein:
/// <list type="bullet">
/// <item>Nur vollständige Sync-Läufe erzeugen einen Punkt — kein Punkt aus
/// Partial-Sync (AC #38-1).</item>
/// <item>Die Bewertung verwendet ausschließlich Markt-Snapshots mit
/// Timestamp ≤ CapturedAt (As-of). Der verwendete Hub wird als Provenienz
/// gespeichert; ein späterer Marktwechsel überschreibt bestehende Punkte nie
/// (AC #38-1).</item>
/// <item>Items in Sell-Orders (LocationFlag "CorpSellOrder") landen im
/// Escrow-Bucket, nie zusätzlich im freien Bestand — jedes Item genau einmal,
/// keine Doppelzählung (AC #38-2).</item>
/// <item>Realisierte Ergebnisse entstehen nur aus dem chronologischen Replay
/// belegter Ledger-Ereignisse bis CapturedAt; ohne belegte Ereignisse bleibt
/// der Wert null/unknown (AC #38-3). Cashflow und Bewertung stehen getrennt.</item>
/// </list>
///
/// Deterministisch und ohne Live-ESI: alle Lookups laufen gebündelt über die
/// lokale Datenbank (kein N+1 pro Item).
/// </summary>
public class PortfolioHistoryService : IPortfolioHistoryService
{
    private const string StatusCompleted = "completed";
    private const string EscrowLocationFlag = "CorpSellOrder";
    private const string UnknownCategory = "Unbekannt";

    private readonly WalletDbContext _db;
    private readonly Func<int, Task<string?>>? _categoryNameResolver;

    /// <summary>
    /// Erstellt den Service. Der optionale Kategorie-Auflöser liefert je TypeId
    /// das Kategorie-Label (z. B. SDE-Gruppenname); ohne Auflöser bleiben alle
    /// Zeilen unter "Unbekannt" — es wird keine erfundene Kategorie vergeben.
    /// </summary>
    public PortfolioHistoryService(WalletDbContext db, Func<int, Task<string?>>? categoryNameResolver = null)
    {
        _db = db;
        _categoryNameResolver = categoryNameResolver;
    }

    public async Task<PortfolioHistoryEvaluation> EvaluateAsync(long holdingSnapshotId, CancellationToken ct = default)
    {
        var source = await _db.HoldingSnapshots
            .Include(s => s.SyncRun)
            .Include(s => s.Items)
            .SingleOrDefaultAsync(s => s.Id == holdingSnapshotId, ct)
            ?? throw new InvalidOperationException(
                $"Kein Holdings-Snapshot mit Id {holdingSnapshotId} vorhanden.");

        // AC #38-1: Kein Punkt aus Partial-Sync. Nur ein vollständig
        // abgeschlossener Sync-Lauf erzeugt/behält einen Historien-Punkt.
        if (!string.Equals(source.SyncRun.Status, StatusCompleted, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Snapshot {holdingSnapshotId} gehört zu keinem vollständigen Sync-Lauf " +
                $"(Status \"{source.SyncRun.Status}\") — kein Punkt aus Partial-Sync.");
        }

        var capturedAt = source.SyncedAt;

        // Idempotenz: dieselbe Quell-Snapshot-ID erzeugt keinen zweiten Punkt
        // und überschreibt nichts. Projektionen werden deterministisch aus den
        // eingefrorenen Eingaben gebildet — bei einem vorhandenen Punkt zählt
        // dessen eingefrorene Bewertungsregion, nie der heutige aktive Hub.
        var existing = await _db.PortfolioHistoryPoints
            .SingleOrDefaultAsync(p => p.HoldingSnapshotId == holdingSnapshotId, ct);

        var hub = await _db.MarketHubProfiles
            .AsNoTracking()
            .Where(p => p.IsActiveHub)
            .OrderBy(p => p.Id)
            .FirstOrDefaultAsync(ct);

        var valuationRegionId = existing?.ValuationRegionId ?? hub?.RegionId;
        var valuationHubName = existing?.ValuationHubName ?? hub?.Name;

        var priceByType = await LoadAsOfPricesAsync(source.Items, valuationRegionId, capturedAt, ct);
        var basisByType = await LoadBasisByTypeAsync(source, ct);
        var realizedProfit = await ComputeRealizedProfitAsync(source, capturedAt, ct);
        var walletFlow = await LoadWalletFlowAsync(source, capturedAt, ct);
        var categoryLabels = await ResolveCategoriesAsync(source.Items, ct);

        var (locations, categories) = BuildProjections(source.Items, priceByType, categoryLabels);

        // Aggregate über die getrennten Buckets (Assets/Escrow/Basis/Unknown).
        double? assetsValue = null, escrowValue = null;
        long assetsQty = 0, escrowQty = 0, unknownValuationQty = 0;
        int unknownValuationItems = 0;
        double? knownBasis = null, unknownBasisMarket = null;
        long unknownBasisQty = 0;
        int costBasisKnownItems = 0, unknownCostBasisItems = 0;

        foreach (var item in source.Items)
        {
            var isEscrow = item.LocationFlag == EscrowLocationFlag;
            var hasBasis = basisByType.ContainsKey(item.TypeId);
            var price = priceByType.TryGetValue(item.TypeId, out var snapshot) ? snapshot.BestSellPrice : null;

            if (!hasBasis)
            {
                unknownCostBasisItems++;
                unknownBasisQty += item.Quantity;
                if (price.HasValue) unknownBasisMarket = (unknownBasisMarket ?? 0) + item.Quantity * price.Value;
            }
            else
            {
                costBasisKnownItems++;
                if (basisByType[item.TypeId] is double unitBasis)
                {
                    knownBasis = (knownBasis ?? 0) + item.Quantity * unitBasis;
                }
            }

            if (price.HasValue)
            {
                if (isEscrow)
                {
                    escrowQty += item.Quantity;
                    escrowValue = (escrowValue ?? 0) + item.Quantity * price.Value;
                }
                else
                {
                    assetsQty += item.Quantity;
                    assetsValue = (assetsValue ?? 0) + item.Quantity * price.Value;
                }
            }
            else
            {
                unknownValuationItems++;
                unknownValuationQty += item.Quantity;
            }
        }

        var valuedTypeCount = priceByType.Values.Count(s => s.BestSellPrice.HasValue);
        var distinctTypeCount = source.Items.Select(i => i.TypeId).Distinct().Count();

        if (existing is not null)
        {
            return new PortfolioHistoryEvaluation { Point = existing, Locations = locations, Categories = categories };
        }

        var point = new PortfolioHistoryPoint
        {
            HoldingSnapshotId = source.Id,
            OwnerType = source.OwnerType,
            OwnerId = source.OwnerId,
            CapturedAt = capturedAt,
            Source = source.Source,
            ValuationRegionId = valuationRegionId,
            ValuationHubName = valuationHubName,
            ValuatedTypeCount = valuedTypeCount,
            UnknownValuationTypeCount = distinctTypeCount - valuedTypeCount,
            AssetsItemCount = source.Items.Count(i => i.LocationFlag != EscrowLocationFlag),
            AssetsQuantity = assetsQty,
            AssetsValue = assetsValue,
            UnknownValuationItemCount = unknownValuationItems,
            UnknownValuationQuantity = unknownValuationQty,
            EscrowItemCount = source.Items.Count(i => i.LocationFlag == EscrowLocationFlag),
            EscrowQuantity = escrowQty,
            EscrowValue = escrowValue,
            CostBasisKnownItemCount = costBasisKnownItems,
            UnknownCostBasisItemCount = unknownCostBasisItems,
            KnownBasisValue = knownBasis,
            UnknownBasisQuantity = unknownBasisQty,
            UnknownBasisMarketValue = unknownBasisMarket,
            WalletTransactionCount = walletFlow.Count,
            WalletCashInflow = walletFlow.Inflow,
            WalletCashOutflow = walletFlow.Outflow,
            RealizedProfit = realizedProfit
        };

        _db.PortfolioHistoryPoints.Add(point);
        await _db.SaveChangesAsync(ct);
        return new PortfolioHistoryEvaluation { Point = point, Locations = locations, Categories = categories };
    }

    /// <summary>
    /// As-of-Preise: neuester Markt-Snapshot der Bewertungsregion mit
    /// Timestamp ≤ CapturedAt je TypeId — ein Lookup, kein N+1. Ohne aktive
    /// Hub-Region oder ohne Quote bleibt der Typ unbewertet (explicit Unknown).
    /// </summary>
    private async Task<Dictionary<int, MarketSnapshot>> LoadAsOfPricesAsync(
        IEnumerable<HoldingItem> items, int? regionId, DateTime capturedAt, CancellationToken ct)
    {
        var typeIds = items.Select(i => i.TypeId).Distinct().ToList();
        if (typeIds.Count == 0 || !regionId.HasValue) return new Dictionary<int, MarketSnapshot>();

        var snapshots = await _db.MarketSnapshots
            .AsNoTracking()
            .Where(s => s.RegionId == regionId.Value && typeIds.Contains(s.TypeId) && s.Timestamp <= capturedAt)
            .GroupBy(s => s.TypeId)
            .Select(g => g.OrderByDescending(s => s.Timestamp).ThenByDescending(s => s.Id).First())
            .ToDictionaryAsync(s => s.TypeId, ct);
        return snapshots;
    }

    /// <summary>
    /// Cost-Basis je (Owner, Typ): TypeId → belegter Einheitenwert (nur
    /// Character; ein Lookup). Types ohne Eintrag haben keine belegte Basis.
    /// </summary>
    private async Task<Dictionary<int, double?>> LoadBasisByTypeAsync(HoldingSnapshot source, CancellationToken ct)
    {
        var typeIds = source.Items.Select(i => i.TypeId).Distinct().ToList();
        if (source.OwnerType != OwnerType.Character || typeIds.Count == 0) return new Dictionary<int, double?>();

        var entries = await _db.CostBasisEntries
            .AsNoTracking()
            .Where(e => e.CharacterId == source.OwnerId && typeIds.Contains(e.TypeId))
            .Select(e => new { e.TypeId, e.Value })
            .ToListAsync(ct);
        return entries.ToDictionary(e => e.TypeId, e => e.Value);
    }

    /// <summary>
    /// Realisierter Gewinn/Verlust (AC #38-3): chronologischer Replay der
    /// belegten Ledger-Ereignisse bis CapturedAt in die Cost-Basis-Zustandsmaschine.
    /// Nur Character-Owner; ohne belegte Ereignisse null (unknown, nie erfunden).
    /// </summary>
    private async Task<double?> ComputeRealizedProfitAsync(HoldingSnapshot source, DateTime capturedAt, CancellationToken ct)
    {
        if (source.OwnerType != OwnerType.Character) return null;

        var events = await _db.CostBasisLedgerEntries
            .AsNoTracking()
            .Where(e => e.CharacterId == source.OwnerId && e.Quantity > 0 && e.Date <= capturedAt)
            .OrderBy(e => e.Date)
            .ThenBy(e => e.SourceTransactionId)
            .ToListAsync(ct);

        if (events.Count == 0) return null;

        var positionsByType = new Dictionary<int, CostBasisPosition>();
        foreach (var e in events)
        {
            if (!positionsByType.TryGetValue(e.TypeId, out var position))
            {
                position = new CostBasisPosition(source.OwnerId, e.TypeId);
                positionsByType.Add(e.TypeId, position);
            }
            if (e.IsBuy) position.ApplyBuy(e.Quantity, e.UnitPrice);
            else position.ApplySell(e.Quantity, e.UnitPrice);
        }

        return positionsByType.Values.Sum(p => p.RealizedProfit);
    }

    /// <summary>
    /// Cashflow aus lokalen Wallet-Transaktionen bis CapturedAt (nur Character;
    /// Corporation-Wallet wird lokal nicht gespiegelt → null). Zufluss = Verkäufe,
    /// Abfluss = Käufe; Cashflow steht getrennt von Bewertung und Realisiertem.
    /// </summary>
    private async Task<(int Count, double? Inflow, double? Outflow)> LoadWalletFlowAsync(
        HoldingSnapshot source, DateTime capturedAt, CancellationToken ct)
    {
        if (source.OwnerType != OwnerType.Character) return (0, null, null);

        var transactions = await _db.WalletTransactionRecords
            .AsNoTracking()
            .Where(t => t.CharacterId == source.OwnerId && t.Date <= capturedAt)
            .ToListAsync(ct);

        double inflow = 0, outflow = 0;
        foreach (var t in transactions)
        {
            var amount = t.Quantity * t.UnitPrice;
            if (t.IsBuy) outflow += amount;
            else inflow += amount;
        }
        return (transactions.Count, inflow, outflow);
    }

    /// <summary>Kategorie-Label je TypeId über den optionalen Auflöser (gecacht, kein N+1).</summary>
    private async Task<Dictionary<int, string>> ResolveCategoriesAsync(IEnumerable<HoldingItem> items, CancellationToken ct)
    {
        var result = new Dictionary<int, string>();
        if (_categoryNameResolver is null) return result;

        foreach (var typeId in items.Select(i => i.TypeId).Distinct())
        {
            ct.ThrowIfCancellationRequested();
            var label = await _categoryNameResolver(typeId);
            result[typeId] = string.IsNullOrWhiteSpace(label) ? UnknownCategory : label;
        }
        return result;
    }

    /// <summary>
    /// Baut die Ort-/Kategorie-Projektionen mengengleich zum Snapshot: jedes Item
    /// erscheint genau einmal — freier Bestand und Escrow sind getrennt (keine
    /// Doppelzählung, AC #38-2).
    /// </summary>
    private (IReadOnlyList<PortfolioLocationValue> Locations, IReadOnlyList<PortfolioCategoryValue> Categories) BuildProjections(
        IEnumerable<HoldingItem> items,
        Dictionary<int, MarketSnapshot> priceByType,
        Dictionary<int, string> categoryLabels)
    {
        var locationRows = new Dictionary<(long LocationId, string Flag), PortfolioLocationValue>();
        var categoryRows = new Dictionary<string, PortfolioCategoryValue>();

        foreach (var item in items)
        {
            var isEscrow = item.LocationFlag == EscrowLocationFlag;
            var price = priceByType.TryGetValue(item.TypeId, out var snapshot) ? snapshot.BestSellPrice : null;
            var value = price.HasValue ? item.Quantity * price.Value : (double?)null;

            var key = (item.LocationId, item.LocationFlag);
            if (!locationRows.TryGetValue(key, out var location))
            {
                location = new PortfolioLocationValue { LocationId = item.LocationId, LocationFlag = item.LocationFlag };
                locationRows[key] = location;
            }
            if (isEscrow)
            {
                location.EscrowQuantity += item.Quantity;
                location.EscrowItemCount += 1;
                if (value.HasValue) location.EscrowValue = (location.EscrowValue ?? 0) + value.Value;
            }
            else
            {
                location.Quantity += item.Quantity;
                location.ItemCount += 1;
                if (value.HasValue) location.Value = (location.Value ?? 0) + value.Value;
            }

            var categoryLabel = categoryLabels.TryGetValue(item.TypeId, out var label) ? label : UnknownCategory;
            if (!categoryRows.TryGetValue(categoryLabel, out var category))
            {
                category = new PortfolioCategoryValue { Category = categoryLabel };
                categoryRows[categoryLabel] = category;
            }
            if (isEscrow)
            {
                category.EscrowQuantity += item.Quantity;
                category.EscrowItemCount += 1;
                if (value.HasValue) category.EscrowValue = (category.EscrowValue ?? 0) + value.Value;
            }
            else
            {
                category.Quantity += item.Quantity;
                category.ItemCount += 1;
                if (value.HasValue) category.Value = (category.Value ?? 0) + value.Value;
            }
        }

        return (
            locationRows.Values.OrderByDescending(r => r.Quantity + r.EscrowQuantity).ToList(),
            categoryRows.Values.OrderByDescending(r => r.Quantity + r.EscrowQuantity).ToList());
    }
}