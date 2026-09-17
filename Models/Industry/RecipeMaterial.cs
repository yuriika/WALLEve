namespace WALLEve.Models.Industry;

/// <summary>
/// Ein einzelnes Rezept-Material (#56): Rohmenge pro Run laut SDE
/// (<c>industryActivityMaterials.quantity</c>), ohne ME-Abfall und ohne Rundung.
/// Die ME-abhängige Endmenge wird erst in <c>ManufacturingMath</c> berechnet.
/// </summary>
public sealed record RecipeMaterial(int MaterialTypeId, int BaseQuantity);