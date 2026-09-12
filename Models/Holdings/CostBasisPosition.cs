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
/// Gleitender Bestandsdurchschnitt als reine Zustandsmaschine (Issue #42).
///
/// Verwaltet pro (Character, Type) die Cost-Basis als expliziten Vertrag:
/// <list type="bullet">
/// <item><see cref="QuantityOnHand"/> — aktuell gehaltene Menge (belegt + unbekannt);</item>
/// <item><see cref="InventoryValue"/> — Bestandswert zu Anschaffungskosten NUR der belegten
/// Einheiten (unbekannte Einheiten tragen 0 bei und werden nicht erfunden);</item>
/// <item><see cref="AverageUnitCost"/> — laufender Durchschnitt pro belegter Einheit;</item>
/// <item><see cref="Source"/> — Herkunft der belegten Basis (Transaction/Manual/None);</item>
/// <item><see cref="Quality"/> — Anteil der gehaltenen Menge mit belegter Basis.</item>
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
/// <item>Mining/Loot/Production/Contract/Manual/Unknown sind unterschiedliche Provenienz:
/// Diese Maschine ordnet nie automatisch eine Basis zu — Basis entsteht nur durch explizite
/// Käufe (<see cref="ApplyBuy"/>) oder manuelle Deklaration (<see cref="ApplyManualEntry"/>).</item>
/// </list>
///
/// Reine, deterministische Klasse ohne I/O: alle Übergänge sind in-memory und testbar.
/// </summary>
public class CostBasisPosition
{
    private long _knownQuantity;
    private long _unknownQuantity;
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
    public long QuantityOnHand => _knownQuantity + _unknownQuantity;

    /// <summary>Gehaltene Menge mit belegter Basis (Kauf/Manual).</summary>
    public long KnownQuantity => _knownQuantity;

    /// <summary>Gehaltene Menge unbekannter Herkunft (Eröffnungsbestand/Transfer), getrennt geführt.</summary>
    public long UnknownQuantity => _unknownQuantity;

    /// <summary>Bestandswert zu Anschaffungskosten; unbekannte Einheiten tragen 0 bei (nicht erfunden).</summary>
    public double InventoryValue => _knownValue;

    /// <summary>Laufender Durchschnitt pro belegter Einheit; null ohne belegte Menge.</summary>
    public double? AverageUnitCost => _knownQuantity > 0 ? _knownValue / _knownQuantity : null;

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
            if (_unknownQuantity > 0 && _knownQuantity > 0) return CostBasisQuality.Partial;
            if (_unknownQuantity > 0) return CostBasisQuality.Missing;
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

    /// <summary>Eröffnungsbestand mit unbekannter Basis (z. B. Mining/Loot/Production/Contract/Bestand vor Trackingbeginn).</summary>
    public void ApplyOpeningBalance(long quantity)
    {
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity), "Menge muss positiv sein.");
        _unknownQuantity += quantity;
    }

    /// <summary>
    /// Transfer (Eingang oder Ausgang, z. B. zwischen eigenen Charakteren).
    /// Eingang: Gegenbuchung OHNE Kaufcharakter — wird NICHT als kostenloser Zugang (Preis 0)
    /// verbucht, sondern als unbekannte Menge getrennt geführt. Ausgang: konsumiert zuerst
    /// unbekannte, dann belegte Einheiten — erzeugt NIE realisierten Gewinn.
    /// </summary>
    public void ApplyTransfer(long quantity, bool isInbound)
    {
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity), "Menge muss positiv sein.");

        if (isInbound)
        {
            _unknownQuantity += quantity;
            return;
        }

        var remaining = quantity;

        // Zuerst unbekannte Einheiten (ohne P&L), dann belegte (Wert folgt der Ware, kein Gewinn).
        var consumedUnknown = Math.Min(remaining, _unknownQuantity);
        _unknownQuantity -= consumedUnknown;
        remaining -= consumedUnknown;

        var consumedKnown = Math.Min(remaining, _knownQuantity);
        if (consumedKnown > 0)
        {
            var avg = _knownValue / _knownQuantity;
            _knownValue -= avg * consumedKnown;
            _knownQuantity -= consumedKnown;
        }
        remaining -= consumedKnown;

        if (remaining > 0) WasOversold = true;
    }

    /// <summary>
    /// Marktkauf: erhöht Menge und Wert zum Kaufpreis (gewichteter neuer Durchschnitt).
    /// <paramref name="source"/> ist standardmäßig eine Markt-Transaktion; manuelle Käufe
    /// (Source=Manual) überstimmen den bisherigen Source.
    /// </summary>
    public void ApplyBuy(long quantity, double unitPrice, CostBasisSource source = CostBasisSource.Transaction)
    {
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity), "Menge muss positiv sein.");
        if (unitPrice < 0) throw new ArgumentOutOfRangeException(nameof(unitPrice), "Preis darf nicht negativ sein.");

        _knownQuantity += quantity;
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
    /// Gewinn wird für unbekannte Herkunft nie erfunden.
    /// </summary>
    public void ApplySell(long quantity, double unitPrice)
    {
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity), "Menge muss positiv sein.");
        if (unitPrice < 0) throw new ArgumentOutOfRangeException(nameof(unitPrice), "Preis darf nicht negativ sein.");

        var remaining = quantity;

        // Belegte Einheiten zuerst: Ausbuchung zum laufenden Durchschnitt.
        var consumedKnown = Math.Min(remaining, _knownQuantity);
        if (consumedKnown > 0)
        {
            var avg = _knownValue / _knownQuantity;
            RealizedProfit += (unitPrice - avg) * consumedKnown;
            _knownValue -= avg * consumedKnown;
            _knownQuantity -= consumedKnown;
        }
        remaining -= consumedKnown;

        // Danach unbekannte Einheiten: kein Gewinn, kein Wert (Basis fehlt).
        var consumedUnknown = Math.Min(remaining, _unknownQuantity);
        _unknownQuantity -= consumedUnknown;
        remaining -= consumedUnknown;

        if (remaining > 0) WasOversold = true;
    }

    /// <summary>
    /// Manuelle Deklaration der Basis für die aktuell unbekannte Menge (z. B. Eröffnungsbestand
    /// wird nachträglich deklariert): unbekannte Einheiten werden zu belegten Einheiten zum
    /// angegebenen Stückpreis. Source wird Manual — manuelle Werte gewinnen immer.
    /// </summary>
    public void ApplyManualEntry(double unitPrice)
    {
        if (unitPrice < 0) throw new ArgumentOutOfRangeException(nameof(unitPrice), "Preis darf nicht negativ sein.");
        if (_unknownQuantity == 0)
        {
            throw new InvalidOperationException(
                "Keine unbekannte Menge vorhanden, für die ein manueller Wert gesetzt werden könnte.");
        }

        _knownQuantity += _unknownQuantity;
        _knownValue += _unknownQuantity * unitPrice;
        _unknownQuantity = 0;
        Source = CostBasisSource.Manual;
    }
}