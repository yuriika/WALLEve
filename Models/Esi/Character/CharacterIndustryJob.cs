using System.Text.Json.Serialization;

namespace WALLEve.Models.Esi.Character;

/// <summary>
/// Ein Character-Industriejob (#40).
/// GET /characters/{character_id}/industry/jobs/ — Scope: esi-industry.read_character_jobs.v1.
/// ESI liefert aktive sowie die zuletzt ~90 Tage abgeschlossenen Jobs; der
/// eindeutige Schlüssel eines Jobs ist seine job_id je Character. Zeitangaben
/// sind RFC3339-Date-Times und werden in der Persistenzschicht nach UTC
/// normalisiert. Optionale Felder (product_type_id, completed_date, cost …)
/// fehlen bei Jobs ohne Produkt bzw. vor Abschluss.
/// </summary>
public class CharacterIndustryJob
{
    [JsonPropertyName("activity_id")]
    public int ActivityId { get; set; }

    [JsonPropertyName("blueprint_id")]
    public long BlueprintId { get; set; }

    [JsonPropertyName("blueprint_location_id")]
    public long BlueprintLocationId { get; set; }

    [JsonPropertyName("blueprint_type_id")]
    public int BlueprintTypeId { get; set; }

    [JsonPropertyName("completed_character_id")]
    public int? CompletedCharacterId { get; set; }

    [JsonPropertyName("completed_date")]
    public string? CompletedDate { get; set; }

    [JsonPropertyName("cost")]
    public double? Cost { get; set; }

    [JsonPropertyName("duration")]
    public int Duration { get; set; }

    [JsonPropertyName("end_date")]
    public string EndDate { get; set; } = string.Empty;

    [JsonPropertyName("facility_id")]
    public long FacilityId { get; set; }

    [JsonPropertyName("installer_id")]
    public int InstallerId { get; set; }

    [JsonPropertyName("job_id")]
    public int JobId { get; set; }

    [JsonPropertyName("licensed_runs")]
    public int? LicensedRuns { get; set; }

    [JsonPropertyName("output_location_id")]
    public long OutputLocationId { get; set; }

    [JsonPropertyName("pause_date")]
    public string? PauseDate { get; set; }

    [JsonPropertyName("probability")]
    public double? Probability { get; set; }

    [JsonPropertyName("product_type_id")]
    public int? ProductTypeId { get; set; }

    [JsonPropertyName("runs")]
    public int Runs { get; set; }

    [JsonPropertyName("start_date")]
    public string StartDate { get; set; } = string.Empty;

    [JsonPropertyName("station_id")]
    public long StationId { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("successful_runs")]
    public int? SuccessfulRuns { get; set; }
}