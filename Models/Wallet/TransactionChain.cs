namespace WALLEve.Models.Wallet;

/// <summary>
/// Vollständige Transaktionskette (z.B. Order erstellen → verkaufen → Gebühren)
/// </summary>
public class TransactionChain
{
    /// <summary>
    /// Wurzel der Kette (meist market_escrow beim Order-Erstellen)
    /// </summary>
    public WalletEntryViewModel? Root { get; set; }

    /// <summary>
    /// Haupttransaktion (market_transaction)
    /// </summary>
    public WalletEntryViewModel? Transaction { get; set; }

    /// <summary>
    /// Zugehörige Steuer (transaction_tax)
    /// </summary>
    public WalletEntryViewModel? Tax { get; set; }

    /// <summary>
    /// Escrow Release (market_escrow_release)
    /// </summary>
    public WalletEntryViewModel? EscrowRelease { get; set; }

    /// <summary>
    /// Broker Fee Modifikationen (brokers_fee - bei Preisänderungen)
    /// </summary>
    public List<WalletEntryViewModel> BrokerFeeModifications { get; set; } = new();

    /// <summary>
    /// Berechnet den Netto-Profit dieser Transaktion
    /// </summary>
    public decimal NetProfit
    {
        get
        {
            decimal total = 0;

            // Transaction Amount (Einnahme oder Ausgabe)
            if (Transaction != null)
                total += (decimal)Transaction.Amount;

            // Steuern abziehen
            if (Tax != null)
                total += (decimal)Tax.Amount; // Tax.Amount ist negativ

            // Escrow Release berücksichtigen (wenn negativ = verbraucht)
            if (EscrowRelease != null && EscrowRelease.Amount < 0)
                total += (decimal)EscrowRelease.Amount;

            // Broker Fee Modifikationen
            foreach (var fee in BrokerFeeModifications)
                total += (decimal)fee.Amount;

            return total;
        }
    }

    /// <summary>
    /// Brutto-Betrag der Transaktion
    /// </summary>
    public decimal GrossAmount => Transaction != null ? (decimal)Transaction.Amount : 0;

    /// <summary>
    /// Gesamt-Steuern (Sales Tax + Broker Fees)
    /// </summary>
    public decimal TotalTaxes
    {
        get
        {
            decimal total = 0;

            if (Tax != null)
                total += Math.Abs((decimal)Tax.Amount);

            if (EscrowRelease != null && EscrowRelease.Amount < 0)
                total += Math.Abs((decimal)EscrowRelease.Amount);

            foreach (var fee in BrokerFeeModifications)
                total += Math.Abs((decimal)fee.Amount);

            return total;
        }
    }

    /// <summary>
    /// Status der Transaktion
    /// </summary>
    public string Status
    {
        get
        {
            if (Transaction != null)
            {
                // Vollständig abgeschlossen
                if (Tax != null && EscrowRelease != null)
                    return "Abgeschlossen";

                // Teilweise verarbeitet
                if (Tax != null || EscrowRelease != null)
                    return "Teilweise";

                return "Transaktion erfasst";
            }

            if (Root != null)
            {
                // Order erstellt, aber noch nicht verkauft
                if (EscrowRelease != null && EscrowRelease.Amount > 0)
                    return "Storniert";

                return "Order aktiv";
            }

            return "Unvollständig";
        }
    }

    /// <summary>
    /// Gibt an, ob die Kette vollständig ist (alle erwarteten Einträge vorhanden)
    /// </summary>
    public bool IsComplete
    {
        get
        {
            // Eine vollständige Verkaufs-Kette hat:
            // Root (escrow) + Transaction + Tax + EscrowRelease
            return Root != null && Transaction != null && Tax != null && EscrowRelease != null;
        }
    }

    /// <summary>
    /// Gibt an, ob dies ein Verkauf (true) oder Kauf (false) war
    /// </summary>
    public bool? IsSell
    {
        get
        {
            if (Transaction?.TransactionDetails != null)
                return !Transaction.TransactionDetails.IsBuy;
            return null;
        }
    }

    /// <summary>
    /// Zeitstempel der Transaktion (frühester Eintrag)
    /// </summary>
    public DateTime Timestamp
    {
        get
        {
            var dates = new List<DateTime>();

            if (Root != null) dates.Add(Root.Date);
            if (Transaction != null) dates.Add(Transaction.Date);
            if (Tax != null) dates.Add(Tax.Date);
            if (EscrowRelease != null) dates.Add(EscrowRelease.Date);

            return dates.Any() ? dates.Min() : DateTime.MinValue;
        }
    }
}
