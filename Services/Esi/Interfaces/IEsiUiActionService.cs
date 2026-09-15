namespace WALLEve.Services.Esi.Interfaces;

/// <summary>Ergebnis einer EVE-UI-Hilfsaktion (Issue #66).</summary>
public enum EsiUiActionResultStatus
{
    /// <summary>Anfrage erfolgreich an den EVE-Client abgesetzt (öffnet dort das Fenster).</summary>
    Success,

    /// <summary>Kein gültiger angemeldeter Charakter.</summary>
    NotAuthenticated,

    /// <summary>
    /// Scope <c>esi-ui.open_window.v1</c> ist nicht autorisiert. Die Analyse funktioniert
    /// ohne diesen Scope; erst nach vollständigem Logout und Login mit erweitertem Scope
    /// ist die Aktion verfügbar.
    /// </summary>
    MissingScope,

    /// <summary>ESI hat die Anfrage abgelehnt oder der Client war nicht erreichbar.</summary>
    Error
}

/// <summary>Ergebnis einer EVE-UI-Hilfsaktion mit deutscher Nutzermeldung.</summary>
public sealed record EsiUiActionResult(EsiUiActionResultStatus Status, string Message)
{
    public bool IsSuccess => Status == EsiUiActionResultStatus.Success;
}

/// <summary>
/// Freiwillige EVE-UI-Hilfsaktionen für Action Cards (Issue #66): öffnet Marktdetails
/// und setzt Wegpunkte ausschließlich über die erlaubten ESI-UI-Endpunkte
/// (POST /ui/openwindow/marketdetails/ und POST /ui/openwindow/waypoint/,
/// Scope esi-ui.open_window.v1). Die Aktionen sind ein reiner Seitenkanal der UI:
/// Sie verändern weder die Empfehlung noch die Karte. Fehlende Berechtigung und
/// Fehler werden als Ergebnis geliefert (nie geworfen, außer Cancellation), damit
/// die UI die Berechtigung/Folge sichtbar macht.
/// </summary>
public interface IEsiUiActionService
{
    /// <summary>
    /// Öffnet das Marktdetails-Fenster des EVE-Clients für das Item.
    /// Query-Parameter: <c>type_id</c>.
    /// </summary>
    Task<EsiUiActionResult> OpenMarketDetailsAsync(int typeId, CancellationToken ct = default);

    /// <summary>
    /// Setzt einen Wegpunkt im EVE-Client auf das Ziel (Station/Struktur/Solarsystem).
    /// Body: <c>destination_id</c>, <c>clear_other_waypoints</c>, <c>add_to_beginning=false</c>.
    /// </summary>
    Task<EsiUiActionResult> SetWaypointAsync(long destinationId, bool clearOtherWaypoints = true, CancellationToken ct = default);
}