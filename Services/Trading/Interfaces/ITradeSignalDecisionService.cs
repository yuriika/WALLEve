using System.Security.Cryptography;
using System.Text;
using WALLEve.Models.Database;
using WALLEve.Models.Trading;

namespace WALLEve.Services.Trading.Interfaces;

/// <summary>Grund, aus dem ein Signal unterdrückt wird; null = Signal wird gemeldet.</summary>
public enum TradeSignalSuppression
{
    /// <summary>Kein Hindernis — das Signal ist neu und darf gemeldet werden.</summary>
    None = 0,

    /// <summary>Kandidaten seit der letzten Meldung materiell unverändert (gleicher Fingerprint).</summary>
    UnchangedResult,

    /// <summary>Letzte Meldung liegt innerhalb des Profil-Cooldowns.</summary>
    CooldownActive,

    /// <summary>Profil wurde vom Owner manuell deaktiviert.</summary>
    ManualDeactivation
}

/// <summary>
/// Ergebnis der reinen Signalentscheidung (Issue #68): trennt ein materiell
/// neues Ergebnis von einer Wiederholung des zuletzt gemeldeten Zustands.
/// <see cref="Fingerprint"/> ist der deterministische Hash der (Owner-)Kandidaten,
/// der bei einer Meldung persistiert werden muss.
/// </summary>
public sealed record TradeSignalDecision(bool Signal, TradeSignalSuppression Suppression, string Fingerprint)
{
    public static TradeSignalDecision Reported(string fingerprint) => new(true, TradeSignalSuppression.None, fingerprint);

    public static TradeSignalDecision Suppressed(TradeSignalSuppression reason, string fingerprint)
        => new(false, reason, fingerprint);
}

/// <summary>
/// Reine Signalentscheidung für Trading-Empfehlungen (Issue #68): vergleicht die
/// aktuell gefilterten Kandidaten eines Owners per Fingerprint mit der zuletzt
/// gemeldeten Menge und respektiert Cooldown und manuelle Deaktivierung des
/// Profils. Zustandsfrei und deterministisch: die Entscheidung hängt nur von
/// (Profil, Kandidaten, utcNow) ab und trifft keinen externen Dienst — die
/// Persistenz der gemeldeten Zustände übernimmt der Aufrufer mit
/// <see cref="RecordReported"/>. Die Auslieferung (Notification, Desktop) ist
/// ausdrücklich NICHT Teil dieses Services (Issue #68, Nicht-Ziele).
/// </summary>
public interface ITradeSignalDecisionService
{
    /// <summary>
    /// Entscheidet, ob die Kandidaten eine materiell neue Chance darstellen.
    /// Nur Kandidaten des Profil-Owners fließen in den Fingerprint ein
    /// (Owner-Isolation). Ein fehlendes Profil ist kein Signal-Hindernis: ohne
    /// gespeicherten Fingerprint gilt jede Kandidatenmenge als neu.
    /// </summary>
    TradeSignalDecision Evaluate(TradeProfile profile, IReadOnlyList<TradingOpportunity> candidates, DateTime utcNow);

    /// <summary>
    /// Persistiert den gemeldeten Zustand am Profil (Fingerprint + Zeitpunkt).
    /// Reine Mutation der übergebenen Entität; das Speichern übernimmt der
    /// Aufrufer in seiner eigenen Transaktion.
    /// </summary>
    void RecordReported(TradeProfile profile, string fingerprint, DateTime utcNow);
}