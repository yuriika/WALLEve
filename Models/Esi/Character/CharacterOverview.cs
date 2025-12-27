using WALLEve.Models.Esi.Alliance;
using WALLEve.Models.Esi.Corporation;
using WALLEve.Models.Esi.Universe;

namespace WALLEve.Models.Esi.Character;

public class CharacterOverview
{
    public int CharacterId { get; set; }
    public EveCharacter Character { get; set; } = new();
    public EveCorporation Corporation { get; set; } = new();
    public EveAlliance? Alliance { get; set; }
    public double WalletBalance { get; set; }
    public CharacterLocation? Location { get; set; }
    public SolarSystem? CurrentSystem { get; set; }
    public CharacterShip? CurrentShip { get; set; }
    public EveType? ShipType { get; set; }
    public CharacterOnlineStatus? OnlineStatus { get; set; }

    public string PortraitUrl => $"https://images.evetech.net/characters/{CharacterId}/portrait?size=256";
    public string CorporationLogoUrl => $"https://images.evetech.net/corporations/{Character.CorporationId}/logo?size=128";
    public string? AllianceLogoUrl => Character.AllianceId.HasValue
        ? $"https://images.evetech.net/alliances/{Character.AllianceId}/logo?size=128"
        : null;
}
