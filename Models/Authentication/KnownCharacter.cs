namespace WALLEve.Models.Authentication;

/// <summary>Gespeicherter Charakter eines Kontos (ohne Token — nur für die Switcher-Liste).</summary>
public class KnownCharacter
{
    public int CharacterId { get; set; }
    public string CharacterName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}