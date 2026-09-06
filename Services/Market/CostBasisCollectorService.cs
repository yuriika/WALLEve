using Microsoft.EntityFrameworkCore;
using WALLEve.Data;
using WALLEve.Models.Database;
using WALLEve.Services.Authentication.Interfaces;
using WALLEve.Services.Esi.Interfaces;
using WALLEve.Services.Market.Interfaces;

namespace WALLEve.Services.Market;

/// <summary>
/// Hintergrund-Task für die Cost-Basis-Ermittlung.
///
/// Phase A (Sink):   Spiegelt ESI-Wallet-Transaktionen täglich in die lokale
///                   Tabelle WalletTransactionRecords (dedup per TransactionId).
///                   Damit wird das ~30-Tage-Fenster von ESI langfristig egal.
///
/// Phase B (Ableitung): Geht Items ohne CostBasisEntry durch (Reihenfolge:
///                   Marktwert absteigend) und setzt echte Einkaufspreise aus
///                   lokalen Kauf-Transaktionen (Source=Transaction).
///
/// Beide Phasen laufen als persistierte BackgroundJobs: Status und Fortschritt
/// stehen in der DB; nach einem Neustart werden interrupted Jobs automatisch
/// fortgesetzt (idempotent). Schätzungen (Source=Estimate) werden NICHT hier
/// angestoßen — die stößt der Nutzer über die Übersichtsseite an.
/// </summary>
public class CostBasisCollectorService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CostBasisCollectorService> _logger;

    private const string SinkJobType = "CostBasisSink";
    private const string DeductionJobType = "CostBasisDeduction";
    private const string EstimateJobType = "CostBasisEstimate";

    /// <summary>Mindestabstand zwischen zwei Sink-Läufen.</summary>
    private static readonly TimeSpan SinkInterval = TimeSpan.FromHours(24);

    public CostBasisCollectorService(
        IServiceScopeFactory scopeFactory,
        ILogger<CostBasisCollectorService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Cost Basis Collector Service starting...");

        try
        {
            // App-Start abwarten, damit TokenStorage den gespeicherten Login geladen hat
            await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
                var authService = scope.ServiceProvider.GetRequiredService<IEveAuthenticationService>();
                var jobManager = scope.ServiceProvider.GetRequiredService<IBackgroundJobManager>();

                // Alle unterbrochenen Jobs (App-Neustart) als Interrupted markieren,
                // damit die Übersicht den Zustand korrekt zeigt.
                await MarkRunningAsInterruptedAsync(db, stoppingToken);

                var authState = await authService.GetAuthStateAsync();
                if (authState?.IsValid == true)
                {
                    await RunSinkIfDueAsync(scope, db, jobManager, authState.CharacterId, stoppingToken);
                    await RunDeductionIfNeededAsync(scope, db, jobManager, authState.CharacterId, stoppingToken);
                    await RunEstimateJobsAsync(scope, db, jobManager, authState.CharacterId, stoppingToken);
                }
                else
                {
                    _logger.LogInformation("Cost basis collector: no authenticated character, waiting...");
                }
            }
            catch (OperationCanceledException)
            {
                // Normaler Shutdown
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Cost Basis Collector Service main loop");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Cost Basis Collector Service stopping...");
    }

    // ------------------------------------------------------------------
    // Phase A: Transaktions-Sink
    // ------------------------------------------------------------------

    private async Task RunSinkIfDueAsync(IServiceScope scope, WalletDbContext db,
        IBackgroundJobManager jobManager, int characterId, CancellationToken ct)
    {
        var lastSink = await db.BackgroundJobs
            .Where(j => j.JobType == SinkJobType && j.CharacterId == characterId
                     && j.Status == BackgroundJobStatus.Completed)
            .OrderByDescending(j => j.CompletedAt)
            .Select(j => j.CompletedAt)
            .FirstOrDefaultAsync(ct);

        var now = DateTime.UtcNow;
        if (lastSink.HasValue && now - lastSink.Value < SinkInterval)
        {
            _logger.LogInformation("Cost basis sink: last run {LastRun}, next run due {NextRun}",
                lastSink.Value, lastSink.Value + SinkInterval);
            return;
        }

        // Es läuft bereits ein Sink? (z.B. Restart im Gange) — nicht doppelt starten.
        var runningSink = await db.BackgroundJobs
            .AnyAsync(j => j.JobType == SinkJobType && j.CharacterId == characterId
                        && (j.Status == BackgroundJobStatus.Running
                         || j.Status == BackgroundJobStatus.Interrupted), ct);
        if (runningSink) return;

        var job = await jobManager.CreateJobAsync(SinkJobType, "Wallet-Transaktionen spiegeln",
            characterId, total: 0);
        _logger.LogInformation("Cost basis sink: started (job {JobId})", job.Id);

        try
        {
            var esi = scope.ServiceProvider.GetRequiredService<IEsiApiService>();
            var transactions = await esi.GetAllWalletTransactionsPagesAsync(characterId);
            if (transactions == null || transactions.Count == 0)
            {
                _logger.LogInformation("Cost basis sink: no transactions from ESI");
            }
            else
            {
                // Vorhandene TransactionIds pro Character ermitteln (Dedup)
                var existing = await db.WalletTransactionRecords
                    .Where(t => t.CharacterId == characterId)
                    .Select(t => t.TransactionId)
                    .ToHashSetAsync(ct);

                var newRecords = new List<WalletTransactionRecord>(transactions.Count);
                foreach (var t in transactions)
                {
                    ct.ThrowIfCancellationRequested();
                    if (existing.Contains(t.TransactionId)) continue;

                    newRecords.Add(new WalletTransactionRecord
                    {
                        CharacterId = characterId,
                        TransactionId = t.TransactionId,
                        TypeId = t.TypeId,
                        Date = t.Date,
                        IsBuy = t.IsBuy,
                        IsPersonal = t.IsPersonal,
                        JournalRefId = t.JournalRefId,
                        LocationId = t.LocationId,
                        Quantity = t.Quantity,
                        UnitPrice = t.UnitPrice
                    });
                }

                // In Blöcken speichern (10k Batches), um In-Memory-Druck klein zu halten
                for (var i = 0; i < newRecords.Count; i += 10000)
                {
                    ct.ThrowIfCancellationRequested();
                    var batch = newRecords.Skip(i).Take(10000).ToList();
                    await db.WalletTransactionRecords.AddRangeAsync(batch, ct);
                    await db.SaveChangesAsync(ct);
                }

                _logger.LogInformation("Cost basis sink: {New} new of {Total} transactions stored",
                    newRecords.Count, transactions.Count);
            }

            // Sink wiederholt sich körniger, falls ESI limitiert war (Seiten fehlen)
            await jobManager.MarkCompletedAsync(job.Id);
            await jobManager.UpdateProgressAsync(job.Id, 1, 1);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await jobManager.MarkFailedAsync(job.Id, "Cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cost basis sink failed");
            await jobManager.MarkFailedAsync(job.Id, ex.Message);
        }
    }

    // ------------------------------------------------------------------
    // Phase B: Ableitung echter Einkaufspreise
    // ------------------------------------------------------------------

    private async Task RunDeductionIfNeededAsync(IServiceScope scope, WalletDbContext db,
        IBackgroundJobManager jobManager, int characterId, CancellationToken ct)
    {
        // Liegt bereits ein aktiver (oder fortzusetzender) Ableitungs-Job vor?
        var activeJob = await db.BackgroundJobs
            .Where(j => j.JobType == DeductionJobType && j.CharacterId == characterId
                     && (j.Status == BackgroundJobStatus.Running
                      || j.Status == BackgroundJobStatus.Interrupted
                      || j.Status == BackgroundJobStatus.Paused))
            .OrderByDescending(j => j.UpdatedAt)
            .FirstOrDefaultAsync(ct);

        if (activeJob != null)
        {
            _logger.LogInformation("Cost basis deduction: job {JobId} is {Status} — resuming",
                activeJob.Id, activeJob.Status);
            if (activeJob.Status != BackgroundJobStatus.Paused)
            {
                await RunDeductionCoreAsync(scope, db, jobManager, activeJob, characterId, ct);
            }
            return;
        }

        // Kein aktiver Job: neuen nur starten, wenn es Items gibt, die überhaupt
        // per Transaktion matchbar sind (Buy-Transaktion in der lokalen Spiegelung)
        // und noch keinen Cost-Basis-Eintrag haben.
        var inventoryService = scope.ServiceProvider.GetRequiredService<IInventoryService>();
        var items = await inventoryService.GetInventoryAsync(characterId);
        var itemByType = items.ToDictionary(i => i.TypeId);

        var matchableTypeIds = await db.WalletTransactionRecords
            .Where(t => t.CharacterId == characterId && t.IsBuy && t.Quantity > 0)
            .Select(t => t.TypeId)
            .Distinct()
            .ToListAsync(ct);

        var resolvedTypeIds = await db.CostBasisEntries
            .Where(e => e.CharacterId == characterId && e.Source != CostBasisSource.None)
            .Select(e => e.TypeId)
            .ToHashSetAsync(ct);

        var openTypeIds = matchableTypeIds
            .Where(t => itemByType.ContainsKey(t) && !resolvedTypeIds.Contains(t))
            .OrderByDescending(t => itemByType[t].CurrentMarketValue) // Wertvolle zuerst
            .ToList();

        _logger.LogInformation("Cost basis deduction: {Open} of {Matchable} item types still open",
            openTypeIds.Count, matchableTypeIds.Count);

        if (openTypeIds.Count == 0) return;

        // TypeIds im Job persistieren, damit ein Resume exakt an der Abbruchstelle
        // weiterarbeiten kann (Stabilität der Reihenfolge über Neustarts hinweg).
        var parameters = System.Text.Json.JsonSerializer.Serialize(openTypeIds);

        var job = await jobManager.CreateJobAsync(DeductionJobType,
            "Echte Einkaufspreise ermitteln", characterId, total: openTypeIds.Count,
            parametersJson: parameters);
        await RunDeductionCoreAsync(scope, db, jobManager, job, characterId, ct,
            typeIdsOverride: openTypeIds);
    }

    private async Task RunDeductionCoreAsync(IServiceScope scope, WalletDbContext db,
        IBackgroundJobManager jobManager, BackgroundJob job, int characterId, CancellationToken ct,
        List<int>? typeIdsOverride = null)
    {
        try
        {
            await RunDeductionCoreInnerAsync(scope, db, jobManager, job, characterId, ct, typeIdsOverride);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // Shutdown — Status wird beim nächsten Start als Interrupted behandelt
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cost basis deduction failed (job {JobId})", job.Id);
            await jobManager.MarkFailedAsync(job.Id, ex.Message);
        }
    }

    private async Task RunDeductionCoreInnerAsync(IServiceScope scope, WalletDbContext db,
        IBackgroundJobManager jobManager, BackgroundJob job, int characterId, CancellationToken ct,
        List<int>? typeIdsOverride = null)
    {
        ct.ThrowIfCancellationRequested();

        // Alle Kauf-Transaktionen des Charakters aus der lokalen Spiegelung laden.
        var buyTransactions = await db.WalletTransactionRecords
            .Where(t => t.CharacterId == characterId && t.IsBuy && t.Quantity > 0)
            .OrderBy(t => t.Date)
            .ToListAsync(ct);

        var buysByType = buyTransactions
            .GroupBy(t => t.TypeId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(t => t.Date).Take(10).ToList());

        _logger.LogInformation("Cost basis deduction: {TypeCount} item types with buy transactions available",
            buysByType.Count);

        List<int>? typeIds = typeIdsOverride;
        if (typeIds == null && !string.IsNullOrEmpty(job.ParametersJson))
        {
            // Resume: Arbeitsliste aus dem persistierten Job-Parameter rekonstruieren
            try
            {
                typeIds = System.Text.Json.JsonSerializer.Deserialize<List<int>>(job.ParametersJson);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cost basis deduction: could not parse job parameters (job {JobId})", job.Id);
            }
        }
        if (typeIds == null)
        {
            // Fallback (alte Jobs ohne Parameter): Gesamtliste aus Inventory holen,
            // um die Reihenfolge stabil zu halten.
            var inventoryService = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            var items = await inventoryService.GetInventoryAsync(characterId);
            typeIds = items
                .OrderByDescending(i => i.CurrentMarketValue)
                .Select(i => i.TypeId)
                .ToList();
        }

        var total = typeIds.Count;
        var processed = 0;
        var matched = 0;
        var skippedBecauseAlreadySet = 0;

        foreach (var typeId in typeIds)
        {
            ct.ThrowIfCancellationRequested();

            // Pause respektieren: Job wurde pausiert → abbrechen, Status bleibt Paused
            var fresh = await db.BackgroundJobs.FindAsync(job.Id);
            if (fresh == null) return;
            if (fresh.Status == BackgroundJobStatus.Paused)
            {
                _logger.LogInformation("Cost basis deduction: job {JobId} paused — stopping", job.Id);
                return;
            }
            if (fresh.Status == BackgroundJobStatus.Failed)
            {
                return;
            }

            if (processed < job.Current) // Resume: bereits erledigte überspringen
            {
                processed++;
                continue;
            }

            // Cost Basis existiert bereits (Source != None)? Dann nicht überschreiben.
            var existing = await db.CostBasisEntries
                .FirstOrDefaultAsync(e => e.CharacterId == characterId && e.TypeId == typeId, ct);
            if (existing != null && existing.Source != CostBasisSource.None)
            {
                skippedBecauseAlreadySet++;
                processed++;
                continue;
            }

            // Versuch 1: echte Kauf-Transaktionen
            if (buysByType.TryGetValue(typeId, out var buys))
            {
                var totalQty = buys.Sum(t => (double)t.Quantity);
                var totalCost = buys.Sum(t => t.UnitPrice * t.Quantity);
                var avgPrice = totalQty > 0 ? totalCost / totalQty : (double?)null;

                if (avgPrice.HasValue)
                {
                    var entry = new CostBasisEntry
                    {
                        CharacterId = characterId,
                        TypeId = typeId,
                        Value = avgPrice.Value,
                        Source = CostBasisSource.Transaction,
                        PurchaseDate = buys[0].Date,
                        UpdatedAt = DateTime.UtcNow
                    };
                    await UpsertEntryAsync(db, entry, ct);
                    matched++;
                }
            }

            processed++;
            if (processed % 10 == 0 || processed == total)
            {
                await jobManager.UpdateProgressAsync(job.Id, processed, total);
                _logger.LogInformation("Cost basis deduction: {Processed}/{Total} processed ({Matched} matched)",
                    processed, total, matched);
            }
        }

        await jobManager.UpdateProgressAsync(job.Id, total, total);
        await jobManager.MarkCompletedAsync(job.Id);
        _logger.LogInformation("Cost basis deduction: done — {Matched} matched, {Skipped} already set",
            matched, skippedBecauseAlreadySet);
    }

    // ------------------------------------------------------------------
    // Hilfsfunktionen
    // ------------------------------------------------------------------

    private static async Task UpsertEntryAsync(WalletDbContext db, CostBasisEntry entry, CancellationToken ct)
    {
        var existing = await db.CostBasisEntries
            .FirstOrDefaultAsync(e => e.CharacterId == entry.CharacterId && e.TypeId == entry.TypeId, ct);
        if (existing == null)
        {
            db.CostBasisEntries.Add(entry);
        }
        else
        {
            // Bestehenden Eintrag nicht überschreiben, wenn er manuell oder bereits gesetzt ist;
            // der Fall wird oben schon abgefangen, hier nur Sicherheit für Transaction-Überschreibung.
            if (existing.Source == CostBasisSource.Manual) return;
            existing.Value = entry.Value;
            existing.Source = entry.Source;
            existing.PurchaseDate = entry.PurchaseDate;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
    }

    private static async Task MarkRunningAsInterruptedAsync(WalletDbContext db, CancellationToken ct)
    {
        var running = await db.BackgroundJobs
            .Where(j => j.Status == BackgroundJobStatus.Running)
            .ToListAsync(ct);
        if (running.Count == 0) return;

        foreach (var job in running)
        {
            job.Status = BackgroundJobStatus.Interrupted;
            job.UpdatedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
        // Bewusst ohne Log-Ausgabe pro Job — zu laut bei vielen Jobs.
    }

    // ------------------------------------------------------------------
    // Schätz-Jobs (vom Nutzer über die Übersichtsseite angestoßen)
    // ------------------------------------------------------------------

    private async Task RunEstimateJobsAsync(IServiceScope scope, WalletDbContext db,
        IBackgroundJobManager jobManager, int characterId, CancellationToken ct)
    {
        var job = await db.BackgroundJobs
            .Where(j => j.JobType == EstimateJobType && j.CharacterId == characterId
                     && (j.Status == BackgroundJobStatus.Running
                      || j.Status == BackgroundJobStatus.Interrupted
                      || j.Status == BackgroundJobStatus.Paused))
            .OrderByDescending(j => j.UpdatedAt)
            .FirstOrDefaultAsync(ct);

        if (job == null) return;

        _logger.LogInformation("Cost basis estimate: job {JobId} is {Status} — processing",
            job.Id, job.Status);
        if (job.Status == BackgroundJobStatus.Paused) return;

        try
        {
            await RunEstimateJobCoreAsync(scope, db, jobManager, job, characterId, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cost basis estimate failed (job {JobId})", job.Id);
            await jobManager.MarkFailedAsync(job.Id, ex.Message);
        }
    }

    private async Task RunEstimateJobCoreAsync(IServiceScope scope, WalletDbContext db,
        IBackgroundJobManager jobManager, BackgroundJob job, int characterId, CancellationToken ct)
    {
        // Parameter aus dem Job lesen: {"typeIds":[...], "regionId":N}
        var (typeIds, regionId) = ParseEstimateParameters(job.ParametersJson);
        if (typeIds == null || typeIds.Count == 0 || !regionId.HasValue)
        {
            _logger.LogWarning("Cost basis estimate: job {JobId} has no valid parameters", job.Id);
            await jobManager.MarkFailedAsync(job.Id, "Ungültige Job-Parameter (typeIds/regionId fehlen).");
            return;
        }

        var esi = scope.ServiceProvider.GetRequiredService<IEsiApiService>();

        // Basispreise: Letzter History-Eintrag pro Type (falls vorhanden)
        var historyByType = await db.MarketHistory
            .Where(h => h.RegionId == regionId.Value && typeIds.Contains(h.TypeId))
            .GroupBy(h => h.TypeId)
            .Select(g => g.OrderByDescending(h => h.Date).First())
            .ToDictionaryAsync(h => h.TypeId, ct);

        // Fallback: ESI adjusted_price für Types ohne History (einmaliger Abruf)
        var marketPrices = await esi.GetMarketPricesAsync();
        var adjustedByType = marketPrices?
            .Where(p => typeIds.Contains(p.TypeId))
            .ToDictionary(p => p.TypeId, p => p.AdjustedPrice) ?? new();

        // Bereits endgültig belegte Items (Manual/Transaction) nie überschreiben
        var lockedTypeIds = await db.CostBasisEntries
            .Where(e => e.CharacterId == characterId
                     && (e.Source == CostBasisSource.Manual || e.Source == CostBasisSource.Transaction)
                     && typeIds.Contains(e.TypeId))
            .Select(e => e.TypeId)
            .ToHashSetAsync(ct);

        var estimated = 0;
        var total = typeIds.Count;

        for (var i = 0; i < total; i++)
        {
            ct.ThrowIfCancellationRequested();

            var typeId = typeIds[i];

            // Pause/Status frisch prüfen (ähnlich wie Deduction)
            var fresh = await db.BackgroundJobs.FindAsync(job.Id);
            if (fresh == null) return;
            if (fresh.Status == BackgroundJobStatus.Paused || fresh.Status == BackgroundJobStatus.Failed)
            {
                _logger.LogInformation("Cost basis estimate: job {JobId} {Status} — stopping", job.Id, fresh.Status);
                return;
            }

            if (i < job.Current) continue; // Resume: bereits erledigte überspringen
            if (lockedTypeIds.Contains(typeId)) continue;

            double? estimate = null;
            var purchaseDate = (DateTime?)null;

            if (historyByType.TryGetValue(typeId, out var history))
            {
                estimate = history.Average;
                purchaseDate = history.Date; // letzter bekannter Markttag als Referenz
            }
            else if (adjustedByType.TryGetValue(typeId, out var adjusted) && adjusted.HasValue)
            {
                estimate = adjusted.Value;
            }

            if (estimate.HasValue)
            {
                var existing = await db.CostBasisEntries
                    .FirstOrDefaultAsync(e => e.CharacterId == characterId && e.TypeId == typeId, ct);
                if (existing == null)
                {
                    db.CostBasisEntries.Add(new CostBasisEntry
                    {
                        CharacterId = characterId,
                        TypeId = typeId,
                        Value = estimate.Value,
                        Source = CostBasisSource.Estimate,
                        PurchaseDate = purchaseDate,
                        EstimateRegionId = regionId,
                        UpdatedAt = DateTime.UtcNow
                    });
                }
                else if (existing.Source == CostBasisSource.Estimate || existing.Source == CostBasisSource.None)
                {
                    existing.Value = estimate.Value;
                    existing.Source = CostBasisSource.Estimate;
                    existing.PurchaseDate = purchaseDate;
                    existing.EstimateRegionId = regionId;
                    existing.UpdatedAt = DateTime.UtcNow;
                }
                estimated++;
            }

            await jobManager.UpdateProgressAsync(job.Id, i + 1, total);

            // Kleine Pause für ESI-Freundlichkeit (nur wenn ESI-Abruf genutzt wurde)
            if (!historyByType.ContainsKey(typeId))
            {
                await Task.Delay(250, ct);
            }
        }

        await jobManager.UpdateProgressAsync(job.Id, job.Total, job.Total);
        await jobManager.MarkCompletedAsync(job.Id);
        _logger.LogInformation("Cost basis estimate: done — {Estimated} of {Total} items estimated (region {RegionId})",
            estimated, job.Total, regionId);
    }

    private static (List<int>? TypeIds, int? RegionId) ParseEstimateParameters(string? parametersJson)
    {
        if (string.IsNullOrEmpty(parametersJson)) return (null, null);
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(parametersJson);
            var root = doc.RootElement;
            var typeIds = root.TryGetProperty("typeIds", out var t)
                ? t.EnumerateArray().Select(e => e.GetInt32()).ToList()
                : null;
            var regionId = root.TryGetProperty("regionId", out var r) ? r.GetInt32() : (int?)null;
            return (typeIds, regionId);
        }
        catch (Exception)
        {
            return (null, null);
        }
    }
}