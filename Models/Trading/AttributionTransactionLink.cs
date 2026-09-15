namespace WALLEve.Models.Trading;

/// <summary>
/// Expliziter Link zwischen einer Zuordnung (<see cref="RecommendationAttribution"/>)
/// und einer konkreten Wallet-Transaktion (Issue #60). Jeder Link beansprucht die
/// angegebene Menge der Transaktion: eine Transaktion kann nicht zwei Empfehlungen
/// voll zugerechnet werden, weil offene Mengen aus diesen Links berechnet werden.
/// </summary>
public class AttributionTransactionLink
{
    public long Id { get; set; }

    public int AttributionId { get; set; }

    public int TradingOpportunityId { get; set; }

    /// <summary>Owner — Owner-Isolation wie bei der Zuordnung selbst.</summary>
    public int CharacterId { get; set; }

    /// <summary>ESI-Transaktions-ID der zugeordneten Wallet-Transaktion.</summary>
    public long TransactionId { get; set; }

    public int TypeId { get; set; }

    /// <summary>Zeitpunkt der Transaktion (Match-Kriterium "Zeit").</summary>
    public DateTime TransactionDate { get; set; }

    /// <summary>Handelsseite (Werte aus <see cref="TradeSide"/>).</summary>
    public string Side { get; set; } = string.Empty;

    /// <summary>Anteil dieser Transaktion, der dieser Zuordnung zugerechnet wird.</summary>
    public int Quantity { get; set; }

    /// <summary>Einzelpreis der Transaktion (ISK).</summary>
    public double UnitPrice { get; set; }

    public DateTime CreatedAt { get; set; }
}