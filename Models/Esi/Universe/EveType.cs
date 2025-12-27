using System.Text.Json.Serialization;

namespace WALLEve.Models.Esi.Universe;

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
