namespace WALLEve.Models.Industry;

/// <summary>
/// Berechnete Endmenge eines Materials für einen kompletten Fertigungs-Job (#56):
/// ME-Waste + Rundung bereits auf die Gesamt-Runs angewendet.
/// </summary>
public sealed record MaterialRequirement(int MaterialTypeId, long RequiredQuantity);