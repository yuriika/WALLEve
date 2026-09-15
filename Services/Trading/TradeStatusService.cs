using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using WALLEve.Data;
using WALLEve.Models.Database;
using WALLEve.Models.Trading;
using WALLEve.Services.Trading.Interfaces;

namespace WALLEve.Services.Trading;

/// <summary>
/// Statusverwaltung für Trading-Empfehlungen (Issue #45): persistiert Status,
/// Zeit und Quelle jeder Änderung in <see cref="TradeStatusChange"/>, erzwingt
/// eine explizite Zustandsmaschine (erlaubte/verbotene Wechsel), Owner-Isolation
/// und Idempotenz. Ablauf/Invalidierung löscht die ursprüngliche Empfehlung und
/// ihre Eingaben NIE — nur der Status ändert sich, die Historie wird additiv
/// ergänzt. "executed" wird nie automatisch behauptet (nur Nutzerangabe).
/// </summary>
public sealed class TradeStatusService : ITradeStatusService
{
    private const string ExpiryReason =
        "Automatisch abgelaufen (Gültigkeit überschritten); Empfehlung und Inputs bleiben erhalten.";

    private readonly WalletDbContext _db;

    public TradeStatusService(WalletDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Erlaubte Statuswechsel. Schlüssel = aktueller Status; Wert = Ziel-Status
    /// plus zulässige Quellen. Terminale Zustände (executed/expired/invalid)
    /// haben keine erlaubten Wechsel.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, TradeStatusSource[]>> AllowedTransitions =
        new Dictionary<string, IReadOnlyDictionary<string, TradeStatusSource[]>>
        {
            [RecommendationStatus.Active] = new Dictionary<string, TradeStatusSource[]>
            {
                [RecommendationStatus.Planned] = [TradeStatusSource.User],
                [RecommendationStatus.Executed] = [TradeStatusSource.User],
                [RecommendationStatus.Dismissed] = [TradeStatusSource.User],
                [RecommendationStatus.Expired] = [TradeStatusSource.System],
                [RecommendationStatus.Invalid] = [TradeStatusSource.User, TradeStatusSource.System]
            },
            [RecommendationStatus.Planned] = new Dictionary<string, TradeStatusSource[]>
            {
                [RecommendationStatus.Executed] = [TradeStatusSource.User],
                [RecommendationStatus.Dismissed] = [TradeStatusSource.User],
                [RecommendationStatus.Expired] = [TradeStatusSource.System],
                [RecommendationStatus.Invalid] = [TradeStatusSource.User, TradeStatusSource.System]
            },
            [RecommendationStatus.Dismissed] = new Dictionary<string, TradeStatusSource[]>
            {
                [RecommendationStatus.Executed] = [TradeStatusSource.User],
                [RecommendationStatus.Expired] = [TradeStatusSource.System],
                [RecommendationStatus.Invalid] = [TradeStatusSource.User, TradeStatusSource.System]
            }
        };

    public async Task<MarkStatusResult> MarkAsync(
        int opportunityId,
        int characterId,
        string newStatus,
        TradeStatusSource source,
        string? note,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(newStatus))
            return MarkStatusResult.Failed("Ein Ziel-Status ist erforderlich.");

        var opportunity = await _db.TradingOpportunities
            .SingleOrDefaultAsync(o => o.Id == opportunityId, cancellationToken);
        if (opportunity is null)
            return MarkStatusResult.Failed("Empfehlung nicht gefunden.");

        // Owner-Isolation: nur der Owner der Empfehlung darf ihren Status ändern.
        if (opportunity.CharacterId != characterId)
            return MarkStatusResult.Failed("Fremde Empfehlung: Statuswechsel nur durch den Owner.");

        // Idempotenz: identische Wiederholung erzeugt keine neue Historie.
        if (opportunity.Status == newStatus)
            return MarkStatusResult.AlreadySet(newStatus);

        if (!AllowedTransitions.TryGetValue(opportunity.Status, out var targets))
            return MarkStatusResult.Failed(
                $"Verbotener Statuswechsel: {opportunity.Status} → {newStatus} (terminaler Zustand).");

        if (!targets.TryGetValue(newStatus, out var allowedSources))
            return MarkStatusResult.Failed(
                $"Verbotener Statuswechsel: {opportunity.Status} → {newStatus}.");

        if (source == TradeStatusSource.User && Array.IndexOf(allowedSources, TradeStatusSource.User) < 0)
            return MarkStatusResult.Failed(
                $"Markierung {newStatus} ist über die Quelle „Nutzer\" nicht zulässig (Quelle siehe Historie).");
        if (source == TradeStatusSource.System && Array.IndexOf(allowedSources, TradeStatusSource.System) < 0)
            return MarkStatusResult.Failed(
                $"Markierung {newStatus} ist über die Quelle „System\" nicht zulässig — eine automatische " +
                "Ausführung wird nie behauptet.");

        var fromStatus = opportunity.Status;
        var change = new TradeStatusChange
        {
            TradingOpportunityId = opportunity.Id,
            CharacterId = opportunity.CharacterId,
            FromStatus = fromStatus,
            ToStatus = newStatus,
            Source = source,
            ChangedAt = DateTime.UtcNow,
            Note = note
        };

        opportunity.Status = newStatus;
        if (newStatus == RecommendationStatus.Executed)
            opportunity.ExecutedAt = change.ChangedAt;

        _db.TradeStatusChanges.Add(change);
        await _db.SaveChangesAsync(cancellationToken);

        return MarkStatusResult.Ok(change);
    }

    public async Task<IReadOnlyList<TradeStatusChange>> GetHistoryAsync(
        int opportunityId,
        int characterId,
        CancellationToken cancellationToken = default)
    {
        return await _db.TradeStatusChanges
            .AsNoTracking()
            .Where(c => c.TradingOpportunityId == opportunityId && c.CharacterId == characterId)
            .OrderBy(c => c.ChangedAt)
            .ThenBy(c => c.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> ApplyExpiryAsync(DateTime utcNow, CancellationToken cancellationToken = default)
    {
        // Nur nicht-terminale (planned/active) Empfehlungen laufen ab — executed,
        // dismissed, expired und invalid bleiben unberührt (idempotent, keine neue
        // Historie für bereits markierte).
        var expired = await _db.TradingOpportunities
            .Where(o => o.ExpiresAt < utcNow
                     && RecommendationStatus.PlannedStorageValues.Contains(o.Status))
            .ToListAsync(cancellationToken);
        if (expired.Count == 0)
            return 0;

        var changedAt = DateTime.UtcNow;
        foreach (var opportunity in expired)
        {
            // Der historische Ausgangswert dokumentiert exakt, was gespeichert war —
            // auch den Legacy-Wert "active" (vor Issue #45); keine Normalisierung der Chronik.
            var fromStatus = opportunity.Status;
            opportunity.Status = RecommendationStatus.Expired;
            _db.TradeStatusChanges.Add(new TradeStatusChange
            {
                TradingOpportunityId = opportunity.Id,
                CharacterId = opportunity.CharacterId,
                FromStatus = fromStatus,
                ToStatus = RecommendationStatus.Expired,
                Source = TradeStatusSource.System,
                ChangedAt = changedAt,
                Note = ExpiryReason
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
        return expired.Count;
    }

    public async Task<bool> InvalidateStagedAsync(
        TradingOpportunity opportunity,
        string reason,
        DateTime changedAt)
    {
        ArgumentNullException.ThrowIfNull(opportunity);

        // Idempotent: bereits ungültig — nichts zu tun, kein neuer Historie-Eintrag.
        if (opportunity.Status == RecommendationStatus.Invalid)
            return true;

        // System-Invalidierung nur für Status, die laut Zustandsmaschine einen
        // System-Übergang zu "invalid" erlauben (planned/active/dismissed); terminale
        // Zustände (executed/expired) werden nicht angefasst.
        if (!AllowedTransitions.TryGetValue(opportunity.Status, out var targets)
            || !targets.TryGetValue(RecommendationStatus.Invalid, out var allowedSources)
            || Array.IndexOf(allowedSources, TradeStatusSource.System) < 0)
            return false;

        var fromStatus = opportunity.Status;
        opportunity.Status = RecommendationStatus.Invalid;
        _db.TradeStatusChanges.Add(new TradeStatusChange
        {
            TradingOpportunityId = opportunity.Id,
            CharacterId = opportunity.CharacterId,
            FromStatus = fromStatus,
            ToStatus = RecommendationStatus.Invalid,
            Source = TradeStatusSource.System,
            ChangedAt = changedAt,
            Note = reason
        });

        // Kein SaveChanges hier: der Aufrufer persistiert den gesamten Analyse-Lauf
        // in einem einzigen Zyklus (eine Transaktion).
        return true;
    }
}