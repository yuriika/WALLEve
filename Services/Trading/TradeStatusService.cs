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
}