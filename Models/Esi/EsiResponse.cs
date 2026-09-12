namespace WALLEve.Models.Esi;

/// <summary>
/// Fehlerkategorien des ESI-Transports.
/// Bewusst getrennt von der Datenqualität (siehe <see cref="EsiCompleteness"/>):
/// Ein Transportfehler darf einen vorhandenen, aber stale Snapshot begleiten.
/// </summary>
public enum EsiErrorCategory
{
    /// <summary>Nicht bestimmt — Default. Niemals als Erfolg lesen.</summary>
    Unknown = 0,

    /// <summary>Kein Fehler (2xx/304).</summary>
    None,

    /// <summary>404 — Ressource existiert nicht.</summary>
    NotFound,

    /// <summary>401/403 — Token ungültig oder Scope fehlt.</summary>
    AuthenticationFailed,

    /// <summary>420/429 — ESI-Rate- bzw. Error-Limit erreicht.</summary>
    RateLimited,

    /// <summary>5xx — ESI-Serverfehler.</summary>
    ServerError,

    /// <summary>Request wurde abgebrochen (Cancellation).</summary>
    Cancelled,

    /// <summary>Payload ist unlesbar/malformed.</summary>
    Malformed
}

/// <summary>
/// Datenqualität einer ESI-Antwort, unabhängig vom Transportstatus.
/// Default ist <see cref="Unknown"/> — ein nicht bestimmter Zustand darf
/// nie als vollständiges Ergebnis gelesen werden.
/// </summary>
public enum EsiCompleteness
{
    /// <summary>Nicht bestimmt — Default.</summary>
    Unknown = 0,

    /// <summary>Vollständig, aber ohne Daten — kein Payload oder eine gültig
    /// leere Collection (legitimes leeres Ergebnis, z. B. ESI `[]`).</summary>
    CompleteEmpty,

    /// <summary>Vollständige, frische Daten.</summary>
    CompleteData,

    /// <summary>Unvollständig: weitere Seiten erwartet, aber nicht geholt.</summary>
    Partial,

    /// <summary>Daten vorhanden, aber nicht frisch (per 304 referenzierte
    /// Cache-Daten oder ein Transportfehler begleitet einen alten Snapshot).</summary>
    Stale,

    /// <summary>Keine verwertbaren Daten (Transportfehler ohne Daten oder
    /// 304 ohne referenzierbaren Cache).</summary>
    Failed
}

/// <summary>
/// Wrapper für ESI API Responses mit Metadaten.
/// Trägt den vereinbarten Vollständigkeits- und Fehlervertrag: Transportstatus
/// (StatusCode/ErrorCategory) und Datenqualität (Completeness) sind getrennt.
/// </summary>
public class EsiResponse<T>
{
    public T? Data { get; set; }
    public int StatusCode { get; set; }
    public string? ETag { get; set; }
    public DateTime? Expires { get; set; }
    public DateTime? LastModified { get; set; }

    /// <summary>Datenzeit: wann die Daten tatsächlich geholt wurden.</summary>
    public DateTime? FetchedAt { get; set; }

    public int? TotalPages { get; set; }

    /// <summary>Aktuelle Seite dieser Antwort (Seitenfortschritt).</summary>
    public int? Page { get; set; }

    /// <summary>Bereits geholte Seiten (Seitenfortschritt gesamt).</summary>
    public int? PagesFetched { get; set; }

    public RateLimitInfo? RateLimit { get; set; }
    public bool IsSuccess => StatusCode >= 200 && StatusCode < 300;
    public bool IsNotModified => StatusCode == 304;

    /// <summary>Explizite Übersteuerung für Nicht-HTTP-Ursachen (Cancelled/Malformed).</summary>
    private EsiErrorCategory? _errorOverride;

    /// <summary>
    /// Fehlerkategorie: ohne Übersteuerung aus dem StatusCode abgeleitet.
    /// Default ist <see cref="EsiErrorCategory.Unknown"/>, nie automatisch "kein Fehler".
    /// </summary>
    public EsiErrorCategory ErrorCategory
    {
        get => _errorOverride ?? MapErrorCategory(StatusCode);
        set => _errorOverride = value;
    }

    /// <summary>
    /// Datenqualität, aus StatusCode/ErrorCategory/Daten/Seiten abgeleitet.
    /// Bewusst unabhängig vom Transportstatus: Ein Fehler kann einen alten,
    /// vollständigen Snapshot begleiten (dann Stale statt Failed).
    /// Eine leere Collection (z. B. deserialisiertes ESI-`[]`) zählt als
    /// gültig leeres Ergebnis und nicht als Datenbestand.
    /// </summary>
    public EsiCompleteness Completeness
    {
        get
        {
            if (Data == null)
            {
                // 2xx ohne Fehler-Flag → vollständig leeres Ergebnis (z. B. keine Orders)
                if (IsSuccess && ErrorCategory is EsiErrorCategory.None or EsiErrorCategory.Unknown)
                {
                    return EsiCompleteness.CompleteEmpty;
                }

                // 304 ohne referenzierte Cache-Daten ist kein Erfolg
                if (IsNotModified)
                {
                    return EsiCompleteness.Failed;
                }

                // Jeder Transportfehler ohne verwertbare Daten
                if (ErrorCategory is not (EsiErrorCategory.None or EsiErrorCategory.Unknown))
                {
                    return EsiCompleteness.Failed;
                }

                return EsiCompleteness.Unknown;
            }

            // Daten vorhanden: ein Transportfehler begleitet den (alten) Snapshot
            if (ErrorCategory is not (EsiErrorCategory.None or EsiErrorCategory.Unknown))
            {
                return EsiCompleteness.Stale;
            }

            // Per 304 referenzierte Cache-Daten sind vorhanden, aber nicht frisch
            if (IsNotModified)
            {
                return EsiCompleteness.Stale;
            }

            // Seitenfortschritt: weitere Seiten erwartet, aber nicht geholt
            if (TotalPages is > 1 &&
                ((Page.HasValue && Page < TotalPages) || (PagesFetched.HasValue && PagesFetched < TotalPages)))
            {
                return EsiCompleteness.Partial;
            }

            // Gültig leere Collection (z. B. ESI-Listen-Response `[]`)
            if (IsEmptyCollection(Data))
            {
                return IsSuccess ? EsiCompleteness.CompleteEmpty : EsiCompleteness.Unknown;
            }

            if (IsSuccess)
            {
                return EsiCompleteness.CompleteData;
            }

            return EsiCompleteness.Unknown;
        }
    }

    /// <summary>
    /// Erkennt gültig leere Collection-Payloads über die nicht-generische
    /// <see cref="System.Collections.ICollection"/>-Schnittstelle. Deckt
    /// <see cref="List{T}"/>, Arrays, Dictionaries u. Ä. ab;
    /// <see cref="HashSet{T}"/> implementiert nur <c>ICollection&lt;T&gt;</c> und
    /// wird nicht erkannt — für ESI-Listen-Payloads (durchgängig List) irrelevant.
    /// </summary>
    private static bool IsEmptyCollection(object? data)
        => data is System.Collections.ICollection { Count: 0 };

    private static EsiErrorCategory MapErrorCategory(int statusCode)
    {
        return statusCode switch
        {
            >= 200 and < 300 => EsiErrorCategory.None,
            304 => EsiErrorCategory.None,
            401 or 403 => EsiErrorCategory.AuthenticationFailed,
            404 => EsiErrorCategory.NotFound,
            420 or 429 => EsiErrorCategory.RateLimited,
            >= 500 and < 600 => EsiErrorCategory.ServerError,
            _ => EsiErrorCategory.Unknown
        };
    }
}