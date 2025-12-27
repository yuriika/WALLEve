using System.Text.Json.Serialization;

namespace WALLEve.Models;

public class EveCharacter
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("birthday")]
    public DateTime Birthday { get; set; }
    
    [JsonPropertyName("gender")]
    public string Gender { get; set; } = string.Empty;
    
    [JsonPropertyName("race_id")]
    public int RaceId { get; set; }
    
    [JsonPropertyName("bloodline_id")]
    public int BloodlineId { get; set; }
    
    [JsonPropertyName("ancestry_id")]
    public int? AncestryId { get; set; }
    
    [JsonPropertyName("corporation_id")]
    public int CorporationId { get; set; }
    
    [JsonPropertyName("alliance_id")]
    public int? AllianceId { get; set; }
    
    [JsonPropertyName("security_status")]
    public float? SecurityStatus { get; set; }
    
    [JsonPropertyName("description")]
    public string? Description { get; set; }
    
    [JsonPropertyName("title")]
    public string? Title { get; set; }
}

public class EveCorporation
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("ticker")]
    public string Ticker { get; set; } = string.Empty;
    
    [JsonPropertyName("member_count")]
    public int MemberCount { get; set; }
    
    [JsonPropertyName("alliance_id")]
    public int? AllianceId { get; set; }
    
    [JsonPropertyName("ceo_id")]
    public int CeoId { get; set; }
    
    [JsonPropertyName("description")]
    public string? Description { get; set; }
    
    [JsonPropertyName("date_founded")]
    public DateTime? DateFounded { get; set; }
    
    [JsonPropertyName("tax_rate")]
    public float TaxRate { get; set; }
    
    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

public class EveAlliance
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("ticker")]
    public string Ticker { get; set; } = string.Empty;
    
    [JsonPropertyName("creator_id")]
    public int CreatorId { get; set; }
    
    [JsonPropertyName("creator_corporation_id")]
    public int CreatorCorporationId { get; set; }
    
    [JsonPropertyName("executor_corporation_id")]
    public int? ExecutorCorporationId { get; set; }
    
    [JsonPropertyName("date_founded")]
    public DateTime DateFounded { get; set; }
}

public class CharacterLocation
{
    [JsonPropertyName("solar_system_id")]
    public int SolarSystemId { get; set; }
    
    [JsonPropertyName("station_id")]
    public int? StationId { get; set; }
    
    [JsonPropertyName("structure_id")]
    public long? StructureId { get; set; }
}

public class CharacterOnlineStatus
{
    [JsonPropertyName("online")]
    public bool Online { get; set; }
    
    [JsonPropertyName("last_login")]
    public DateTime? LastLogin { get; set; }
    
    [JsonPropertyName("last_logout")]
    public DateTime? LastLogout { get; set; }
    
    [JsonPropertyName("logins")]
    public int? Logins { get; set; }
}

public class CharacterShip
{
    [JsonPropertyName("ship_item_id")]
    public long ShipItemId { get; set; }
    
    [JsonPropertyName("ship_name")]
    public string ShipName { get; set; } = string.Empty;
    
    [JsonPropertyName("ship_type_id")]
    public int ShipTypeId { get; set; }
}

public class SolarSystem
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("system_id")]
    public int SystemId { get; set; }
    
    [JsonPropertyName("constellation_id")]
    public int ConstellationId { get; set; }
    
    [JsonPropertyName("security_status")]
    public float SecurityStatus { get; set; }
}

public class EveType
{
    [JsonPropertyName("type_id")]
    public int TypeId { get; set; }
    
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("description")]
    public string? Description { get; set; }
    
    [JsonPropertyName("group_id")]
    public int GroupId { get; set; }
}

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

/// <summary>
/// Skill eines Charakters (von ESI)
/// </summary>
public class CharacterSkill
{
    [JsonPropertyName("skill_id")]
    public int SkillId { get; set; }

    [JsonPropertyName("trained_skill_level")]
    public int TrainedSkillLevel { get; set; }

    [JsonPropertyName("skillpoints_in_skill")]
    public long SkillPointsInSkill { get; set; }

    [JsonPropertyName("active_skill_level")]
    public int ActiveSkillLevel { get; set; }
}

/// <summary>
/// Skills-Response von ESI
/// </summary>
public class CharacterSkills
{
    [JsonPropertyName("skills")]
    public List<CharacterSkill> Skills { get; set; } = new();

    [JsonPropertyName("total_sp")]
    public long TotalSp { get; set; }

    [JsonPropertyName("unallocated_sp")]
    public int? UnallocatedSp { get; set; }
}
