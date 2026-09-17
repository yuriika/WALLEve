namespace WALLEve.Models.Industry;

/// <summary>
/// Persistente Zeile eines Character-Blueprints (#49).
/// Additiv: pro (CharacterId, ItemId) existiert genau eine Zeile; eine erneute
/// Synchronisation ersetzt nur die mutablen Felder (Ort, Menge, ME/TE, Runs,
/// Kopierstatus) und löscht nie Zeilen — Blueprints, die ESI nicht mehr liefert
/// (verkauft/zerstört), bleiben als Historie erhalten, statt still zu gelten.
/// Die BPO/BPC-Semantik wird als Rohwerte gespiegelt: <see cref="Runs"/> = -1
/// ist der BPO-Sentinel, <see cref="IsBlueprintCopy"/> der boolesche Status.
/// Beide Felder werden nie auseinander abgeleitet (Akzeptanzkriterium #49).
/// </summary>
public class BlueprintEntry
{
    public long Id { get; set; }

    /// <summary>Owner: der Character, dem der Blueprint gehört.</summary>
    public int CharacterId { get; set; }

    /// <summary>ESI-Schlüssel: eindeutige Item-Id des Blueprints je Character.</summary>
    public long ItemId { get; set; }

    public int TypeId { get; set; }

    /// <summary>Lager-Ort des Blueprints (ESI station/structure/container_id).</summary>
    public long LocationId { get; set; }

    /// <summary>ESI location_flag als Rohstring (z. B. "Hangar", "CorpDeliveries").</summary>
    public string LocationFlag { get; set; } = string.Empty;

    /// <summary>ESI-Menge (quantity) — Rohwert, unverändert gespiegelt.</summary>
    public int Quantity { get; set; }

    /// <summary>Material Efficiency (ME) — Rohwert 0..10+ aus ESI.</summary>
    public int MaterialEfficiency { get; set; }

    /// <summary>Time Efficiency (TE) — Rohwert 0..20 aus ESI.</summary>
    public int TimeEfficiency { get; set; }

    /// <summary>Roh-Runs: -1 = BPO-Sentinel, &gt;= 0 = verbleibende BPC-Runs.</summary>
    public int Runs { get; set; }

    /// <summary>true = BPC (Kopie), false = BPO (Original).</summary>
    public bool IsBlueprintCopy { get; set; }

    public DateTime UpdatedAt { get; set; }
}