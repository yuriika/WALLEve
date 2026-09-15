using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WALLEve.Configuration;
using WALLEve.Services.Authentication.Interfaces;
using WALLEve.Services.Esi.Interfaces;

namespace WALLEve.Services.Esi;

/// <summary>
/// Ausführung der EVE-UI-Hilfsaktionen (Issue #66) über die erlaubten ESI-UI-Endpunkte.
/// Die Aktionen sind ein Seitenkanal (Side-Channel): Sie verändern weder Empfehlung noch
/// Karte. Berechtigungs- und Fehlerzustände werden als <see cref="EsiUiActionResult"/>
/// geliefert, nie geworfen (Ausnahme: Cancellation wird weitergegeben). Der erforderliche
/// Scope <c>esi-ui.open_window.v1</c> steckt bewusst NICHT in der Standard-Scope-Liste —
/// die Analyse arbeitet ohne EVE-UI-Scope; die UI zeigt die fehlende Berechtigung sichtbar.
/// </summary>
public sealed class EsiUiActionService : IEsiUiActionService
{
    /// <summary>Scope für alle EVE-UI-Endpunkte (POST /ui/openwindow/*, /ui/autopilot/*).</summary>
    public const string OpenWindowScope = "esi-ui.open_window.v1";

    private readonly EveOnlineSettings _settings;
    private readonly IEveAuthenticationService _authService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<EsiUiActionService> _logger;

    public EsiUiActionService(
        IOptions<EveOnlineSettings> settings,
        IEveAuthenticationService authService,
        IHttpClientFactory httpClientFactory,
        ILogger<EsiUiActionService> logger)
    {
        _settings = settings.Value;
        _authService = authService;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<EsiUiActionResult> OpenMarketDetailsAsync(int typeId, CancellationToken ct = default)
    {
        var preflight = await PreflightAsync("Marktdetails");
        if (preflight is not null)
            return preflight;

        try
        {
            var client = await CreateAuthenticatedClientAsync();
            var url = $"{_settings.EsiBaseUrl}/ui/openwindow/marketdetails/?type_id={typeId}";
            _logger.LogInformation("Opening market details for type {TypeId}", typeId);

            using var response = await client.PostAsync(url, null, ct);
            return ToResult(response, "Marktdetails im EVE-Client angefordert.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "UI action 'marketdetails' failed");
            return Failure(EsiUiActionResultStatus.Error, $"Marktdetails nicht ausführbar: {ex.Message}");
        }
    }

    public async Task<EsiUiActionResult> SetWaypointAsync(long destinationId, bool clearOtherWaypoints = true, CancellationToken ct = default)
    {
        var preflight = await PreflightAsync("Wegpunkt");
        if (preflight is not null)
            return preflight;

        try
        {
            var client = await CreateAuthenticatedClientAsync();
            var url = $"{_settings.EsiBaseUrl}/ui/openwindow/waypoint/";
            var body = JsonSerializer.Serialize(new
            {
                destination_id = destinationId,
                clear_other_waypoints = clearOtherWaypoints,
                add_to_beginning = false
            });
            _logger.LogInformation("Setting waypoint to destination {DestinationId} (clear={Clear})",
                destinationId, clearOtherWaypoints);

            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            var response = await client.SendAsync(request, ct);
            return ToResult(response, "Wegpunkt im EVE-Client gesetzt.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "UI action 'waypoint' failed");
            return Failure(EsiUiActionResultStatus.Error, $"Wegpunkt nicht ausführbar: {ex.Message}");
        }
    }

    /// <summary>
    /// Gemeinsame Vorprüfung: gültiger Charakter und autorisierter UI-Scope.
    /// Liefert <c>null</c>, wenn die Aktion ausgeführt werden darf.
    /// </summary>
    private async Task<EsiUiActionResult?> PreflightAsync(string actionLabel)
    {
        var auth = await _authService.GetAuthStateAsync();
        if (auth == null || !auth.IsValid)
        {
            return Failure(EsiUiActionResultStatus.NotAuthenticated,
                $"{actionLabel} benötigt einen angemeldeten Charakter.");
        }

        if (!auth.Scopes.Contains(OpenWindowScope, StringComparer.Ordinal))
        {
            return Failure(EsiUiActionResultStatus.MissingScope,
                $"Berechtigung fehlt: Scope „{OpenWindowScope}“ ist nicht autorisiert. " +
                $"Nach vollständigem Logout und Login mit erweitertem Scope verfügbar.");
        }

        return null;
    }

    /// <summary>Erstellt den „EveApi“-Client und setzt den Bearer-Header (asynchron).</summary>
    private async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var client = _httpClientFactory.CreateClient("EveApi");
        var token = await _authService.GetAccessTokenAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static EsiUiActionResult ToResult(HttpResponseMessage response, string successMessage)
    {
        if (response.IsSuccessStatusCode)
            return new EsiUiActionResult(EsiUiActionResultStatus.Success, successMessage);

        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            return Failure(EsiUiActionResultStatus.Error,
                "ESI hat die Aktion abgelehnt (403) — Berechtigung am Client nicht wirksam oder Client nicht verbunden.");
        }

        return Failure(EsiUiActionResultStatus.Error,
            $"ESI hat die Aktion abgelehnt ({(int)response.StatusCode}).");
    }

    private static EsiUiActionResult Failure(EsiUiActionResultStatus status, string message)
        => new(status, message);
}