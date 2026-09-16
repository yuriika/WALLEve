using WALLEve.Models.Measurement;

namespace WALLEve.Services.Market;

/// <summary>
/// Wertet Regionalscan-Messungen aus und leitet evidenzbasierte technische
/// Grenzempfehlungen ab (#67). Reine Funktion — keine IO, offline testbar.
/// Grundsatz: Es werden ausschließlich beobachtete Werte gemeldet; ohne vollständige
/// Messung gibt es keine Empfehlung. Fehlgeschlagene Seiten fließen ins Fehlerbudget.
/// </summary>
public static class RegionalScanMeasurementEvaluator
{
    public static RegionalScanLimitRecommendations Evaluate(IReadOnlyList<RegionalScanRegionResult> results)
    {
        var recommendations = new RegionalScanLimitRecommendations();
        var completed = results.Where(r => r.Completed).ToList();

        // Fehlerbudget zählt über ALLE Regionen (auch abgebrochene): der Verbrauch ist
        // unabhängig davon dokumentierenswert.
        recommendations.TotalFailedPages = results.Sum(r => r.FailedPages);

        if (completed.Count == 0)
        {
            recommendations.Note =
                "Keine vollständige Messung vorhanden — es werden keine Grenzwerte empfohlen (niemals geratene Limits).";
            return recommendations;
        }

        recommendations.CompletedRegionCount = completed.Count;
        recommendations.TotalPages = completed.Sum(r => r.Pages);
        recommendations.TotalOrders = completed.Sum(r => r.Orders);
        recommendations.TotalBytes = completed.Sum(r => r.BytesTransferred);
        recommendations.MaxPagesObserved = completed.Max(r => r.Pages);
        recommendations.MaxOrdersObserved = completed.Max(r => r.Orders);
        recommendations.MaxBytesObserved = completed.Max(r => r.BytesTransferred);
        recommendations.MaxElapsedMsObserved = completed.Max(r => r.ElapsedMs);
        recommendations.ErrorBudgetPercent = recommendations.TotalPages > 0
            ? Math.Round(recommendations.TotalFailedPages * 100.0 / recommendations.TotalPages, 3)
            : 0;

        recommendations.Note =
            "Empfehlungen sind das gemessene Envelope (Maximum der beobachteten Werte), keine Produktionslimits. " +
            "Ein Messlauf ist repräsentativ, aber punktuell — die Werte vor der Verwendung als Limit verifizieren.";
        return recommendations;
    }
}