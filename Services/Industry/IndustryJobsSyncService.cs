using System.Globalization;
using Microsoft.EntityFrameworkCore;
using WALLEve.Data;
using WALLEve.Models.Esi.Character;
using WALLEve.Models.Industry;
using WALLEve.Services.Esi.Interfaces;
using WALLEve.Services.Industry.Interfaces;

namespace WALLEve.Services.Industry;

/// <summary>
/// Idempotente Synchronisation der Character-Industriejobs (#40).
/// Der ESI-Schlüssel job_id wird je Character auf (CharacterId, JobId)
/// gespiegelt; eine erneute Synchronisation ersetzt nur die mutablen
/// Statusfelder (Status, Termine, Cost, SuccessfulRuns) statt neue Zeilen
/// anzulegen. Fehlt ein Job in der ESI-Antwort (API-Fenster ~90 Tage), wird
/// er nicht gelöscht — historische Jobs bleiben erhalten.
/// </summary>
public class IndustryJobsSyncService : IIndustryJobsSyncService
{
    private readonly WalletDbContext _db;
    private readonly IEsiApiService _esiApi;
    private readonly ILogger<IndustryJobsSyncService> _logger;

    public IndustryJobsSyncService(
        WalletDbContext db,
        IEsiApiService esiApi,
        ILogger<IndustryJobsSyncService> logger)
    {
        _db = db;
        _esiApi = esiApi;
        _logger = logger;
    }

    public async Task<IndustryJobsSyncResult> SynchronizeAsync(int characterId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var jobs = await _esiApi.GetCharacterIndustryJobsAsync(characterId, ct);
        if (jobs == null)
        {
            // M0-Vertrag: null = Fehler/Abbruch (keine Teildaten) — der
            // publizierte Job-Zustand bleibt unverändert.
            _logger.LogWarning("Industrie-Job-Sync für Character {CharacterId}: ESI lieferte kein vollständiges Ergebnis", characterId);
            return new IndustryJobsSyncResult
            {
                Success = false,
                Error = "ESI lieferte keine vollständigen Industrie-Jobs (Fehler oder Abbruch).",
                Total = 0
            };
        }

        var existing = await _db.IndustryJobEntries
            .Where(e => e.CharacterId == characterId)
            .ToListAsync(ct);
        var byJobId = existing.ToDictionary(e => e.JobId);

        var now = DateTime.UtcNow;
        var inserted = 0;
        var updated = 0;

        foreach (var job in jobs)
        {
            if (byJobId.TryGetValue(job.JobId, out var stored))
            {
                // Statuswechsel (z. B. active → delivered) und korrigierte
                // Abschluss-/Pause-Termine ersetzen die alten Werte — Idempotenz:
                // eine erfolgreiche Wiederholung mit unveränderten Daten ändert nichts.
                if (ApplyChanges(stored, job, now))
                {
                    updated++;
                }
            }
            else
            {
                _db.IndustryJobEntries.Add(MapNew(characterId, job, now));
                inserted++;
            }
        }

        // Ein einzelner SaveChanges ist atomar: keine Teildaten bei Abbruch.
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Industrie-Job-Sync für Character {CharacterId} abgeschlossen: {Inserted} neu, {Updated} aktualisiert, {Total} Jobs von ESI",
            characterId, inserted, updated, jobs.Count);

        return new IndustryJobsSyncResult
        {
            Success = true,
            Inserted = inserted,
            Updated = updated,
            Total = jobs.Count
        };
    }

    private static IndustryJobEntry MapNew(int characterId, CharacterIndustryJob job, DateTime now)
        => new()
        {
            CharacterId = characterId,
            JobId = job.JobId,
            ActivityId = job.ActivityId,
            BlueprintId = job.BlueprintId,
            BlueprintTypeId = job.BlueprintTypeId,
            BlueprintLocationId = job.BlueprintLocationId,
            OutputLocationId = job.OutputLocationId,
            FacilityId = job.FacilityId,
            StationId = job.StationId,
            ProductTypeId = job.ProductTypeId,
            Status = job.Status,
            StartDate = ToUtc(job.StartDate),
            EndDate = ToUtc(job.EndDate),
            CompletedDate = job.CompletedDate == null ? (DateTime?)null : ToUtc(job.CompletedDate),
            PauseDate = job.PauseDate == null ? (DateTime?)null : ToUtc(job.PauseDate),
            Runs = job.Runs,
            LicensedRuns = job.LicensedRuns,
            SuccessfulRuns = job.SuccessfulRuns,
            Cost = job.Cost,
            Duration = job.Duration,
            InstallerId = job.InstallerId,
            UpdatedAt = now
        };

    /// <summary>
    /// Aktualisiert nur die mutablen Statusfelder eines Bestandsjobs.
    /// Liefert true, wenn sich mindestens ein Feld geändert hat.
    /// </summary>
    private static bool ApplyChanges(IndustryJobEntry stored, CharacterIndustryJob job, DateTime now)
    {
        var changed = false;

        if (stored.Status != job.Status)
        {
            stored.Status = job.Status;
            changed = true;
        }

        var endDate = ToUtc(job.EndDate);
        if (stored.EndDate != endDate)
        {
            stored.EndDate = endDate;
            changed = true;
        }

        var completedDate = job.CompletedDate == null ? (DateTime?)null : ToUtc(job.CompletedDate);
        if (stored.CompletedDate != completedDate)
        {
            stored.CompletedDate = completedDate;
            changed = true;
        }

        var pauseDate = job.PauseDate == null ? (DateTime?)null : ToUtc(job.PauseDate);
        if (stored.PauseDate != pauseDate)
        {
            stored.PauseDate = pauseDate;
            changed = true;
        }

        if (stored.SuccessfulRuns != job.SuccessfulRuns)
        {
            stored.SuccessfulRuns = job.SuccessfulRuns;
            changed = true;
        }

        if (stored.Cost != job.Cost)
        {
            stored.Cost = job.Cost;
            changed = true;
        }

        if (changed)
        {
            stored.UpdatedAt = now;
        }

        return changed;
    }

    /// <summary>
    /// Normalisiert eine ESI-RFC3339-Zeitangabe nach UTC. Ein nicht parsebarer
    /// Wert ist ein ESI-Vertragsbruch und lässt den Lauf fehlschlagen
    /// (M0: keine Teildaten statt stiller Datenkorruption).
    /// </summary>
    private static DateTime ToUtc(string value)
        => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture).UtcDateTime;
}