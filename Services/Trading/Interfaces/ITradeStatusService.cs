using WALLEve.Models.Trading;

namespace WALLEve.Services.Trading.Interfaces;

/// <summary>
/// Ergebnis eines Statuswechsel-Versuchs (Issue #45). <see cref="Changed"/> = false
/// bedeutet eine idempotente Wiederholung (Zielstatus bereits erreicht) — kein Fehler,
/// aber auch kein neuer Historie-Eintrag.
/// </summary>
public sealed record TradeStatusChangeResult(bool Success, string? Error, TradeStatus? Target, bool Changed)
{
    public static TradeStatusChangeResult Ok(TradeStatus target, bool changed) => new(true, null, target, changed);

    public static TradeStatusChangeResult Failed(string error) => new(false, error, null, false);
}

/// <summary>
/// Verwaltet den Lebenszyklus-Status von Trading-Opportunities (Issue #45):
/// erlaubte/verbotene Übergänge, Owner-Isolation, persistierte Historie mit Zeit
/// und Quelle. Manuelle Markierungen sind Nutzerangaben (<see cref="TradeStatusSource.User"/>);
/// Ablauf/Invalidierung sind Systemübergänge (<see cref="TradeStatusSource.System"/>) und
/// löschen die ursprüngliche Empfehlung samt Inputs (TradeContract) NIE.
/// </summary>
public interface ITradeStatusService
{
    /// <summary>
    /// Markiert eine Opportunity als Zielstatus (manuelle Nutzerangabe, Quelle = User).
    /// Nur der Owner (CharacterId) darf wechseln; Übergänge folgen der erlaubten Matrix;
    /// Wiederholung desselben Status ist idempotent (kein neuer Historie-Eintrag).
    /// </summary>
    Task<TradeStatusChangeResult> MarkAsync(
        int characterId, int opportunityId, TradeStatus target,
        string? reason = null, CancellationToken ct = default);

    /// <summary>
    /// System-Hygiene: markiert ALLE abgelaufenen, noch nicht terminalen Opportunities
    /// (Status planned/active, ExpiresAt &lt; now) als <see cref="TradeStatus.Expired"/> mit
    /// Historie und Quelle System. Löscht nichts; idempotent (bereits terminale bleiben unberührt).
    /// </summary>
    Task<int> ApplyExpiryAsync(DateTime nowUtc, CancellationToken ct = default);

    /// <summary>
    /// Reine Übergangslogik für den Analyse-Lauf (kein SaveChanges — der Aufrufer
    /// persistiert mit der eigenen Transaktion): markiert eine wegfällige aktive
    /// Opportunity als <see cref="TradeStatus.Invalid"/> und stagt den Historie-Eintrag.
    /// Unbekannte Ausgangs-Status oder verbotene Übergänge lehnen ab (false).
    /// </summary>
    bool InvalidateStaged(WALLEve.Models.Database.TradingOpportunity opportunity, string reason, DateTime changedAt);
}