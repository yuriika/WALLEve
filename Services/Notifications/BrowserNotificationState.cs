namespace WALLEve.Services.Notifications;

/// <summary>
/// Pro-Circuit-Zustand des Browser-Benachrichtigungs-Opt-ins (Issue #70).
/// Bewusst als einfache, von JS und Komponenten entkoppelte Klasse, damit das
/// Akzeptanzkriterium „verweigerte/fehlende Berechtigung beeinträchtigt die
/// In-App-Meldungen nicht" deterministisch testbar ist: Der In-App-Feed
/// (<see cref="ITradingNotificationService"/>) kennt diesen Zustand nicht.
/// </summary>
public sealed class BrowserNotificationState
{
    public const string PermissionGranted = "granted";
    public const string PermissionDenied = "denied";
    public const string PermissionDefault = "default";
    public const string PermissionUnsupported = "unsupported";

    private string _permission = PermissionUnsupported;
    private bool _userOptedIn;
    private bool _supportChecked;

    /// <summary>true, sobald der Browser als unterstützend erkannt wurde.</summary>
    public bool IsSupported => _permission != PermissionUnsupported;

    /// <summary>Berechtigungsstring ("granted", "denied", "default", "unsupported").</summary>
    public string Permission => _permission;

    /// <summary>true, wenn der Nutzer Browser-Benachrichtigungen aktiviert hat UND die Berechtigung erteilt ist.</summary>
    public bool CanSendBrowserNotifications => IsSupported && _userOptedIn && _permission == PermissionGranted;

    /// <summary>Nutzer-Hinweis in der Zielgruppensprache (Deutsch), nie null.</summary>
    public string Hint { get; private set; } = "In-App-Meldungen sind immer aktiv. Browser-Benachrichtigungen sind optional.";

    /// <summary>Hat der Nutzer die Berechtigung bereits angefordert?</summary>
    public bool HasRequestedPermission { get; private set; }

    /// <summary>Ergebnis des letzten Berechtigungs-Checks (für Tests/Logging).</summary>
    public bool SupportChecked => _supportChecked;

    public void ApplySupport(bool supported)
    {
        _supportChecked = true;
        if (!supported)
        {
            _permission = PermissionUnsupported;
            _userOptedIn = false;
            Hint = "Dieser Browser unterstützt keine Benachrichtigungen — In-App-Meldungen bleiben aktiv.";
        }
    }

    public void ApplyPermission(string permission)
    {
        _permission = permission switch
        {
            PermissionGranted or PermissionDenied or PermissionDefault => permission,
            _ => PermissionUnsupported,
        };

        if (!IsSupported || _permission == PermissionDenied)
            _userOptedIn = false;

        Hint = _permission switch
        {
            PermissionUnsupported => "Dieser Browser unterstützt keine Benachrichtigungen — In-App-Meldungen bleiben aktiv.",
            PermissionDenied => "Browser-Berechtigung verweigert — In-App-Meldungen bleiben trotzdem aktiv.",
            _ => "In-App-Meldungen sind immer aktiv. Browser-Benachrichtigungen sind optional.",
        };
    }

    public void ApplyRequestResult(bool granted)
    {
        HasRequestedPermission = true;
        if (granted)
        {
            _userOptedIn = true;
            Hint = "Browser-Benachrichtigungen sind aktiv — In-App-Meldungen bleiben zusätzlich bestehen.";
        }
        else
        {
            _userOptedIn = false;
            Hint = "Browser-Berechtigung nicht erteilt — In-App-Meldungen bleiben trotzdem aktiv.";
        }
    }

    public void ToggleOptIn(bool enabled)
    {
        _userOptedIn = enabled;
        if (enabled && _permission != PermissionGranted)
            Hint = "Bitte erteile die Browser-Berechtigung, um Benachrichtigungen zu erhalten.";
        else if (enabled)
            Hint = "Browser-Benachrichtigungen sind aktiv — In-App-Meldungen bleiben zusätzlich bestehen.";
        else
            Hint = "Browser-Benachrichtigungen sind deaktiviert — In-App-Meldungen bleiben aktiv.";
    }
}