using WALLEve.Models.Database;

namespace WALLEve.Services.Notifications;

/// <summary>
/// In-App-Meldungsfeed für Trading-Chancen (Issue #70).
/// Der Feed ist bewusst ein Singleton im Prozess: Er überlebt Blazor-Reconnects,
/// und paralleles Polling (z.&nbsp;B. mehrere Circuit-Instanzen oder doppelte
/// Timer) kann keine Duplikate erzeugen. Dedupe/Cooldown sind pro Charakter und
/// inhaltlicher Chance definiert; die Browser-Berechtigung spielt hier keinerlei
/// Rolle — verweigerte/fehlende Berechtigung beeinträchtigt den In-App-Feed nie.
/// </summary>
public interface ITradingNotificationService
{
    /// <summary>
    /// Prüft die übergebenen aktiven Chancen eines Charakters gegen Dedupe-Key
    /// und Cooldown und erzeugt für neue Chancen In-App-Meldungen.
    /// Thread-sicher: beliebig viele parallele Aufrufe erzeugen höchstens eine
    /// Meldung pro Chance innerhalb des Cooldown-Fensters.
    /// </summary>
    /// <returns>Anzahl neu veröffentlichter Meldungen.</returns>
    int Publish(int characterId, IReadOnlyList<TradingOpportunity> opportunities, DateTime utcNow);

    /// <summary>Alle Meldungen eines Charakters, neueste zuerst.</summary>
    IReadOnlyList<TradingNotification> GetAll(int characterId);

    /// <summary>Noch ungelesene Meldungen eines Charakters, neueste zuerst.</summary>
    IReadOnlyList<TradingNotification> GetUnseen(int characterId);

    /// <summary>Markiert alle Meldungen eines Charakters als gelesen.</summary>
    void MarkAllSeen(int characterId);
}