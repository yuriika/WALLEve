using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using WALLEve.Models.Database;
using WALLEve.Models.Trading;
using WALLEve.Services.Trading.Interfaces;

namespace WALLEve.Services.Trading;

/// <summary>
/// Reine Signalentscheidung für Trading-Empfehlungen (Issue #68): verhindert den
/// Meldungssturm bei unveränderten Ergebnissen und über Neustarts hinweg, indem
/// die aktuell gefilterten Kandidaten eines Owners als deterministischer
/// Fingerprint mit der zuletzt gemeldeten Menge verglichen werden. Zusätzlich
/// respektiert die Entscheidung den Profil-Cooldown und die manuelle
/// Deaktivierung. Die Entscheidung ist eine reine Funktion von
/// (Profil, Kandidaten, utcNow) — bewusst ohne Datenbank- oder
/// Uhrzeit-Abhängigkeit, damit Grenzfälle deterministisch testbar sind.
/// </summary>
/// <remarks>
/// Materialität: Preise gehen mit zwei Nachkommastellen, Gewinn und Score auf
/// ganze Einheiten gerundet in den Fingerprint ein — eine Schwankung unterhalb
/// dieser Auflösung ist keine „materiell neue Chance" und erzeugt kein Signal.
/// Der Fingerprint ist sortierungsunabhängig (stabile Sortierung nach Typ/Ort),
/// restartfest (persistiert auf dem Profil) und versionsgebunden
/// (v1-Präfix — eine Änderung der Fingerprint-Definition invalidiert alte
/// Fingerprints bewusst).
/// </remarks>
public sealed class TradeSignalDecisionService : ITradeSignalDecisionService
{
    private const string FingerprintVersion = "v1";

    public TradeSignalDecision Evaluate(
        TradeProfile profile,
        IReadOnlyList<TradingOpportunity> candidates,
        DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(candidates);

        // Owner-Isolation: Nur Kandidaten des Profil-Owners bestimmen das Signal —
        // ein fremder Charakter kann den Report-Zustand dieses Profils nie verändern.
        var ownerCandidates = candidates
            .Where(c => c.CharacterId == profile.CharacterId)
            .ToList();

        var fingerprint = ComputeFingerprint(ownerCandidates);

        // 1) Manuelle Deaktivierung schlägt alles: auch eine materiell neue Chance.
        if (profile.SignalDeactivated)
            return TradeSignalDecision.Suppressed(TradeSignalSuppression.ManualDeactivation, fingerprint);

        // 2) Unverändertes Ergebnis (auch nach Neustart — Fingerprint ist persistiert):
        //    kein Meldungssturm bei identischer Kandidatenmenge.
        if (profile.LastReportedFingerprint is { } last && string.Equals(last, fingerprint, StringComparison.Ordinal))
            return TradeSignalDecision.Suppressed(TradeSignalSuppression.UnchangedResult, fingerprint);

        // 3) Cooldown: nach der letzten Meldung bleibt der Report gesperrt, bis der
        //    Cooldown abgelaufen ist. Die Grenze ist inklusiv — exakt zum Ablauf
        //    (last + CooldownMinutes == utcNow) ist ein neues Signal wieder erlaubt.
        if (profile.CooldownMinutes > 0 && profile.LastReportedAt is { } lastReportedAt
            && lastReportedAt.AddMinutes(profile.CooldownMinutes) > utcNow)
        {
            return TradeSignalDecision.Suppressed(TradeSignalSuppression.CooldownActive, fingerprint);
        }

        return TradeSignalDecision.Reported(fingerprint);
    }

    public void RecordReported(TradeProfile profile, string fingerprint, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(profile);

        profile.LastReportedFingerprint = fingerprint;
        profile.LastReportedAt = utcNow;
    }

    /// <summary>
    /// Deterministischer, sortierungsunabhängiger Hash der Kandidatenmenge.
    /// Öffentlich, weil die Definition des materiellen Fingerprints Teil des
    /// Signal-Vertrags ist und die Regressionstests ihn direkt prüfen.
    /// </summary>
    public static string ComputeFingerprint(IReadOnlyList<TradingOpportunity> candidates)
    {
        // Stabile Reihenfolge: unabhängig von der Lauf-Reihenfolge des Aufrufers
        // erzeugt dieselbe Menge denselben Fingerprint (Restart-Festigkeit).
        var lines = candidates
            .OrderBy(c => c.OpportunityType, StringComparer.Ordinal)
            .ThenBy(c => c.TypeId)
            .ThenBy(c => c.BuyLocationId)
            .ThenBy(c => c.SellLocationId)
            .Select(c => string.Join("|",
                c.OpportunityType,
                c.TypeId.ToString(CultureInfo.InvariantCulture),
                c.BuyLocationId?.ToString(CultureInfo.InvariantCulture) ?? "",
                c.SellLocationId?.ToString(CultureInfo.InvariantCulture) ?? "",
                c.BuyPrice is { } buy ? buy.ToString("0.00", CultureInfo.InvariantCulture) : "",
                c.SellPrice is { } sell ? sell.ToString("0.00", CultureInfo.InvariantCulture) : "",
                c.EstimatedProfit.ToString("0", CultureInfo.InvariantCulture),
                c.Score.ToString("0", CultureInfo.InvariantCulture),
                c.AlgorithmVersion ?? ""));

        var canonical = $"{FingerprintVersion}\n{string.Join("\n", lines)}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash);
    }
}