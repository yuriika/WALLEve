namespace WALLEve.Models.Database;

/// <summary>
/// Persistiertes Hub-Profil für die Bewertung (Issue #58).
///
/// Speichert additiv Region, System und optionale Location eines Markthubs,
/// kennzeichnet den aktivierten Hub (IsActiveHub) und einen davon unabhängigen
/// Vergleichsmarkt (IsComparisonMarket). Beide Rollen können an derselben Zeile
/// stehen; die automatische Hub-Wahl berücksichtigt AUSSCHLIESSLICH
/// IsActiveHub — der Vergleichsmarkt hat darauf keinen Einfluss.
///
/// Bestehende Daten anderer Tabellen werden durch die additive Migration nicht
/// angetastet; Nutzer-Einstellungen (z. B. CostBasis.DefaultRegionId) bleiben
/// unverändert gültig.
/// </summary>
public class MarketHubProfile
{
    public int Id { get; set; }

    /// <summary>Anzeigename (z. B. „Jita").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Region des Hubs (SDE mapRegions).</summary>
    public int RegionId { get; set; }

    /// <summary>Solar-System des Hubs (SDE mapSolarSystems). Distanz-Bezugspunkt.</summary>
    public int SystemId { get; set; }

    /// <summary>Optionale Station-/Upwell-Location (NPC-Station oder Struktur).</summary>
    public long? LocationId { get; set; }

    /// <summary>
    /// Aktivierter Hub: nimmt an der automatischen Hub-Wahl über die exakte
    /// ungewichtete Sprungdistanz im SDE-Graph teil.
    /// </summary>
    public bool IsActiveHub { get; set; }

    /// <summary>
    /// Unabhängiger Vergleichsmarkt (Preisvergleich). Beeinflusst die
    /// automatische Hub-Wahl NICHT.
    /// </summary>
    public bool IsComparisonMarket { get; set; }

    public DateTime UpdatedAt { get; set; }
}