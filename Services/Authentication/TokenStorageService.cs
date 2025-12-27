using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
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
        ILogger<TokenStorageService> logger)
    {
        _protector = dataProtectionProvider.CreateProtector("WALLEve.Tokens");
        _logger = logger;
        
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WALLEve");
        
        Directory.CreateDirectory(appDataPath);
        _tokenFilePath = Path.Combine(appDataPath, "auth.dat");
        
        _logger.LogInformation("Token storage path: {Path}", _tokenFilePath);
    }

    public async Task SaveAuthStateAsync(EveAuthState state)
    {
        try
        {
            var json = JsonSerializer.Serialize(state);
            var encrypted = _protector.Protect(json);
            await File.WriteAllTextAsync(_tokenFilePath, encrypted);
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
        try
        {
            if (!File.Exists(_tokenFilePath))
            {
                _logger.LogDebug("No auth state file found");
                return null;
            }

            var encrypted = await File.ReadAllTextAsync(_tokenFilePath);
            var json = _protector.Unprotect(encrypted);
            var state = JsonSerializer.Deserialize<EveAuthState>(json);
            
            _logger.LogDebug("Auth state loaded for character {CharacterName}", state?.CharacterName);
            return state;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load auth state, clearing");
            await ClearAuthStateAsync();
            return null;
        }
    }

    public Task ClearAuthStateAsync()
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
        
        return Task.CompletedTask;
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
}
