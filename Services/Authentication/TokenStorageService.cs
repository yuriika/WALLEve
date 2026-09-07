using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using WALLEve.Configuration;
using WALLEve.Models.Authentication;
using WALLEve.Services.Authentication.Interfaces;

namespace WALLEve.Services.Authentication;

public class TokenStorageService : ITokenStorageService
{
    private readonly IDataProtector _protector;
    private readonly string _tokenFilePath;
    private readonly ILogger<TokenStorageService> _logger;
    private readonly Dictionary<string, PkceChallenge> _pkceChallenges = new();
    private readonly object _pkceLock = new();

    public TokenStorageService(
        IDataProtectionProvider dataProtectionProvider,
        IOptions<ApplicationSettings> appSettings,
        ILogger<TokenStorageService> logger)
    {
        var settings = appSettings.Value;
        _protector = dataProtectionProvider.CreateProtector($"{settings.Name}.Tokens");
        _logger = logger;

        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            settings.AppDataFolder);

        Directory.CreateDirectory(appDataPath);
        _tokenFilePath = Path.Combine(appDataPath, "auth.dat");

        _logger.LogInformation("Token storage path: {Path}", _tokenFilePath);
    }

    public async Task SaveAuthStateAsync(EveAuthState state)
    {
        try
        {
            var store = await LoadOrCreateAsync();
            store.Save(state);
            await WriteAsync(store);
            _logger.LogInformation("Auth state saved for character {CharacterName}", state.CharacterName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save auth state");
            throw;
        }
    }

    public async Task<EveAuthState?> GetAuthStateAsync()
    {
        var store = await TryLoadAsync();
        return store?.GetActive();
    }

    /// <summary>Alle gespeicherten Charaktere des Kontos (nur Id + Name, ohne Token).</summary>
    public async Task<List<KnownCharacter>> GetAllCharactersAsync()
    {
        var store = await TryLoadAsync();
        if (store == null) return new List<KnownCharacter>();

        return store.Characters.Values
            .Select(c => new KnownCharacter { CharacterId = c.CharacterId, CharacterName = c.CharacterName })
            .OrderByDescending(c => c.CharacterId == store.ActiveCharacterId) // aktiver zuerst
            .ThenBy(c => c.CharacterName)
            .ToList();
    }

    /// <summary>Setzt den aktiven Charakter und speichert (persistiert, kein Token-Tausch nötig).</summary>
    public async Task SetActiveCharacterAsync(int characterId)
    {
        var store = await TryLoadAsync();
        if (store == null || !store.Characters.ContainsKey(characterId.ToString()))
        {
            _logger.LogWarning("Cannot activate unknown character {CharacterId}", characterId);
            return;
        }

        store.ActiveCharacterId = characterId;
        await WriteAsync(store);
        _logger.LogInformation("Active character switched to {CharacterId}", characterId);
    }

    /// <summary>Entfernt einen Charakter (und dessen Tokens). Wenn er aktiv war, wird der nächste aktiv.</summary>
    public async Task<bool> RemoveCharacterAsync(int characterId)
    {
        var store = await TryLoadAsync();
        if (store == null || !store.Characters.ContainsKey(characterId.ToString())) return false;

        store.Remove(characterId);

        if (store.ActiveCharacterId == characterId)
        {
            var next = store.LastActive();
            store.ActiveCharacterId = next?.CharacterId; // null bei leerem Store
        }

        if (store.Characters.Count == 0)
        {
            await ClearAuthStateAsync();
        }
        else
        {
            await WriteAsync(store);
        }

        return true;
    }

    public async Task ClearAuthStateAsync()
    {
        try
        {
            if (File.Exists(_tokenFilePath))
            {
                File.Delete(_tokenFilePath);
                _logger.LogInformation("Auth state cleared");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear auth state");
        }

        await Task.CompletedTask;
    }

    public void StorePkceChallenge(PkceChallenge challenge)
    {
        lock (_pkceLock)
        {
            _pkceChallenges[challenge.State] = challenge;
            _logger.LogDebug("PKCE challenge stored for state {State}", challenge.State);
        }
    }

    public PkceChallenge? GetAndClearPkceChallenge(string state)
    {
        lock (_pkceLock)
        {
            if (_pkceChallenges.TryGetValue(state, out var challenge))
            {
                _pkceChallenges.Remove(state);
                _logger.LogDebug("PKCE challenge retrieved for state {State}", state);
                return challenge;
            }

            _logger.LogWarning("No PKCE challenge found for state {State}", state);
            return null;
        }
    }

    // ------------------------------------------------------------------
    // Datei-IO
    // ------------------------------------------------------------------

    private async Task<AuthStoreFile?> TryLoadAsync()
    {
        try
        {
            if (!File.Exists(_tokenFilePath)) return null;

            var encrypted = await File.ReadAllTextAsync(_tokenFilePath);
            var json = _protector.Unprotect(encrypted);
            var store = AuthStoreFile.Deserialize(json);

            // Altes v1-Format gefunden → direkt als v2 persistieren (Migration einmalig)
            if (store != null && !json.Contains("\"Characters\"", StringComparison.OrdinalIgnoreCase) && json.Contains("\"CharacterId\""))
            {
                await WriteAsync(store);
            }

            return store;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load auth state, clearing");
            await ClearAuthStateAsync();
            return null;
        }
    }

    private async Task<AuthStoreFile> LoadOrCreateAsync() => await TryLoadAsync() ?? new AuthStoreFile();

    private async Task WriteAsync(AuthStoreFile store)
    {
        var json = AuthStoreFile.Serialize(store);
        var encrypted = _protector.Protect(json);
        await File.WriteAllTextAsync(_tokenFilePath, encrypted);
    }
}