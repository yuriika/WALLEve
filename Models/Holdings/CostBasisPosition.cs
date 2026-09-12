using WALLEve.Models.Database;

namespace WALLEve.Models.Holdings;

/// <summary>
/// Qualität einer Cost-Basis-Position: Wie vollständig ist die belegte Basis der
/// aktuell gehaltenen Menge? Unbekannte Einheiten (Eröffnungsbestand, Transfer,
/// fehlende Gegenbuchung) senken die Qualität — sie werden nie rückwirkend aus
/// Käufen geschätzt und nie als kostenloser Zugang (Preis 0) verbucht.
/// </summary>
public enum CostBasisQuality
{
    /// <summary>Keine belegte Basis: alle gehaltenen Einheiten unbekannter Herkunft.</summary>
    Missing = 0,

    /// <summary>Ein Teil der gehaltenen Menge hat eine belegte Basis, ein Teil nicht.</summary>
    Partial = 1,

    /// <summary>Jede gehaltene Einheit hat eine belegte Basis.</summary>
    Full = 2
}

/// <summary>
/// Herkunft einer Bestandseinheit (Provenienz). Unabhängig von der Cost-Basis-Herkunft
/// (<see cref="CostBasisSource"/>): <see cref="ItemProvenance"/> beschreibt, woher die
/// Einheit stammt (Mining, Loot, Production, Contract, Transfer, manuelle Erfassung,
/// Marktkauf oder unbekannt); <see cref="CostBasisSource"/> beschreibt, worauf eine
/// belegte Basis beruht (Transaction/Manual/None).
///
/// Die Maschine erfindet keine Zuordnung: Einheiten nicht-käuflicher Herkunft bleiben
/// als solche geführt und werden getrennt nach Provenienz nachverfolgt — sie werden
/// nie stillschweigend als Käufe interpretiert und nie aus Käufen rückwirkend geschätzt.
/// </summary>
public enum ItemProvenance
{
    /// <summary>Keine Herkunft angegeben oder bekannt (z. B. Eröffnungsbestand ohne Angabe).</summary>
    Unknown = 0,

    /// <summary>Abgebaut (Mining).</summary>
    Mining = 1,

    /// <summary>Gelootet (z. B. von NPCs oder Wracks).</summary>
    Loot = 2,

    /// <summary>Hergestellt (Production/Industry).</summary>
    Production = 3,

    /// <summary>Per Contract erhalten.</summary>
    Contract = 4,

    /// <summary>Manuell erfasst (Nutzerangabe, z. B. Eröffnungsbestand per Hand).</summary>
    Manual = 5,

    /// <summary>Transfer zwischen eigenen Charakteren.</summary>
    Transfer = 6,

    /// <summary>Marktkauf (Wallet-Transaktion).</summary>
    Transaction = 7
}

/// <summary>
/// Gleitender Bestandsdurchschnitt als reine Zustandsmaschine (Issue #42).
///
/// Verwaltet pro (Character, Type) die Cost-Basis als expliziten Vertrag:
/// <list type="bullet">
/// <item><see cref="QuantityOnHand"/> — aktuell gehaltene Menge (belegt + unbekannt);</item>
/// <item><see cref="InventoryValue"/> — Bestandswert zu Anschaffungskosten NUR der belegten
/// Einheiten (unbekannte Einheiten tragen 0 bei und werden nicht erfunden);</item>
/// <item><see cref="AverageUnitCost"/> — laufender Durchschnitt pro belegter Einheit;</item>
/// <item><see cref="Source"/> — Herkunft der belegten Basis (Transaction/Manual/None);</item>
/// <item><see cref="Quality"/> — Anteil der gehaltenen Menge mit belegter Basis;</item>
/// <item>Provenienz (<see cref="ItemProvenance"/>) — Herkunft jeder gehaltenen Einheit,
/// getrennt nach Mining/Loot/Production/Contract/Manual/Transfer/Transaction/Unknown
/// geführt (auditierbar über <see cref="QuantityByProvenance"/>).</item>
/// </list>
///
/// Regeln:
/// <list type="bullet">
/// <item>Käufe erhöhen Menge und Wert zum Kaufpreis (gewichteter neuer Durchschnitt).</item>
/// <item>Verkäufe buchen zum laufenden Durchschnitt aus; realisierter Gewinn entsteht nur
/// auf belegten Einheiten. Verkäufe konsumieren zuerst belegte Einheiten, dann unbekannte
/// (ohne P&amp;L), Rest wird bei 0 abgeschnitten (<see cref="WasOversold"/>).</item>
/// <item>Unbekannte Eröffnungsbestände und Transfers bleiben getrennt — sie werden NIEMALS
/// rückwirkend aus Käufen geschätzt. Ein Transfer-Eingang ist keine kostenlose Anschaffung,
/// ein Transfer-Ausgang erzeugt keinen Gewinn.</item>
/// <item>Mining/Loot/Production/Contract/Manual/Unknown bleiben unterschiedliche Provenienz:
/// Diese Maschine ordnet nie automatisch eine Basis zu — Basis entsteht nur durch explizite
/// Käufe (<see cref="ApplyBuy"/>) oder manuelle Deklaration (<see cref="ApplyManualEntry"/>).</item>
/// <item>Die Provenienz bleibt durch alle Übergänge erhalten: Einheiten, die per
/// <see cref="ApplyManualEntry"/> eine Basis erhalten, behalten ihre ursprüngliche
/// Provenienz; Transfer-Eingänge werden als <see cref="ItemProvenance.Transfer"/> geführt.</item>
/// </list>
///
/// Reine, deterministische Klasse ohne I/O: alle Übergänge sind in-memory und testbar.
/// </summary>
public class CostBasisPosition
{
    /// <summary>Anzahl der Provenienz-Werte (Array-Größe für die Buckets).</summary>
    private static readonly int ProvenanceCount = Enum.GetValues<ItemProvenance>().Length;

    private readonly long[] _knownByProvenance = new long[ProvenanceCount];
    private readonly long[] _unknownByProvenance = new long[ProvenanceCount];
    private double _knownValue;

    public CostBasisPosition(int characterId, int typeId)
    {
        CharacterId = characterId;
        TypeId = typeId;
        Source = CostBasisSource.None;
    }

    public int CharacterId { get; }

    public int TypeId { get; }

    /// <summary>Aktuell gehaltene Menge insgesamt (belegte + unbekannte Einheiten).</summary>
    public long QuantityOnHand => KnownQuantity + UnknownQuantity;

    /// <summary>Gehaltene Menge mit belegter Basis (Kauf/Manual).</summary>
    public long KnownQuantity
    {
        get
        {
            long sum = 0;
            foreach (var q in _knownByProvenance) sum += q;
            return sum;
        }
    }

    /// <summary>Gehaltene Menge unbekannter Herkunft, getrennt nach Provenienz geführt.</summary>
    public long UnknownQuantity
    {
        get
        {
            long sum = 0;
            foreach (var q in _unknownByProvenance) sum += q;
            return sum;
        }
    }

    /// <summary>Bestandswert zu Anschaffungskosten; unbekannte Einheiten tragen 0 bei (nicht erfunden).</summary>
    public double InventoryValue => _knownValue;

    /// <summary>Laufender Durchschnitt pro belegter Einheit; null ohne belegte Menge.</summary>
    public double? AverageUnitCost => KnownQuantity > 0 ? _knownValue / KnownQuantity : null;

    /// <summary>
    /// Herkunft der belegten Basis. Transaction = aus Markt-Transaktionen abgeleitet,
    /// Manual = manuell deklariert (überstimmt immer), None = keine belegte Basis vorhanden.
    /// Schätzwerte (Estimate) erzeugt diese Maschine nie — Schätzung ist ein separater,
    /// expliziter Pipeline-Schritt außerhalb des Ledgers.
    /// </summary>
    public CostBasisSource Source { get; private set; }

    /// <summary>Anteil der gehaltenen Menge mit belegter Basis (Missing/Partial/Full).</summary>
    public CostBasisQuality Quality
    {
        get
        {
            if (UnknownQuantity > 0 && KnownQuantity > 0) return CostBasisQuality.Partial;
            if (UnknownQuantity > 0) return CostBasisQuality.Missing;
            return CostBasisQuality.Full;
        }
    }

    /// <summary>Kumulierter realisierter Gewinn (positiv) bzw. Verlust (negativ) nur aus Verkäufen belegter Einheiten.</summary>
    public double RealizedProfit { get; private set; }

    /// <summary>
    /// true, wenn ein Verkauf mehr Einheiten konsumierte, als je belegt oder unbekannt
    /// geführt waren (fehlende Gegenbuchung) — Rest wurde bei 0 abgeschnitten, Menge wird
    /// nie negativ, und für den überschüssigen Teil wurde KEIN Gewinn erfunden.
    /// </summary>
    public bool WasOversold { get; private set; }

    /// <summary>Gesamte gehaltene Menge einer Provenienz (belegt + unbekannt).</summary>
    public long QuantityByProvenance(ItemProvenance provenance)
        => _knownByProvenance[(int)provenance] + _unknownByProvenance[(int)provenance];

    /// <summary>Gehaltene Menge einer Provenienz mit belegter Basis.</summary>
    public long KnownQuantityByProvenance(ItemProvenance provenance)
        => _knownByProvenance[(int)provenance];

    /// <summary>Gehaltene Menge einer Provenienz ohne belegte Basis.</summary>
    public long UnknownQuantityByProvenance(ItemProvenance provenance)
        => _unknownByProvenance[(int)provenance];

    /// <summary>
    /// Eröffnungsbestand mit explizit angegebener Herkunft (Mining/Loot/Production/Contract/
    /// Manual/Unknown/Transaction). Einheiten bleiben ohne Basis und werden getrennt nach
    /// Provenienz geführt — es entsteht KEINE erfundene Zuordnung zu Käufen.
    /// </summary>
    public void ApplyOpeningBalance(long quantity, ItemProvenance provenance = ItemProvenance.Unknown)
    {
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity), "Menge muss positiv sein.");
        if ((int)provenance < 0 || (int)provenance >= ProvenanceCount)
            throw new ArgumentOutOfRangeException(nameof(provenance), "Unbekannte Provenienz.");
        _unknownByProvenance[(int)provenance] += quantity;
    }

    /// <summary>
    /// Transfer (Eingang oder Ausgang, z. B. zwischen eigenen Charakteren).
    /// Eingang: Gegenbuchung OHNE Kaufcharakter — wird NICHT als kostenloser Zugang (Preis 0)
    /// verbucht, sondern als unbekannte Menge der Provenienz Transfer getrennt geführt.
    /// Ausgang: konsumiert zuerst unbekannte, dann belegte Einheiten — erzeugt NIE
    /// realisierten Gewinn. Konsumreihenfolge innerhalb eines Pools: aufsteigende
    /// Enum-Reihenfolge der Provenienz (Unknown zuerst).
    /// </summary>
    public void ApplyTransfer(long quantity, bool isInbound)
    {
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity), "Menge muss positiv sein.");

        if (isInbound)
        {
            _unknownByProvenance[(int)ItemProvenance.Transfer] += quantity;
            return;
        }

        var remaining = quantity;

        // Zuerst unbekannte Einheiten (ohne P&L), dann belegte (Wert folgt der Ware, kein Gewinn).
        ConsumeProvenanceBuckets(_unknownByProvenance, ref remaining);

        if (remaining > 0)
        {
            var consumedKnown = Math.Min(remaining, KnownQuantity);
            if (consumedKnown > 0)
            {
                var avg = _knownValue / KnownQuantity;
                _knownValue -= avg * consumedKnown;
                ConsumeProvenanceBuckets(_knownByProvenance, ref remaining);
            }
        }

        if (remaining > 0) WasOversold = true;
    }

    /// <summary>
    /// Marktkauf: erhöht Menge und Wert zum Kaufpreis (gewichteter neuer Durchschnitt).
    /// Gekaufte Einheiten werden der Provenienz <see cref="ItemProvenance.Transaction"/>
    /// zugeordnet. <paramref name="source"/> ist standardmäßig eine Markt-Transaktion;
    /// manuelle Käufe (Source=Manual) überstimmen den bisherigen Source.
    /// </summary>
    public void ApplyBuy(long quantity, double unitPrice, CostBasisSource source = CostBasisSource.Transaction)
    {
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity), "Menge muss positiv sein.");
        if (unitPrice < 0) throw new ArgumentOutOfRangeException(nameof(unitPrice), "Preis darf nicht negativ sein.");

        _knownByProvenance[(int)ItemProvenance.Transaction] += quantity;
        _knownValue += quantity * unitPrice;

        if (Source != CostBasisSource.Manual || source == CostBasisSource.Manual)
        {
            Source = source;
        }
    }

    /// <summary>
    /// Marktverkauf: bucht zum laufenden Durchschnitt der belegten Einheiten aus.
    /// Realisierter Gewinn entsteht NUR auf belegten Einheiten; konsumiert danach unbekannte
    /// Einheiten (ohne P&amp;L). Überschuss (fehlende Basis) wird bei 0 abgeschnitten —
    /// Gewinn wird für unbekannte Herkunft nie erfunden. Konsumreihenfolge innerhalb eines
    /// Pools: aufsteigende Enum-Reihenfolge der Provenienz (Unknown zuerst).
    /// </summary>
    public void ApplySell(long quantity, double unitPrice)
    {
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity), "Menge muss positiv sein.");
        if (unitPrice < 0) throw new ArgumentOutOfRangeException(nameof(unitPrice), "Preis darf nicht negativ sein.");

        var remaining = quantity;

        // Belegte Einheiten zuerst: Ausbuchung zum laufenden Durchschnitt.
        var consumedKnown = Math.Min(remaining, KnownQuantity);
        if (consumedKnown > 0)
        {
            var avg = _knownValue / KnownQuantity;
            RealizedProfit += (unitPrice - avg) * consumedKnown;
            _knownValue -= avg * consumedKnown;
            ConsumeProvenanceBuckets(_knownByProvenance, ref remaining);
        }

        // Danach unbekannte Einheiten: kein Gewinn, kein Wert (Basis fehlt).
        ConsumeProvenanceBuckets(_unknownByProvenance, ref remaining);

        if (remaining > 0) WasOversold = true;
    }

    /// <summary>
    /// Manuelle Deklaration der Basis für die aktuell unbekannte Menge (z. B. Eröffnungsbestand
    /// wird nachträglich deklariert): unbekannte Einheiten werden zu belegten Einheiten zum
    /// angegebenen Stückpreis. Source wird Manual — manuelle Werte gewinnen immer.
    /// Die Provenienz der Einheiten bleibt dabei erhalten (auditierbar).
    /// </summary>
    public void ApplyManualEntry(double unitPrice)
    {
        if (unitPrice < 0) throw new ArgumentOutOfRangeException(nameof(unitPrice), "Preis darf nicht negativ sein.");
        if (UnknownQuantity == 0)
        {
            throw new InvalidOperationException(
                "Keine unbekannte Menge vorhanden, für die ein manueller Wert gesetzt werden könnte.");
        }

        var converted = 0L;
        for (var i = 0; i < _unknownByProvenance.Length; i++)
        {
            _knownByProvenance[i] += _unknownByProvenance[i];
            converted += _unknownByProvenance[i];
            _unknownByProvenance[i] = 0;
        }

        _knownValue += converted * unitPrice;
        Source = CostBasisSource.Manual;
    }

    /// <summary>
    /// Konsumiert <paramref name="remaining"/> Einheiten aus den Provenienz-Buckets in
    /// aufsteigender Enum-Reihenfolge (Unknown zuerst) und reduziert <paramref name="remaining"/>
    /// um die tatsächlich konsumierte Menge.
    /// </summary>
    private static void ConsumeProvenanceBuckets(long[] buckets, ref long remaining)
    {
        for (var i = 0; i < buckets.Length && remaining > 0; i++)
        {
            var consumed = Math.Min(remaining, buckets[i]);
            buckets[i] -= consumed;
            remaining -= consumed;
        }
    }
}