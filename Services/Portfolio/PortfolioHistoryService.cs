using Microsoft.EntityFrameworkCore;
using WALLEve.Data;
using WALLEve.Models.Database;
using WALLEve.Models.Holdings;
using WALLEve.Models.Portfolio;
using WALLEve.Services.Market;
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
/// <item>Ort- und Kategorie-Projektionen werden beim ersten Auswerten gemeinsam
/// mit dem Punkt persistiert und bei Wiederholung unverändert zurückgegeben —
/// spätere As-of-Quote oder SDE-Umbenennungen ändern eingefrorene Projektionen
/// nie (Review #157, Blocker 1).</item>
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
    private readonly Func<IReadOnlyCollection<int>, Task<Dictionary<int, string?>>>? _categoryNameResolver;

    /// <summary>
    /// Erstellt den Service. Der optionale Kategorie-Auflöser liefert die
    /// Kategorie-Label aller TypeIds in EINEM gebündelten Aufruf (kein N+1);
    /// ohne Auflöser bleiben alle Zeilen unter "Unbekannt" — es wird keine
    /// erfundene Kategorie vergeben.
    /// </summary>
    public PortfolioHistoryService(WalletDbContext db, Func<IReadOnlyCollection<int>, Task<Dictionary<int, string?>>>? categoryNameResolver = null)
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
        // und überschreibt nichts. Bei einem vorhandenen Punkt zählen dessen
        // eingefrorene Bewertungsregion und dessen persistierte Projektionen —
        // nie der heutige aktive Hub, nie neu gebaute Orte/Kategorien
        // (Review #157, Blocker 1: Projektionen eingefrorener Punkte dürfen
        // durch spätere As-of-Quote oder SDE-Änderungen nicht mehr mutieren).
        var existing = await _db.PortfolioHistoryPoints
            .SingleOrDefaultAsync(p => p.HoldingSnapshotId == holdingSnapshotId, ct);

        if (existing is not null)
        {
            var frozenLocations = await _db.PortfolioHistoryLocations
                .AsNoTracking()
                .Where(l => l.PointId == existing.Id)
                .ToListAsync(ct);
            var frozenCategories = await _db.PortfolioHistoryCategories
                .AsNoTracking()
                .Where(c => c.PointId == existing.Id)
                .ToListAsync(ct);

            return new PortfolioHistoryEvaluation
            {
                Point = existing,
                Locations = MapLocations(frozenLocations),
                Categories = MapCategories(frozenCategories)
            };
        }

        // Ein expliziter Hub bleibt für bestehende Profile maßgeblich. Gibt es
        // keinen, verwendet Portfolio denselben Referenzmarkt wie die
        // Einkaufspreis-Schätzung — kein zweiter unsichtbarer Schalter.
        var hub = await _db.MarketHubProfiles
            .AsNoTracking()
            .Where(profile => profile.IsActiveHub)
            .OrderBy(profile => profile.Id)
            .FirstOrDefaultAsync(ct);
        var regionSetting = await _db.AppSettings
            .AsNoTracking()
            .Where(setting => setting.Key == CostBasisService.DefaultRegionSettingKey)
            .Select(setting => setting.Value)
            .FirstOrDefaultAsync(ct);
        var fallbackRegionId = int.TryParse(regionSetting, out var configuredRegionId)
            ? configuredRegionId
            : CostBasisService.DefaultRegionId;
        var valuationRegionId = hub?.RegionId ?? fallbackRegionId;
        var valuationHubName = hub?.Name ?? $"Referenzmarkt (Region {fallbackRegionId})";

        var priceByType = await LoadAsOfPricesAsync(source.Items, valuationRegionId, capturedAt, ct);
        var basisByType = await LoadBasisByTypeAsync(source, ct);
        var realizedProfit = await ComputeRealizedProfitAsync(source, capturedAt, ct);
        var walletFlow = await LoadWalletFlowAsync(source, capturedAt, ct);
        var categoryLabels = await ResolveCategoriesAsync(source.Items, ct);

        var (locationRow, categoryRows) = BuildProjections(source.Items, priceByType, categoryLabels);

        // Aggregate über die getrennten Buckets (Assets/Escrow/Basis/Unknown).
        // Mengen werden unabhängig vom Quote-Bestand gezählt; nur die Wertfelder
        // bleiben ohne As-of-Quote unknown (Review #157, Blocker 2).
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

            if (isEscrow)
            {
                escrowQty += item.Quantity;
                if (price.HasValue) escrowValue = (escrowValue ?? 0) + item.Quantity * price.Value;
                else
                {
                    unknownValuationItems++;
                    unknownValuationQty += item.Quantity;
                }
            }
            else
            {
                assetsQty += item.Quantity;
                if (price.HasValue) assetsValue = (assetsValue ?? 0) + item.Quantity * price.Value;
                else
                {
                    unknownValuationItems++;
                    unknownValuationQty += item.Quantity;
                }
            }
        }

        var valuedTypeCount = priceByType.Values.Count(s => s.BestSellPrice.HasValue);
        var distinctTypeCount = source.Items.Select(i => i.TypeId).Distinct().Count();

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

        // Projektionen gemeinsam mit dem Punkt einfrieren — bei Wiederholung
        // werden exakt diese persistierten Zeilen zurückgegeben (Review #157).
        foreach (var row in locationRow) row.Point = point;
        foreach (var row in categoryRows) row.Point = point;
        _db.PortfolioHistoryLocations.AddRange(locationRow);
        _db.PortfolioHistoryCategories.AddRange(categoryRows);

        await _db.SaveChangesAsync(ct);
        return new PortfolioHistoryEvaluation
        {
            Point = point,
            Locations = MapLocations(locationRow),
            Categories = MapCategories(categoryRows)
        };
    }

    public async Task<IReadOnlyList<PortfolioHistoryPoint>> GetHistoryAsync(OwnerType ownerType, int ownerId, CancellationToken ct = default)
    {
        // Nur persistierte, eingefrorene Punkte in zeitlicher Ordnung (AC #47-1):
        // ein späterer Marktwechsel ändert die Provenienz bestehender Punkte nie,
        // fehlende Sync-Zeiträume bleiben als Lücken in der Liste sichtbar.
        return await _db.PortfolioHistoryPoints
            .AsNoTracking()
            .Where(p => p.OwnerType == ownerType && p.OwnerId == ownerId)
            .OrderBy(p => p.CapturedAt)
            .ThenBy(p => p.Id)
            .ToListAsync(ct);
    }

    public async Task<PortfolioHistoryEvaluation?> GetPointAsync(long pointId, CancellationToken ct = default)
    {
        var point = await _db.PortfolioHistoryPoints
            .AsNoTracking()
            .Include(p => p.SourceSnapshot)
            .SingleOrDefaultAsync(p => p.Id == pointId, ct);
        if (point is null) return null;

        var locations = await _db.PortfolioHistoryLocations
            .AsNoTracking()
            .Where(l => l.PointId == point.Id)
            .ToListAsync(ct);
        var categories = await _db.PortfolioHistoryCategories
            .AsNoTracking()
            .Where(c => c.PointId == point.Id)
            .ToListAsync(ct);

        return new PortfolioHistoryEvaluation
        {
            Point = point,
            Locations = MapLocations(locations),
            Categories = MapCategories(categories)
        };
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

    /// <summary>Kategorie-Label je TypeId über den optionalen Auflöser — EIN
    /// gebündelter Aufruf für alle TypeIds, kein N+1 (Review #157).</summary>
    private async Task<Dictionary<int, string>> ResolveCategoriesAsync(IEnumerable<HoldingItem> items, CancellationToken ct)
    {
        var result = new Dictionary<int, string>();
        if (_categoryNameResolver is null) return result;

        var typeIds = items.Select(i => i.TypeId).Distinct().ToList();
        if (typeIds.Count == 0) return result;

        ct.ThrowIfCancellationRequested();
        var labels = await _categoryNameResolver(typeIds);

        foreach (var typeId in typeIds)
        {
            var label = labels.TryGetValue(typeId, out var l) ? l : null;
            result[typeId] = string.IsNullOrWhiteSpace(label) ? UnknownCategory : label;
        }
        return result;
    }

    /// <summary>
    /// Baut die Ort-/Kategorie-Projektionen mengengleich zum Snapshot als zu
    /// persistierende Zeilen: jedes Item erscheint genau einmal — freier Bestand
    /// und Escrow sind getrennt (keine Doppelzählung, AC #38-2). Mengen werden
    /// unabhängig vom Quote-Bestand gezählt; nur Werte bleiben ohne Quote null.
    /// </summary>
    private (List<PortfolioHistoryLocation> Locations, List<PortfolioHistoryCategory> Categories) BuildProjections(
        IEnumerable<HoldingItem> items,
        Dictionary<int, MarketSnapshot> priceByType,
        Dictionary<int, string> categoryLabels)
    {
        var locationRows = new Dictionary<(long LocationId, string Flag), PortfolioHistoryLocation>();
        var categoryRows = new Dictionary<string, PortfolioHistoryCategory>();

        foreach (var item in items)
        {
            var isEscrow = item.LocationFlag == EscrowLocationFlag;
            var price = priceByType.TryGetValue(item.TypeId, out var snapshot) ? snapshot.BestSellPrice : null;
            var value = price.HasValue ? item.Quantity * price.Value : (double?)null;

            var key = (item.LocationId, item.LocationFlag);
            if (!locationRows.TryGetValue(key, out var location))
            {
                location = new PortfolioHistoryLocation { LocationId = item.LocationId, LocationFlag = item.LocationFlag };
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
                category = new PortfolioHistoryCategory { Category = categoryLabel };
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

    /// <summary>Persistierte Zeilen → Orts-Projektion (Sortierung wie beim ersten Auswerten).</summary>
    private static IReadOnlyList<PortfolioLocationValue> MapLocations(IEnumerable<PortfolioHistoryLocation> rows)
        => rows
            .Select(l => new PortfolioLocationValue
            {
                LocationId = l.LocationId,
                LocationFlag = l.LocationFlag,
                ItemCount = l.ItemCount,
                Quantity = l.Quantity,
                Value = l.Value,
                EscrowItemCount = l.EscrowItemCount,
                EscrowQuantity = l.EscrowQuantity,
                EscrowValue = l.EscrowValue
            })
            .OrderByDescending(l => l.Quantity + l.EscrowQuantity)
            .ToList();

    /// <summary>Persistierte Zeilen → Kategorie-Projektion (Sortierung wie beim ersten Auswerten).</summary>
    private static IReadOnlyList<PortfolioCategoryValue> MapCategories(IEnumerable<PortfolioHistoryCategory> rows)
        => rows
            .Select(c => new PortfolioCategoryValue
            {
                Category = c.Category,
                ItemCount = c.ItemCount,
                Quantity = c.Quantity,
                Value = c.Value,
                EscrowItemCount = c.EscrowItemCount,
                EscrowQuantity = c.EscrowQuantity,
                EscrowValue = c.EscrowValue
            })
            .OrderByDescending(c => c.Quantity + c.EscrowQuantity)
            .ToList();
}