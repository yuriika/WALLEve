namespace WALLEve.Models.Industry;

/// <summary>Eine Materialbedarfs-Zeile innerhalb eines Plans (#62).</summary>
public readonly record struct MaterialDemandLine(int MaterialTypeId, long RequiredQuantity);

/// <summary>
/// Ein geplanter Fertigungsbedarf (Issue #62). Ein Plan ist ausdrücklich
/// <b>Planung</b>, keine Reservierung: Der physische Bestand wird nie still
/// abgezogen und nie für mehrere Pläne mehrfach vergeben. Der Abgleich mit
/// den Holdings teilt sich für jedes Material EINEN Bestandspool über alle
/// Pläne hinweg — dieselbe Menge kann nicht in zwei Plänen als verfügbar
/// erscheinen.
/// </summary>
public sealed record MaterialDemandRequest(string PlanName, IReadOnlyList<MaterialDemandLine> Materials);