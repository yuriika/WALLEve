using WALLEve.Data;
using WALLEve.Models.Database;
using WALLEve.Models.Trading;
using WALLEve.Services.Trading.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace WALLEve.Services.Trading;

/// <summary>
/// Implementierung des Opportunity-Lebenszyklus (Issue #45): zentrale Übergangsregeln
/// (nur aus planned sind Wechsel erlaubt; Endzustände sind terminal), Owner-Isolation
/// für manuelle Markierungen und additive Historie mit Zeit + Quelle. Ablauf und
/// Invalidierung markieren nur — sie löschen die ursprüngliche Empfehlung und ihre
/// Inputs (TradeContract) bewusst nicht.
/// </summary>
public sealed class TradeStatusService : ITradeStatusService
{
    private const string ExpiryReason = "Automatisch abgelaufen (Gültigkeit überschritten); Empfehlung bleibt erhalten.";

    private readonly WalletDbContext _db;

    public TradeStatusService(WalletDbContext db)
    {
        _db = db;
    }

    public async Task<TradeStatusChangeResult> MarkAsync(
        int characterId, int opportunityId, TradeStatus target,
        string? reason = null, CancellationToken ct = default)
    {
        var opportunity = await _db.TradingOpportunities
            .SingleOrDefaultAsync(o => o.Id == opportunityId, ct);
        if (opportunity is null)
            return TradeStatusChangeResult.Failed($"Opportunity {opportunityId} nicht gefunden.");

        // Owner-Isolation: nur der Owner darf den Status seiner Opportunity ändern.
        if (opportunity.CharacterId != characterId)
            return TradeStatusChangeResult.Failed("Fremde Opportunity: Statuswechsel nur durch den Owner.");

        var current = TradeStatusExtensions.FromDatabaseValue(opportunity.Status);
        if (current is null)
            return TradeStatusChangeResult.Failed($"Unbekannter Status '{opportunity.Status}' — kein stiller Fallback.");

        // Idempotenz: gleicher Zielstatus ist kein Fehler, aber auch keine neue Historie.
        if (current.Value == target)
            return TradeStatusChangeResult.Ok(target, changed: false);

        if (!current.Value.IsAllowedTransition(target))
            return TradeStatusChangeResult.Failed(
                $"Übergang nicht erlaubt: {current.Value.ToDatabaseValue()} → {target.ToDatabaseValue()}.");

        var changedAt = DateTime.UtcNow;
        ApplyTransition(opportunity, target, TradeStatusSource.User, changedAt,
            reason ?? (target == TradeStatus.Executed ? "Manuell als ausgeführt markiert." : null));

        await _db.SaveChangesAsync(ct);
        return TradeStatusChangeResult.Ok(target, changed: true);
    }

    public async Task<int> ApplyExpiryAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        // Nur noch nicht terminale (planned/active) Opportunities laufen ab;
        // executed/dismissed/expired/invalid bleiben unberührt (idempotent).
        var expired = await _db.TradingOpportunities
            .Where(o => o.ExpiresAt < nowUtc
                     && TradeStatusExtensions.PlannedStorageValues.Contains(o.Status))
            .ToListAsync(ct);
        if (expired.Count == 0)
            return 0;

        foreach (var opportunity in expired)
        {
            var current = TradeStatusExtensions.FromDatabaseValue(opportunity.Status);
            if (current is null)
                continue; // unbekannter Status: nicht anfassen

            ApplyTransition(opportunity, TradeStatus.Expired,
                TradeStatusSource.System, nowUtc, ExpiryReason);
        }

        await _db.SaveChangesAsync(ct);
        return expired.Count;
    }

    public bool InvalidateStaged(TradingOpportunity opportunity, string reason, DateTime changedAt)
    {
        ArgumentNullException.ThrowIfNull(opportunity);

        var current = TradeStatusExtensions.FromDatabaseValue(opportunity.Status);
        if (current is null)
            return false; // unbekannter Ausgangs-Status: kein stiller Fallback

        if (current.Value == TradeStatus.Invalid)
            return true; // idempotent: bereits ungültig, nichts zu stagen

        if (!current.Value.IsAllowedTransition(TradeStatus.Invalid))
            return false; // terminale Zustände (z. B. executed) werden nicht invalidisiert

        ApplyTransition(opportunity, TradeStatus.Invalid,
            TradeStatusSource.System, changedAt, reason);
        return true;
    }

    private void ApplyTransition(TradingOpportunity opportunity, TradeStatus to,
        TradeStatusSource source, DateTime changedAt, string? reason)
    {
        // Der historische Ausgangswert dokumentiert exakt, was gespeichert war —
        // auch den Legacy-Wert "active" (vor Issue #45); keine Normalisierung der Chronik.
        var fromValue = opportunity.Status;
        opportunity.Status = to.ToDatabaseValue();
        // Nur die Ausführungs-Markierung setzt den Ausführungszeitpunkt; ein bereits
        // gesetzter Wert (manuelle Angabe) wird nicht überschrieben.
        if (to == TradeStatus.Executed && opportunity.ExecutedAt is null)
            opportunity.ExecutedAt = changedAt;

        _db.TradeStatusChanges.Add(new TradeStatusChange
        {
            TradingOpportunityId = opportunity.Id,
            CharacterId = opportunity.CharacterId,
            FromStatus = fromValue,
            ToStatus = to.ToDatabaseValue(),
            Source = source.ToDatabaseValue(),
            ChangedAt = changedAt,
            Reason = reason
        });
    }
}