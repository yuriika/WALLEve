using WALLEve.Models.Authentication;

namespace WALLEve.Services.Authentication.Interfaces;

public interface ITokenStorageService
{
    Task SaveAuthStateAsync(EveAuthState state);
    Task<EveAuthState?> GetAuthStateAsync();
    Task ClearAuthStateAsync();
    void StorePkceChallenge(PkceChallenge challenge);
    PkceChallenge? GetAndClearPkceChallenge(string state);
}
