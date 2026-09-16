using Microsoft.JSInterop;

namespace WALLEve.Services.Notifications;

/// <summary>
/// JS-Interop-Fassade für Browser-Notifications (Issue #70).
/// Lädt das Modul /js/trading-notifications.js lazy; jeder Aufruf ist in
/// try/catch gekapselt — ein Fehler schlägt nie auf die In-App-Meldungen durch.
/// </summary>
public sealed class BrowserNotificationService : IBrowserNotificationService
{
    private const string Unsupported = "unsupported";

    private readonly IJSRuntime _js;
    private IJSObjectReference? _module;
    private bool _loadFailed;

    public BrowserNotificationService(IJSRuntime js) => _js = js;

    public async Task<bool> IsSupportedAsync()
    {
        var module = await GetModuleAsync();
        if (module is null)
            return false;
        try
        {
            return await module.InvokeAsync<bool>("isSupported");
        }
        catch
        {
            return false;
        }
    }

    public async Task<string> GetPermissionAsync()
    {
        var module = await GetModuleAsync();
        if (module is null)
            return Unsupported;
        try
        {
            return await module.InvokeAsync<string>("getPermission");
        }
        catch
        {
            return Unsupported;
        }
    }

    public async Task<bool> RequestPermissionAsync()
    {
        var module = await GetModuleAsync();
        if (module is null)
            return false;
        try
        {
            return await module.InvokeAsync<bool>("requestPermission");
        }
        catch
        {
            return false;
        }
    }

    public async Task ShowAsync(string title, string body, string url)
    {
        var module = await GetModuleAsync();
        if (module is null)
            return;
        try
        {
            await module.InvokeVoidAsync("showNotification", title, body, url);
        }
        catch
        {
            // Seitenkanal: Fehler hier dürfen nie auf die In-App-Meldungen durchschlagen.
        }
    }

    private async Task<IJSObjectReference?> GetModuleAsync()
    {
        if (_module is not null)
            return _module;
        if (_loadFailed)
            return null;
        try
        {
            _module = await _js.InvokeAsync<IJSObjectReference>("import", "/js/trading-notifications.js");
            return _module;
        }
        catch
        {
            // Prerender ohne JS-Runtime oder nicht unterstützter Browser.
            _loadFailed = true;
            return null;
        }
    }
}