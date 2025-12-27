using WALLEve.Models.Authentication;

namespace WALLEve.Services.Authentication.Interfaces;

public interface IEveAuthenticationService
{
    Task<EveAuthState?> GetAuthStateAsync();
    Task<bool> IsAuthenticatedAsync();
    string GetLoginUrl();
    Task<bool> HandleCallbackAsync(string code, string state);
    Task<string?> GetAccessTokenAsync();
    Task LogoutAsync();
    event EventHandler<bool>? AuthenticationStateChanged;
}
