namespace WALLEve.Models.Map;

/// <summary>
/// ========================================================================================
/// SYSTEM ACTIVITY MODEL - Live-Aktivitätsdaten für Solar Systems
/// ========================================================================================
///
/// ZWECK:
/// Dieses Model aggregiert Live-Aktivitätsdaten aus der EVE ESI API für ein einzelnes
/// Solar System. Die Daten werden für dynamisches Map-Rendering verwendet:
/// - Node-Größe basiert auf TotalActivity
/// - Border-Farbe/-Dicke basiert auf PvpActivity
///
/// DATENQUELLE:
/// Die Rohdaten stammen aus zwei ESI-Endpoints:
/// 1. GET /universe/system_kills/ - PvP und NPC Kills der letzten Stunde
/// 2. GET /universe/system_jumps/ - Anzahl Schiffs-Sprünge der letzten Stunde
///
/// Diese werden in MapDataService.GetSystemActivitiesAsync() kombiniert.
///
/// VERWENDUNG:
/// - In MapCanvas.razor: Wird als data-Attribute an Cytoscape Nodes übergeben
/// - In cytoscape-map.js: Dynamisches Styling basierend auf Activity-Werten
///
/// BEISPIEL-WERTE:
/// - Jita (Handels-Hub): TotalActivity ~5000, PvpActivity ~10 (viele Jumps, wenig PvP)
/// - Tama (PvP-System): TotalActivity ~200, PvpActivity ~50 (moderater Traffic, viel PvP)
/// - Stilles System: TotalActivity ~0, PvpActivity ~0
/// ========================================================================================
/// </summary>
public class SystemActivity
{
    /// <summary>
    /// Solar System ID (aus SDE, eindeutiger Identifier)
    /// Beispiel: 30000142 = Jita
    /// </summary>
    public int SystemId { get; set; }

    /// <summary>
    /// Anzahl Schiffs-Kills in der letzten Stunde (PvP-Indikator)
    ///
    /// QUELLE: ESI /universe/system_kills/ → ship_kills
    ///
    /// BEDEUTUNG:
    /// Zeigt an, wie viele Spieler-Schiffe in diesem System zerstört wurden.
    /// Hohe Werte deuten auf aktive PvP-Zonen hin (Gate-Camps, Kriege, etc.)
    ///
    /// VERWENDUNG:
    /// - Wird für TotalActivity addiert (Node-Größe)
    /// - Wird für PvpActivity addiert (Border-Farbe: Rot bei Ship Kills > 0)
    /// </summary>
    public int ShipKills { get; set; }

    /// <summary>
    /// Anzahl NPC-Kills in der letzten Stunde (PvE-Indikator)
    ///
    /// QUELLE: ESI /universe/system_kills/ → npc_kills
    ///
    /// BEDEUTUNG:
    /// Zeigt an, wie viele NPCs (Ratten) in diesem System zerstört wurden.
    /// Hohe Werte deuten auf aktive Ratting-Aktivität hin (PvE Farming).
    ///
    /// VERWENDUNG:
    /// - Wird für TotalActivity addiert (Node-Größe)
    /// - Wird NICHT für PvpActivity verwendet (ist kein PvP)
    /// </summary>
    public int NpcKills { get; set; }

    /// <summary>
    /// Anzahl Pod-Kills in der letzten Stunde (PvP-Indikator)
    ///
    /// QUELLE: ESI /universe/system_kills/ → pod_kills
    ///
    /// BEDEUTUNG:
    /// Zeigt an, wie viele Escape Pods (leere Schiffskapseln) zerstört wurden.
    /// Pods werden normalerweise nur in NullSec/LowSec zerstört → PvP-Intensität.
    ///
    /// VERWENDUNG:
    /// - Wird NICHT für TotalActivity addiert (weniger Gewicht als Schiffe)
    /// - Wird für PvpActivity addiert (Border-Dicke)
    /// </summary>
    public int PodKills { get; set; }

    /// <summary>
    /// Anzahl Schiffs-Sprünge in der letzten Stunde (Traffic-Indikator)
    ///
    /// QUELLE: ESI /universe/system_jumps/ → ship_jumps
    ///
    /// BEDEUTUNG:
    /// Zeigt an, wie viele Schiffe durch Stargates in dieses System gesprungen sind.
    /// Hohe Werte deuten auf Handels-Hubs, Durchgangs-Systeme oder Hotspots hin.
    ///
    /// VERWENDUNG:
    /// - Wird für TotalActivity addiert (Node-Größe)
    /// - Wird NICHT für PvpActivity verwendet (Jumps sind kein PvP)
    ///
    /// BEISPIEL-WERTE:
    /// - Jita: ~4000-5000 Jumps/Stunde (größter Handels-Hub)
    /// - Durchgangs-System: ~100-500 Jumps/Stunde
    /// - Deadend-System: ~0-10 Jumps/Stunde
    /// </summary>
    public int ShipJumps { get; set; }

    /// <summary>
    /// BERECHNETE PROPERTY: Gesamt-Aktivität (kombiniert Kills und Jumps)
    ///
    /// FORMEL: ShipKills + NpcKills + ShipJumps
    /// (Pod-Kills werden NICHT eingerechnet, um nicht zu übergewichten)
    ///
    /// VERWENDUNG IN MAP-RENDERING:
    /// In cytoscape-map.js wird die Node-Größe basierend auf dieser Zahl berechnet:
    ///
    /// Formel: width = Math.min(40, 18 + Math.log10(totalActivity + 1) * 5)
    ///
    /// BEISPIELE:
    /// - totalActivity = 0 → width = 18px (Basis-Größe)
    /// - totalActivity = 10 → width ≈ 23px
    /// - totalActivity = 100 → width ≈ 28px
    /// - totalActivity = 1000 → width ≈ 33px
    /// - totalActivity = 5000 → width ≈ 36px (Jita)
    /// - totalActivity > 10000 → width = 40px (Maximum)
    ///
    /// WARUM LOGARITHMISCH?
    /// Jita hat 5000+ Jumps, während die meisten Systeme 0-10 haben.
    /// Eine lineare Skalierung würde Jita riesig und alle anderen winzig machen.
    /// Logarithmisch: Unterschiede zwischen 0-100 sind größer als 1000-2000.
    /// </summary>
    public int TotalActivity => ShipKills + NpcKills + ShipJumps;

    /// <summary>
    /// BERECHNETE PROPERTY: PvP-Aktivität (nur Player-vs-Player Kills)
    ///
    /// FORMEL: ShipKills + PodKills
    ///
    /// VERWENDUNG IN MAP-RENDERING:
    /// In cytoscape-map.js bestimmt dieser Wert den roten Border:
    ///
    /// Formel (Border-Width): 1 + Math.min(3, Math.log10(pvpActivity + 1))
    /// Formel (Border-Color): pvpActivity > 0 ? '#ff0000' : '#888'
    ///
    /// BEISPIELE:
    /// - pvpActivity = 0 → kein Border (oder grau)
    /// - pvpActivity = 1-5 → 1-2px roter Border
    /// - pvpActivity = 10-50 → 2-3px roter Border (aktive PvP-Zone)
    /// - pvpActivity > 100 → 4px roter Border (sehr hot!)
    ///
    /// TYPISCHE PVP-SYSTEME:
    /// - Tama (LowSec Gate-Camp): pvpActivity ~20-50
    /// - Rancer (LowSec Choke-Point): pvpActivity ~10-30
    /// - Jita (HighSec, sicher): pvpActivity ~0-5
    /// </summary>
    public int PvpActivity => ShipKills + PodKills;
}
