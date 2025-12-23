using System.Text.Json.Serialization;

namespace WALLEve.Models;

public class EveTokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;
    
    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = string.Empty;
    
    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
    
    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; set; } = string.Empty;
}

public class EveAuthState
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public int CharacterId { get; set; }
    public string CharacterName { get; set; } = string.Empty;
    public List<string> Scopes { get; set; } = new();
    
    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    public bool IsValid => !string.IsNullOrEmpty(AccessToken) && !string.IsNullOrEmpty(RefreshToken);
}

public class EveJwtPayload
{
    [JsonPropertyName("sub")]
    public string Subject { get; set; } = string.Empty;
    
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("owner")]
    public string Owner { get; set; } = string.Empty;
    
    [JsonPropertyName("exp")]
    public long Expiration { get; set; }
    
    [JsonPropertyName("iss")]
    public string Issuer { get; set; } = string.Empty;
    
    [JsonPropertyName("scp")]
    public object? Scopes { get; set; }
    
    public int GetCharacterId()
    {
        var parts = Subject.Split(':');
        if (parts.Length >= 3 && int.TryParse(parts[2], out var characterId))
        {
            return characterId;
        }
        return 0;
    }
    
    public List<string> GetScopes()
    {
        if (Scopes == null) return new List<string>();
        
        if (Scopes is string singleScope)
        {
            return new List<string> { singleScope };
        }
        
        if (Scopes is System.Text.Json.JsonElement element)
        {
            if (element.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                return element.EnumerateArray()
                    .Select(e => e.GetString() ?? string.Empty)
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();
            }
            if (element.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                return new List<string> { element.GetString() ?? string.Empty };
            }
        }
        
        return new List<string>();
    }
}

public class PkceChallenge
{
    public string CodeVerifier { get; set; } = string.Empty;
    public string CodeChallenge { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
}
