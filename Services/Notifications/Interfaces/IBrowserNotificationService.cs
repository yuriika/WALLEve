namespace WALLEve.Services.Notifications;

/// <summary>
/// Optionaler Seitenkanal für Browser-Benachrichtigungen (Issue #70).
/// Vertrag: Jeder Aufruf ist fehlertolerant — fehlende JS-Runtime (Prerender),
/// nicht unterstützter Browser oder verweigerte Berechtigung dürfen die
/// In-App-Meldungen nie beeinträchtigen. Es wird keine Systemzustellung bei
/// geschlossenem Browser behauptet; Hintergrundzustellung ist bewusst kein
/// Teil dieses Blazor-Slices.
/// </summary>
public interface IBrowserNotificationService
{
    /// <summary>true, wenn der Browser Browser-Notifications unterstützt (fehlertolerant).</summary>
    Task<bool> IsSupportedAsync();

    /// <summary>Berechtigung: "granted", "denied", "default" oder "unsupported".</summary>
    Task<string> GetPermissionAsync();

    /// <summary>Fordert die Berechtigung an; true, wenn danach "granted" ist.</summary>
    Task<bool> RequestPermissionAsync();

    /// <summary>Zeigt eine Browser-Benachrichtigung; ein Klick öffnet <paramref name="url"/>.</summary>
    Task ShowAsync(string title, string body, string url);
}