namespace WALLEve.Models.Trading;

/// <summary>
/// Unveränderliche Historie einer manuellen Korrektur des tatsächlichen Nettos
/// (Issue #60): dokumentiert VOR-Wert, NACH-Wert, Begründung, Quelle und Zeit.
/// Eine Korrektur überschreibt nie die erwartete Spanne und löscht nie die
/// Zuordnung selbst.
/// </summary>
public class ActualNetCorrection
{
    public int Id { get; set; }

    public int AttributionId { get; set; }

    public int TradingOpportunityId { get; set; }

    /// <summary>Owner — Owner-Isolation der Korrektur.</summary>
    public int CharacterId { get; set; }

    /// <summary>Wert VOR der Korrektur (<c>null</c> = war unbekannt).</summary>
    public decimal? PreviousActualNet { get; set; }

    /// <summary>Wert NACH der Korrektur (<c>null</c> = wieder unbekannt).</summary>
    public decimal? NewActualNet { get; set; }

    /// <summary>Begründung der Korrektur (deutsch, Pflicht).</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Herkunft (Werte aus <see cref="AttributionSource"/>).</summary>
    public string Source { get; set; } = AttributionSource.User;

    public DateTime CorrectedAt { get; set; }
}