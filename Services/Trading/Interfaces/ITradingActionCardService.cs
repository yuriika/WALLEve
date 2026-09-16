using WALLEve.Models.Database;
using WALLEve.Models.Risk;

namespace WALLEve.Services.Trading.Interfaces;

/// <summary>
/// Gemeinsame Karten-Engine für Trading-Empfehlungen (Issue #66): baut aus
/// einer <see cref="TradingOpportunity"/> und den bereits geladenen SDE-Namen
/// eine unveränderliche <see cref="TradingActionCardModel"/>. Determinismus: alle
/// Ableitungen (Menge, Payloads, Annahmen, Status) sind reine Funktionen der
/// Eingaben — keine Uhr, kein Netz, kein Live-ESI. Die UI rendert nur noch
/// diese Karten; Hilfsaktionen sind Seitenkanal und ändern die Karte nie.
/// </summary>
public interface ITradingActionCardService
{
    /// <summary>
    /// Baut Karten in der übergebenen Reihenfolge. Fehlende Namen fallen auf
    /// "Type N" / "Location N" zurück; fehlende Daten ergeben ehrliche
    /// Platzhalter ("—") statt erfundener Werte.
    /// </summary>
    IReadOnlyList<TradingActionCardModel> BuildCards(
        IReadOnlyList<TradingOpportunity> opportunities,
        IReadOnlyDictionary<int, string> typeNames,
        IReadOnlyDictionary<long, string> locationNames);

    /// <summary>
    /// Wie <see cref="BuildCards(IReadOnlyList{TradingOpportunity}, IReadOnlyDictionary{int,string}, IReadOnlyDictionary{long,string})"/>,
    /// reichert die Karten zusätzlich mit beobachtetem Routen-Risiko an (#74):
    /// <paramref name="riskByOpportunityId"/> liefert je Opportunity die
    /// Risiko-Zusammenfassung (#73) als reines Enrichment — die Netto-Rechnung
    /// bleibt davon unberührt.
    /// </summary>
    IReadOnlyList<TradingActionCardModel> BuildCards(
        IReadOnlyList<TradingOpportunity> opportunities,
        IReadOnlyDictionary<int, string> typeNames,
        IReadOnlyDictionary<long, string> locationNames,
        IReadOnlyDictionary<int, RouteRiskSummary>? riskByOpportunityId);
}