using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WALLEve.Models.Database;
using WALLEve.Models.Trading;

namespace WALLEve.Services.Trading.Interfaces;

/// <summary>Ergebnis einer Zuordnung; bei Fehlern wird nichts persistiert.</summary>
public sealed record AttributionResult(
    bool Success,
    RecommendationAttribution? Attribution,
    string? Error)
{
    public static AttributionResult Ok(RecommendationAttribution attribution) => new(true, attribution, null);

    public static AttributionResult Failed(string error) => new(false, null, error);
}

/// <summary>Ergebnis einer manuellen Korrektur des tatsächlichen Nettos.</summary>
public sealed record CorrectionResult(
    bool Success,
    RecommendationAttribution? Attribution,
    ActualNetCorrection? Correction,
    string? Error)
{
    public static CorrectionResult Ok(RecommendationAttribution attribution, ActualNetCorrection correction)
        => new(true, attribution, correction, null);

    /// <summary>Idempotente Wiederholung: Wert bereits gesetzt — keine neue Historie.</summary>
    public static CorrectionResult AlreadySet(RecommendationAttribution attribution)
        => new(true, attribution, null, null);

    public static CorrectionResult Failed(string error) => new(false, null, null, error);
}

/// <summary>
/// Ordnet Wallet-Transaktionen evidenzbasiert einer Trading-Empfehlung zu
/// (Issue #60). Verglichen werden Owner, Typ, Seite, Menge und Zeit; die
/// Zuordnung wird über explizite Links (<see cref="AttributionTransactionLink"/>)
/// dokumentiert. Mehrdeutige Lagen bleiben offen (keine zugerechnete Menge, kein
/// tatsächliches Netto). Erwartete Spanne und tatsächliches Netto werden getrennt
/// gespeichert; eine manuelle Korrektur wird mit Historie festgehalten und nie
/// durch einen erneuten Lauf überschrieben. Wiederholte identische Läufe sind
/// idempotent und beanspruchen dieselbe Transaktionsmenge nicht doppelt.
/// </summary>
public interface IRecommendationAttributionService
{
    /// <summary>
    /// Ordnet die übergebenen Wallet-Transaktionen der Empfehlung zu. Die Seite
    /// (<see cref="TradeSide"/>) wird explizit vorgegeben, das Zeitfenster begrenzt
    /// die Kandidaten. Bereits beanspruchte Mengen (Links anderer/älterer
    /// Zuordnungen desselben Owners) stehen nicht erneut zur Verfügung.
    /// </summary>
    Task<AttributionResult> AttributeAsync(
        int opportunityId,
        int characterId,
        string side,
        int expectedQuantity,
        decimal expectedNetMin,
        decimal expectedNetMax,
        DateTime windowStartUtc,
        DateTime windowEndUtc,
        IReadOnlyCollection<WalletTransactionRecord>? transactions,
        CancellationToken cancellationToken = default);

    /// <summary>Zuordnungen einer Empfehlung für den Owner.</summary>
    Task<IReadOnlyList<RecommendationAttribution>> GetAttributionsAsync(
        int opportunityId,
        int characterId,
        CancellationToken cancellationToken = default);

    /// <summary>Explizite Transaktions-Links einer Zuordnung (Owner-Isolation).</summary>
    Task<IReadOnlyList<AttributionTransactionLink>> GetLinksAsync(
        int attributionId,
        int characterId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Korrigiert das tatsächliche Netto manuell. <paramref name="newActualNet"/>
    /// darf <c>null</c> sein ("wieder unbekannt"). Eine Begründung ist Pflicht;
    /// jede Änderung erzeugt einen Historieneintrag.
    /// </summary>
    Task<CorrectionResult> CorrectActualNetAsync(
        int attributionId,
        int characterId,
        decimal? newActualNet,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>Unveränderliche Historie der Netto-Korrekturen einer Zuordnung.</summary>
    Task<IReadOnlyList<ActualNetCorrection>> GetCorrectionHistoryAsync(
        int attributionId,
        int characterId,
        CancellationToken cancellationToken = default);
}