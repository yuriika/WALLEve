using WALLEve.Models.Mining;

namespace WALLEve.Services.Mining.Interfaces;

/// <summary>
/// Auswertung des persönlichen Mining-Ledgers nach Zeitraum, Erztyp und
/// verfügbarer Systemdimension mit Marktbewertung samt Quelle und Alter (#48).
/// Reine Lese-Auswertung: Es wird nie Bestand oder Kostenbasis gebucht —
/// Aktivitätsmenge ist kein automatischer aktueller Bestand.
/// </summary>
public interface IMiningValuationService
{
    /// <summary>
    /// Liefert die gruppierte Auswertung eines Charakters im Zeitraum
    /// (From inklusiv, To exklusiv). Bewertet wird am neuesten MarketSnapshot
    /// der angegebenen Region; fehlende Preise oder nicht auflösbare Namen
    /// bleiben sichtbar unbekannt (null statt 0/Erfindung).
    /// </summary>
    Task<MiningValuationReport> GetReportAsync(
        int characterId,
        MiningValuationFilter filter,
        CancellationToken ct = default);
}