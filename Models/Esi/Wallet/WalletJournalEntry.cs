using System.Text.Json.Serialization;

namespace WALLEve.Models.Esi.Wallet;

public class WalletJournalEntry
{
    [JsonPropertyName("amount")]
    public double? Amount { get; set; }

    [JsonPropertyName("balance")]
    public double? Balance { get; set; }

    [JsonPropertyName("context_id")]
    public long? ContextId { get; set; }

    [JsonPropertyName("context_id_type")]
    public string? ContextIdType { get; set; }

    [JsonPropertyName("date")]
    public DateTime Date { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("first_party_id")]
    public int? FirstPartyId { get; set; }

    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("ref_type")]
    public string RefType { get; set; } = string.Empty;

    [JsonPropertyName("second_party_id")]
    public int? SecondPartyId { get; set; }

    [JsonPropertyName("tax")]
    public double? Tax { get; set; }

    [JsonPropertyName("tax_receiver_id")]
    public int? TaxReceiverId { get; set; }
}
