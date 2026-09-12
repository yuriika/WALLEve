using WALLEve.Models.Esi.Universe;
using WALLEve.Models.Holdings;
using WALLEve.Models.Sde;
using WALLEve.Services.Esi.Interfaces;
using WALLEve.Services.Holdings.Interfaces;
using WALLEve.Services.Sde.Interfaces;

namespace WALLEve.Services.Holdings;

/// <summary>
/// Orchestriert die Location-Auflösung eines Holding-Snapshots (#50):
/// NPC-Stationen und Sonnensysteme per SDE, zugängliche Strukturen per ESI
/// (character-abhängig). Container werden über die reine Kettenauflösung
/// (LocationChainResolver) verfolgt. Jeder Fehlerfall (403, not-in-sde,
/// Zyklus, fehlender Parent, unbekannte ID) endet in Unresolved mit Grund —
/// nie in einem Absturz oder einer erfundenen System-/Region-Zuordnung.
/// </summary>
public class HoldingsLocationResolver : IHoldingsLocationResolver
{
    private readonly ISdeUniverseService _sde;
    private readonly IEsiApiService _esi;
    private readonly ILogger<HoldingsLocationResolver> _logger;

    public HoldingsLocationResolver(
        ISdeUniverseService sde,
        IEsiApiService esi,
        ILogger<HoldingsLocationResolver> logger)
    {
        _sde = sde;
        _esi = esi;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ResolvedHoldingItem>> ResolveSnapshotAsync(
        IEnumerable<HoldingItem>? items,
        int characterId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var itemList = items?.ToList() ?? new List<HoldingItem>();

        // Bestands-Membership für Container-Erkennung. ItemIds sind nicht
        // global eindeutig (#35): bei Duplikaten gewinnt deterministisch die
        // erste Zeile; für die Kettenauflösung zählt nur die Mitgliedschaft.
        var itemsById = new Dictionary<long, HoldingItem>();
        foreach (var item in itemList)
        {
            itemsById.TryAdd(item.ItemId, item);
        }

        // 1) Distinkte Nicht-Container-Locations extern auflösen (SDE/ESI).
        //    Unbekannte ID-Bereiche bleiben bewusst AUSSEN vor — die reine
        //    Kettenauflösung meldet sie als missing-parent/unknown-location.
        var external = new Dictionary<long, LocationResolution>();
        foreach (var locationId in itemList.Select(i => i.LocationId).Distinct())
        {
            if (itemsById.ContainsKey(locationId))
                continue; // Container-Item — rein über die Kette auflösen.

            ct.ThrowIfCancellationRequested();

            switch (LocationChainResolver.Classify(locationId))
            {
                case LocationKind.SolarSystem:
                    var solar = await _sde.GetSolarSystemAsync((int)locationId);
                    if (solar == null)
                    {
                        external[locationId] = Unresolved(locationId, "not-in-sde");
                        break;
                    }
                    external[locationId] = new LocationResolution
                    {
                        LocationId = locationId,
                        Kind = LocationKind.SolarSystem,
                        Name = solar.Name,
                        SolarSystemId = solar.SolarSystemId,
                        RegionId = solar.RegionId,
                        RegionName = solar.RegionName
                    };
                    break;

                case LocationKind.Station:
                    var station = await _sde.GetStationAsync(locationId);
                    if (station == null)
                    {
                        external[locationId] = Unresolved(locationId, "not-in-sde");
                        break;
                    }
                    var stationSystem = await _sde.GetSolarSystemAsync(station.SolarSystemId);
                    external[locationId] = new LocationResolution
                    {
                        LocationId = locationId,
                        Kind = LocationKind.Station,
                        Name = station.Name,
                        SolarSystemId = station.SolarSystemId,
                        SolarSystemName = stationSystem?.Name,
                        RegionId = station.RegionId,
                        RegionName = stationSystem?.RegionName
                    };
                    break;

                case LocationKind.Structure:
                    var structureResult = await _esi.GetStructureAsync(locationId);
                    if (!structureResult.IsResolved)
                    {
                        // 403 / not-found / unauthenticated / unavailable → Unresolved mit Grund.
                        external[locationId] = Unresolved(locationId, structureResult.Error ?? "unavailable");
                        break;
                    }
                    var structure = structureResult.Structure!;
                    var structureSystem = await _sde.GetSolarSystemAsync(structure.SolarSystemId);
                    external[locationId] = new LocationResolution
                    {
                        LocationId = locationId,
                        Kind = LocationKind.Structure,
                        Name = structure.Name,
                        SolarSystemId = structure.SolarSystemId,
                        SolarSystemName = structureSystem?.Name,
                        RegionId = structureSystem?.RegionId,
                        RegionName = structureSystem?.RegionName
                    };
                    break;

                default:
                    // Unbekannter ID-Bereich: nicht raten. Die Kettenauflösung
                    // liefert je nach Kontext missing-parent oder unknown-location.
                    _logger.LogDebug("Location {LocationId} liegt außerhalb bekannter EVE-ID-Bereiche; bleibt unaufgelöst.", locationId);
                    break;
            }
        }

        // 2) Reine Kettenauflösung pro Item.
        var result = new List<ResolvedHoldingItem>(itemList.Count);
        foreach (var item in itemList)
        {
            ct.ThrowIfCancellationRequested();

            var chainResult = LocationChainResolver.ResolveChain(item, itemsById, external);
            result.Add(new ResolvedHoldingItem
            {
                Item = item,
                Location = chainResult.Chain.FirstOrDefault()
                           ?? Unresolved(item.LocationId, chainResult.UnresolvedReason ?? "unknown-location"),
                Chain = chainResult.Chain,
                Anchor = chainResult.Anchor
            });
        }

        return result;
    }

    private static LocationResolution Unresolved(long locationId, string reason)
        => new() { LocationId = locationId, Kind = LocationKind.Unresolved, UnresolvedReason = reason };
}