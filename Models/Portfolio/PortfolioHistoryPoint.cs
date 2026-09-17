using WALLEve.Models.Holdings;

namespace WALLEve.Models.Portfolio;

/// <summary>
/// Historischer Portfolio-Punkt mit Wertung (Issue #38): der ausgewertete
/// Zustand EINES vollständigen Holdings-Snapshots, eingefroren zum
/// Sync-Zeitpunkt des Quell-Snapshots.
///
/// Disziplin: Die Wert-Buckets werden strikt getrennt geführt und nie
/// vermischt — Assets (freier physischer Bestand), Orders/Escrow (in
/// Sell-Orders gebundene Items), Basis/Unknown, Wallet-Cashflow und
/// realisierte Ergebnisse stehen als getrennte Spalten. Ein Item landet
/// genau in EINEM Bucket: gebundene Items (LocationFlag "CorpSellOrder")
/// zählen in Escrow, nie zusätzlich in Assets (keine Doppelzählung).
///
/// Historische Marktprovenienz (Akzeptanzkriterium #38-1): Der Punkt friert
/// den Bewertungsmarkt ein (Hub-Name + Region). Ein späterer Marktwechsel
/// überschreibt die Herkunft eines bestehenden Punkts nie — die Auswertung
/// ist idempotent pro Quell-Snapshot-ID.
///
/// Realisiert (Akzeptanzkriterium #38-3): Nur aus belegten, reconcilierten
/// Ledger-Ereignissen (chronologischer Replay der CostBasisLedgerEntries
/// bis <see cref="CapturedAt"/>). Ohne belegte Ereignisse bleibt der Wert
/// null — es wird nie ein Gewinn erfunden.
/// </summary>
public class PortfolioHistoryPoint
{
    public long Id { get; set; }

    /// <summary>Quell-Snapshot. Eindeutig: die erneute Verarbeitung derselben
    /// Quell-Snapshot-ID dupliziert keinen Punkt und überschreibt nichts.</summary>
    public long HoldingSnapshotId { get; set; }

    /// <summary>Owner-Dimension aus dem Quell-Snapshot (denormalisiert).</summary>
    public OwnerType OwnerType { get; set; }

    /// <summary>EVE-ID des Owners (CharacterId bzw. CorporationId).</summary>
    public int OwnerId { get; set; }

    /// <summary>Historischer Anker = Sync-Zeitpunkt des Quell-Snapshots.
    /// Nicht der Erfassungszeitpunkt — spätere Auswertungszeitpunkte ändern
    /// den Punkt nicht.</summary>
    public DateTime CapturedAt { get; set; }

    /// <summary>Herkunft der Rohdaten, z. B. "esi/characters/{id}/assets".</summary>
    public string Source { get; set; } = string.Empty;

    // ---- Bewertungsmarkt-Provenienz (AC #38-1) ----

    /// <summary>Region des aktivierten Hubs, an der bewertet wurde; null = kein Hub aktiv.</summary>
    public int? ValuationRegionId { get; set; }

    /// <summary>Name des aktivierten Hubs zur Auswertungszeit.</summary>
    public string? ValuationHubName { get; set; }

    /// <summary>Typen, für die zum Sync-Zeitpunkt ein Markt-Snapshot des Hubs existierte (bewertbar).</summary>
    public int ValuatedTypeCount { get; set; }

    /// <summary>Typen ohne As-of-Quote des Hubs — explizit Unknown.</summary>
    public int UnknownValuationTypeCount { get; set; }

    // ---- Assets-Bucket: freier physischer Bestand ----

    /// <summary>Roh-Item-Zeilen im freien Bestand.</summary>
    public int AssetsItemCount { get; set; }

    /// <summary>Menge des freien Bestands (Summe der Rohzeilen-Mengen).</summary>
    public long AssetsQuantity { get; set; }

    /// <summary>Marktwert des freien Bestands zur Sync-Zeit am Bewertungsmarkt;
    /// null = keine bewertbare Menge (kein As-of-Quote).</summary>
    public double? AssetsValue { get; set; }

    /// <summary>Zeilen ohne verfügbaren As-of-Quote — explizit Unknown (Valuation).</summary>
    public int UnknownValuationItemCount { get; set; }

    /// <summary>Menge ohne verfügbaren As-of-Quote.</summary>
    public long UnknownValuationQuantity { get; set; }

    // ---- Orders/Escrow-Bucket: in Sell-Orders gebundene Items (AC #38-2) ----

    /// <summary>Roh-Item-Zeilen mit LocationFlag "CorpSellOrder" (gebunden).</summary>
    public int EscrowItemCount { get; set; }

    /// <summary>Menge der gebundenen Items.</summary>
    public long EscrowQuantity { get; set; }

    /// <summary>Marktwert der gebundenen Items; null = ohne As-of-Quote.</summary>
    public double? EscrowValue { get; set; }

    // ---- Basis / Unknown (AC #38-2, #38-3) ----

    /// <summary>Zeilen, deren (Owner, Typ) zum Erfassungszeitpunkt eine
    /// Cost-Basis hatte (CostBasisEntry vorhanden). Nur Character-Owner.</summary>
    public int CostBasisKnownItemCount { get; set; }

    /// <summary>Zeilen ohne bekannte Cost-Basis — explizit Unknown.</summary>
    public int UnknownCostBasisItemCount { get; set; }

    /// <summary>Anschaffungswert der im Snapshot gehaltenen Einheiten mit Basis
    /// (Menge × CostBasisEntry.Value + Escrow-Menge × Value); null ohne Einträge.</summary>
    public double? KnownBasisValue { get; set; }

    /// <summary>Menge der Einheiten ohne bekannte Basis (freier Bestand + Escrow).</summary>
    public long UnknownBasisQuantity { get; set; }

    /// <summary>Marktwert der Einheiten ohne bekannte Basis — separat ausgewiesen,
    /// nie in KnownBasisValue oder realisierte Ergebnisse eingemischt.</summary>
    public double? UnknownBasisMarketValue { get; set; }

    // ---- Cashflow (Wallet) — getrennt von Bewertung und Realisiertem ----

    /// <summary>Anzahl lokaler Wallet-Transaktionen bis CapturedAt (Character).</summary>
    public int WalletTransactionCount { get; set; }

    /// <summary>Verkaufserlöse (Cash-Zufluss) bis CapturedAt; null ohne Wallet-Daten (z. B. Corporation).</summary>
    public double? WalletCashInflow { get; set; }

    /// <summary>Kaufausgaben (Cash-Abfluss) bis CapturedAt; null ohne Wallet-Daten.</summary>
    public double? WalletCashOutflow { get; set; }

    // ---- Realisiert (AC #38-3) ----

    /// <summary>
    /// Kumulierter realisierter Gewinn/Verlust aus dem chronologischen Replay der
    /// belegten Ledger-Ereignisse bis CapturedAt; null = keine belegten Ereignisse
    /// (oder kein Character-Owner) — dann bleibt das Ergebnis explizit unknown.
    /// </summary>
    public double? RealizedProfit { get; set; }

    public HoldingSnapshot SourceSnapshot { get; set; } = null!;
}