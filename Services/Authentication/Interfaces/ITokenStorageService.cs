using WALLEve.Models.Authentication;

namespace WALLEve.Services.Authentication.Interfaces;

public interface ITokenStorageService
{
    Task SaveAuthStateAsync(EveAuthState state);
    Task<EveAuthState?> GetAuthStateAsync();

    /// <summary>Alle gespeicherten Charaktere (Id + Name, ohne Token).</summary>
    Task<List<KnownCharacter>> GetAllCharactersAsync();

    /// <summary>Setzt den aktiven Charakter (kein Token-Tausch nötig).</summary>
    Task SetActiveCharacterAsync(int characterId);

    /// <summary>Entfernt einen Charakter; nächster wird aktiv, falls der aktive entfernt wurde.</summary>
    Task<bool> RemoveCharacterAsync(int characterId);

    Task ClearAuthStateAsync();
    void StorePkceChallenge(PkceChallenge challenge);
    PkceChallenge? GetAndClearPkceChallenge(string state);
}
