using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WALLEve.Models.Database;
using WALLEve.Models.Trading;

namespace WALLEve.Services.Trading.Interfaces;

/// <summary>Ergebnis einer Statusmarkierung; bei Fehlern keine Persistenz.</summary>
public sealed record MarkStatusResult(bool Success, TradeStatusChange? Change, string? Error)
{
    public static MarkStatusResult Ok(TradeStatusChange change) => new(true, change, null);

    /// <summary>Idempotente Wiederholung: Status ist bereits gesetzt — kein neuer Eintrag.</summary>
    public static MarkStatusResult AlreadySet(string status) => new(true, null, status);

    public static MarkStatusResult Failed(string error) => new(false, null, error);
}

/// <summary>
/// Markiert Trading-Empfehlungen (Issue #45) mit Status, Zeit und Quelle und
/// erhält die Historie: erlaubte/verbotene Statuswechsel nach einer expliziten
/// Zustandsmaschine, Owner-Isolation, idempotente Wiederholungen. Ablauf und
/// Invalidierung löschen die ursprüngliche Empfehlung/ihre Eingaben NIE.
/// </summary>
public interface ITradeStatusService
{
    /// <summary>
    /// Setzt den Status einer Empfehlung, wenn der Wechsel erlaubt ist und der
    /// anfordernde Charakter der Owner ist. "executed" ist nur als Nutzerangabe
    /// zulässig; "expired" nur durch das System.
    /// </summary>
    Task<MarkStatusResult> MarkAsync(
        int opportunityId,
        int characterId,
        string newStatus,
        TradeStatusSource source,
        string? note,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Historie der Statusänderungen EINER Empfehlung, nur für deren Owner
    /// (Owner-Isolation), zeitlich aufsteigend.
    /// </summary>
    Task<IReadOnlyList<TradeStatusChange>> GetHistoryAsync(
        int opportunityId,
        int characterId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Markiert alle abgelaufenen Empfehlungen (planned/active) als "expired"
    /// (Quelle System) und bewahrt die ursprüngliche Empfehlung und ihre Inputs.
    /// Idempotent: bereits terminale Zustände bleiben unberührt. Gibt die Anzahl
    /// der markierten Empfehlungen zurück.
    /// </summary>
    Task<int> ApplyExpiryAsync(DateTime utcNow, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stagt die Invalidierung EINER Empfehlung (Quelle System): Status → "invalid"
    /// und additiver Historie-Eintrag, ohne etwas zu löschen. Idempotent (bereits
    /// "invalid" → true, nichts zu tun); false, wenn der aktuelle Status keine
    /// System-Invalidierung zulässt (z. B. terminal). Persistiert NICHT selbst —
    /// der Aufrufer speichert im eigenen Zyklus (eine Transaktion).
    /// </summary>
    Task<bool> InvalidateStagedAsync(
        TradingOpportunity opportunity,
        string reason,
        DateTime changedAt);
}