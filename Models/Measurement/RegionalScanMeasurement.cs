namespace WALLEve.Models.Measurement;

/// <summary>
/// Telemetrie einer einzelnen abgerufenen Regionalscan-Seite (Transportebene, #67).
/// Wird vom Scan-Pfad (<c>EsiApiService.GetAllRegionalMarketOrdersAsync</c>) gemeldet
/// und vom Messdienst zu Regionsergebnissen aggregiert.
/// </summary>
public sealed class RegionalScanPageTelemetry
{
    public int RegionId { get; set; }
    public int Page { get; set; }
    public int OrderCount { get; set; }

    /// <summary>Content-Length der Antwort (Bytes über Leitung); null bei 304-Cache-Treffer.</summary>
    public long? ContentLength { get; set; }

    public long ElapsedMs { get; set; }

    /// <summary>Daten kamen aus dem ETag/304-Cache (kein neuer Transfer).</summary>
    public bool FromCache { get; set; }

    /// <summary>Daten dieser Seite sind verwertbar (ErrorCategory None).</summary>
    public bool Success { get; set; }
}

/// <summary>
/// Messergebnis eines Regionalscan-Laufs für EINE Region.
/// Enthält ausschließlich beobachtete Werte, keine Schätzungen.
/// </summary>
public sealed class RegionalScanRegionResult
{
    public int RegionId { get; init; }
    public string RegionName { get; init; } = "";
    public DateTime StartedUtc { get; init; }
    public DateTime FinishedUtc { get; init; }
    public int Pages { get; init; }

    /// <summary>Seiten, deren Daten aus dem ETag/304-Cache kamen (kein Transfer).</summary>
    public int CachedPages { get; init; }

    public int Orders { get; init; }

    /// <summary>Summe der Content-Length aller übertragenen Seiten (Bytes).</summary>
    public long BytesTransferred { get; init; }

    public long ElapsedMs { get; init; }
    public long DatabaseBytesBefore { get; init; }
    public long DatabaseBytesAfter { get; init; }

    /// <summary>Seiten ohne verwertbare Daten (Fehlerbudget-Verbrauch).</summary>
    public int FailedPages { get; init; }

    /// <summary>Alle Seiten wurden atomar geladen (vollständiges Ergebnis).</summary>
    public bool Completed { get; init; }

    public string? FailureNote { get; init; }

    /// <summary>Gzip-Probe: komprimierte Transfergröße einer repräsentativen Seite (null = Probe nicht möglich).</summary>
    public long? GzipProbeCompressedBytes { get; init; }

    /// <summary>Gzip-Probe: dekomprimierte Größe derselben Seite.</summary>
    public long? GzipProbeDecompressedBytes { get; init; }
}

/// <summary>
/// Gesamtreport eines Messlaufs: Quelle, Zeitraum, Datenbankpfad und alle Regionen.
/// Wird als JSON-Artefakt persistiert (reproduzierbar, tokenfrei).
/// </summary>
public sealed class RegionalScanMeasurementReport
{
    public string Source { get; init; } =
        "WALL-EVE Regionalscan-Messung (EsiApiService.GetAllRegionalMarketOrdersAsync, öffentliche ESI-Endpunkte, ohne Authentifizierung)";
    public string EsiBaseUrl { get; init; } = "";
    public DateTime MeasuredAtUtc { get; init; }
    public string DatabasePath { get; init; } = "";
    public List<RegionalScanRegionResult> Regions { get; init; } = new();
    public List<string> Notes { get; init; } = new();
}

/// <summary>
/// Aus Messdaten abgeleitete, evidenzbasierte technische Grenzempfehlungen (#67).
/// Grundsatz: Empfohlen wird nur, was tatsächlich gemessen wurde — ohne vollständige
/// Messung gibt es keine Empfehlung (keine geratenen Produktionslimits).
/// </summary>
public sealed class RegionalScanLimitRecommendations
{
    public int CompletedRegionCount { get; set; }
    public int TotalPages { get; set; }
    public int TotalOrders { get; set; }
    public long TotalBytes { get; set; }
    public int MaxPagesObserved { get; set; }
    public int MaxOrdersObserved { get; set; }
    public long MaxBytesObserved { get; set; }
    public long MaxElapsedMsObserved { get; set; }

    /// <summary>Summe der fehlgeschlagenen Seiten über alle Regionen (auch unvollständige).</summary>
    public int TotalFailedPages { get; set; }

    /// <summary>Fehlerbudget: fehlgeschlagene Seiten in Prozent aller Seiten (0 = Budget unverbraucht).</summary>
    public double ErrorBudgetPercent { get; set; }

    public string Note { get; set; } = "";
}