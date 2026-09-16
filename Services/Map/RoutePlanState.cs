using WALLEve.Models.Map;
using WALLEve.Models.Risk;
using WALLEve.Services.Map.Interfaces;
using WALLEve.Services.Risk.Interfaces;

namespace WALLEve.Services.Map;

/// <summary>
/// Zustand des Routen-Flows auf der Karte (#72): hält Ursprung (Charakterposition
/// oder manuell), Ziel und Präferenz und delegiert die Berechnung an den gemeinsamen
/// Routenvertrag <see cref="IRouteCalculationService"/>. Das Ergebnis ist damit für
/// Karte (und künftig Filter/Rechnung) dieselbe Systemfolge + Security-Zusammenfassung.
///
/// Der Dienst ist zustandslos; dieser Zustand verhindert, dass eine veraltete Route
/// angezeigt wird: Ändert sich der effektive Ursprung (Charakterwechsel oder manuelle
/// Auswahl), das Ziel oder die Präferenz, wird die aktive Route verworfen. Jede neue
/// Berechnung verwendet ausschließlich den aktuellen effektiven Ursprung.
/// </summary>
public sealed class RoutePlanState
{
    /// <summary>Ursprung aus der aktuellen Charakterposition (Tracking).</summary>
    public int? CharacterOriginId { get; private set; }

    /// <summary>Manuell gewählter Ursprung.</summary>
    public int? ManualOriginId { get; private set; }

    /// <summary>Zielsystem der Route.</summary>
    public int? DestinationId { get; private set; }

    /// <summary>Routing-Präferenz (Kürzeste/Safer/LessSecure).</summary>
    public RoutingPreference Preference { get; private set; } = RoutingPreference.Safer;

    /// <summary>true = Charakterposition als Ursprung verwenden, sonst manuelle Auswahl.</summary>
    public bool UseCharacterOrigin { get; private set; } = true;

    /// <summary>Letztes berechnetes Routen-Ergebnis (gemeinsamer Vertrag).</summary>
    public RouteResult? ActiveRoute { get; private set; }

    /// <summary>
    /// Risikoevidenz zur aktiven Route (#73). Bleibt null, solange keine
    /// Erhebung lief; eine fehlgeschlagene Quelle ergibt konservativ
    /// <see cref="RiskLevel.Unknown"/>, nie eine „sichere“ Route.
    /// </summary>
    public RouteRiskSummary? RiskSummary { get; private set; }

    /// <summary>Validierungsfehler der letzten Anfrage (UI-Anzeige).</summary>
    public string? Error { get; private set; }

    public bool IsCalculating { get; private set; }

    /// <summary>Effektiver Ursprung gemäß aktueller Auswahl.</summary>
    public int? EffectiveOriginId => UseCharacterOrigin ? CharacterOriginId : ManualOriginId;

    /// <summary>True, wenn ein Ursprung für die Berechnung vorhanden ist.</summary>
    public bool HasOrigin => EffectiveOriginId.HasValue;

    private int? _routeOriginUsed;

    /// <summary>
    /// Aktualisiert die Charakterposition. Weicht sie vom zuletzt verwendeten
    /// Ursprung ab, wird die aktive Route verworfen (Ursprung invalidiert).
    /// </summary>
    public void UpdateCharacterOrigin(int? systemId)
    {
        if (CharacterOriginId == systemId)
        {
            return;
        }

        CharacterOriginId = systemId;
        InvalidateRouteIfOriginChanged();
    }

    /// <summary>Setzt den manuellen Ursprung; invalidiert die Route bei Änderung.</summary>
    public void SetManualOrigin(int? systemId)
    {
        if (ManualOriginId == systemId)
        {
            return;
        }

        ManualOriginId = systemId;
        InvalidateRouteIfOriginChanged();
    }

    /// <summary>Umschalten zwischen Charakterposition und manueller Auswahl.</summary>
    public void SetUseCharacterOrigin(bool useCharacter)
    {
        if (UseCharacterOrigin == useCharacter)
        {
            return;
        }

        UseCharacterOrigin = useCharacter;
        InvalidateRouteIfOriginChanged();
    }

    /// <summary>Setzt das Zielsystem; invalidiert die aktive Route.</summary>
    public void SetDestination(int? systemId)
    {
        if (DestinationId == systemId)
        {
            return;
        }

        DestinationId = systemId;
        InvalidateRoute();
    }

    /// <summary>Setzt die Routing-Präferenz; invalidiert die aktive Route.</summary>
    public void SetPreference(RoutingPreference preference)
    {
        if (Preference == preference)
        {
            return;
        }

        Preference = preference;
        InvalidateRoute();
    }

    /// <summary>
    /// Berechnet die Route über den gemeinsamen Routenvertrag mit dem aktuellen
    /// effektiven Ursprung. Validierungsfehler werden ohne Service-Aufruf gemeldet.
    /// Ist ein Risikodienst übergeben, wird die Evidenz zur Route nachgeladen
    /// (optional und blockiert die Navigation nie: Fehler ergeben Unknown).
    /// </summary>
    public async Task<RouteResult> CalculateAsync(
        IRouteCalculationService service,
        CancellationToken cancellationToken = default,
        IRouteRiskService? riskService = null)
    {
        var origin = EffectiveOriginId;
        if (!origin.HasValue)
        {
            Error = "Start-System wählen (Charakterposition oder manuell).";
            return new RouteResult { Success = false, Error = Error };
        }

        if (!DestinationId.HasValue)
        {
            Error = "Ziel-System wählen.";
            return new RouteResult { Success = false, Error = Error };
        }

        Error = null;
        RiskSummary = null;
        IsCalculating = true;
        try
        {
            var route = await service.CalculateRouteLocalAsync(
                origin.Value,
                DestinationId.Value,
                Preference);

            ActiveRoute = route;
            _routeOriginUsed = origin.Value;

            if (route.Success && route.Path?.Count > 0 && riskService != null)
            {
                try
                {
                    RiskSummary = await riskService.CollectRouteRiskAsync(route.Path, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                    // Risikoquelle darf die Navigation nie blockieren: konservativ unbekannt.
                    RiskSummary = new RouteRiskSummary { RouteLevel = RiskLevel.Unknown };
                }
            }

            return route;
        }
        finally
        {
            IsCalculating = false;
        }
    }

    private void InvalidateRouteIfOriginChanged()
    {
        if (EffectiveOriginId != _routeOriginUsed)
        {
            InvalidateRoute();
        }
    }

    private void InvalidateRoute()
    {
        if (ActiveRoute == null && Error == null)
        {
            return;
        }

        ActiveRoute = null;
        RiskSummary = null;
        Error = null;
    }
}