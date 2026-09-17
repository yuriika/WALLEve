using Microsoft.EntityFrameworkCore;
using WALLEve.Data;
using WALLEve.Models.Database;
using WALLEve.Services.Industry;
using WALLEve.Services.Market.Interfaces;

namespace WALLEve.Services.Market;

/// <summary>
/// Zusammenstellung aller Hintergrund-Syncs eines Charakters mit Beschreibung und
/// letzter Ausführung (aus den persistierten BackgroundJobs).
/// WICHTIG: Neue Sync-Jobs hier in <see cref="Definitions"/> ergänzen — die Liste
/// speist die „Syncs"-Übersicht auf der Character-Seite und muss zur Doku passen.
/// </summary>
public class SyncOverviewService : ISyncOverviewService
{
    private readonly WalletDbContext _db;

    public SyncOverviewService(WalletDbContext db)
    {
        _db = db;
    }

    // (JobType, Name, Beschreibung, abgedeckter Zeitraum, Häufigkeit, manuell triggerbar?, Hinweis)
    private static readonly (string Type, string Name, string Description, string Coverage, string Frequency, bool CanTrigger, string? Hint)[] Definitions =
    {
        ("CostBasisSink",
            "Wallet-Transaktionen spiegeln",
            "Spiegelt deine ESI-Wallet-Transaktionen in die lokale DB, damit die Historie dauerhaft wächst. Rohdaten für echte Einkaufspreise.",
            "~letzte 30 Tage pro Lauf (ESI-Fenster)",
            "täglich (24 h), sobald die App läuft",
            true, null),
        ("CostBasisDeduction",
            "Echte Einkaufspreise ermitteln",
            "Leitet aus den gespiegelten Kauf-Transaktionen deinen echten durchschnittlichen Einkaufspreis ab (Source = Transaction).",
            "alle gespiegelten Käufe des Chars",
            "automatisch bei neuen Käufen",
            false, "läuft automatisch, sobald neue Käufe gespiegelt sind"),
        (CostBasisService.EstimateJobTypeConst,
            "Einkaufspreise schätzen",
            "Schätzt fehlende Einkaufspreise als Vorschlag (Source = Estimate) aus Markt-History bzw. ESI-Referenzpreis in der gewählten Region.",
            "manuell ausgewählte Items",
            "manuell",
            false, "braucht eine Item-Auswahl → ⟶ Einkaufspreise"),
        (CostBasisService.InventoryScanJobTypeConst,
            "Komplett-Scan (Initial Sync)",
            "Schätzt ALLE Items ohne Einkaufspreis und analysiert danach den gesamten Bestand auf Verkaufs-Chancen (Opportunities).",
            "gesamter Bestand",
            "manuell (einmalig)",
            true, null),
        (IndustryJobsSyncExecutor.JobType,
            "Industrie-Jobs synchronisieren",
            "Synchronisiert deine aktiven und historischen Character-Industriejobs (Fertigung, Forschung, Kopieren) aus ESI in die lokale DB — Statuswechsel werden idempotent übernommen, historische Jobs bleiben erhalten.",
            "~letzte 90 Tage pro Lauf (ESI-Fenster)",
            "täglich (24 h), sobald die App läuft",
            true, null),
    };

    public async Task<List<CharacterSyncInfo>> GetSyncOverviewAsync(int characterId)
    {
        var result = new List<CharacterSyncInfo>();

        foreach (var def in Definitions)
        {
            var lastCompleted = await _db.BackgroundJobs
                .Where(j => j.JobType == def.Type && j.CharacterId == characterId
                         && j.Status == BackgroundJobStatus.Completed)
                .OrderByDescending(j => j.CompletedAt)
                .FirstOrDefaultAsync();

            var active = await _db.BackgroundJobs
                .Where(j => j.JobType == def.Type && j.CharacterId == characterId
                         && (j.Status == BackgroundJobStatus.Running
                          || j.Status == BackgroundJobStatus.Paused
                          || j.Status == BackgroundJobStatus.Interrupted))
                .OrderByDescending(j => j.UpdatedAt)
                .FirstOrDefaultAsync();

            // Letzter fehlgeschlagener Lauf: sichtbar machen, damit die UI
            // Fehler/anstehenden „stale Stand" mit Alter anzeigen kann.
            var lastFailed = await _db.BackgroundJobs
                .Where(j => j.JobType == def.Type && j.CharacterId == characterId
                         && j.Status == BackgroundJobStatus.Failed)
                .OrderByDescending(j => j.CompletedAt)
                .FirstOrDefaultAsync();

            result.Add(new CharacterSyncInfo
            {
                JobType = def.Type,
                Name = def.Name,
                Description = def.Description,
                Coverage = def.Coverage,
                Frequency = def.Frequency,
                CanTrigger = def.CanTrigger,
                TriggerHint = def.Hint,
                LastCompletedAt = lastCompleted?.CompletedAt,
                LastCurrent = lastCompleted?.Current,
                LastTotal = lastCompleted?.Total,
                ActiveStatus = active?.Status,
                ActiveCurrent = active?.Current,
                ActiveTotal = active?.Total,
                LastFailedAt = lastFailed?.CompletedAt,
                LastError = lastFailed?.LastError
            });
        }

        return result;
    }
}