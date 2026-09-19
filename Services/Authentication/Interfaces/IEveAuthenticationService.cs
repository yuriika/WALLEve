using WALLEve.Models.Authentication;

namespace WALLEve.Services.Authentication.Interfaces;

public interface IEveAuthenticationService
{
    Task<EveAuthState?> GetAuthStateAsync();
    Task<bool> IsAuthenticatedAsync();
    string GetLoginUrl();
    Task<bool> HandleCallbackAsync(string code, string state);
    Task<string?> GetAccessTokenAsync();

    /// <summary>
    /// Erzwingt eine Erneuerung des Zugriffstokens über den gespeicherten
    /// Refresh-Token, unabhängig vom lokalen Ablaufzeitpunkt. Wird bei einem
    /// serverseitigen 401 (Token serverseitig abgelaufen/widerrufen) verwendet,
    /// damit die App sich selbst heilen kann. Liefert <c>false</c>, wenn kein
    /// Auth-Zustand vorliegt, der Refresh fehlschlägt oder das neue Token die
    /// JWKS-Validierung nicht besteht (Nutzer muss neu einloggen).
    /// </summary>
    Task<bool> ForceRefreshAccessTokenAsync();

    /// <summary>Meldet den AKTIVEN Charakter ab (revoke + entfernen). Andere bleiben gespeichert.</summary>
    Task LogoutAsync();

    /// <summary>Alle gespeicherten Charaktere des Kontos (für den Wechsel-Dialog).</summary>
    Task<List<KnownCharacter>> GetAllCharactersAsync();

    /// <summary>Wechselt zum gespeicherten Charakter, ohne erneute SSO-Anmeldung.</summary>
    Task<bool> SwitchCharacterAsync(int characterId);

    event EventHandler<bool>? AuthenticationStateChanged;
}
