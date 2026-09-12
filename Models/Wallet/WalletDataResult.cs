namespace WALLEve.Models.Wallet;

/// <summary>
/// Datenqualität der Wallet-Anzeige nach ESI-Abruf. Bewusst getrennt vom
/// Dateninhalt: Ein fehlgeschlagener Abruf ist kein „leeres Konto".
/// </summary>
public enum WalletDataStatus
{
    /// <summary>Alle ESI-Quellen erfolgreich geladen.</summary>
    Ok,

    /// <summary>Gültig leeres Ergebnis — es gibt tatsächlich keine Einträge.</summary>
    Empty,

    /// <summary>Mindestens eine ESI-Quelle fehlgeschlagen — Anzeige unvollständig.</summary>
    Failed
}

/// <summary>
/// Ergebnis des kombinierten Wallet-Abrufs: Einträge plus Datenqualität.
/// Bei <see cref="WalletDataStatus.Failed"/> bleiben die erfolgreich
/// geladenen Teile sichtbar, aber die UI muss den Fehler anzeigen statt
/// „keine Daten".
/// </summary>
public class WalletDataResult
{
    public List<WalletEntryViewModel> Entries { get; set; } = new();

    public WalletDataStatus Status { get; set; } = WalletDataStatus.Ok;

    /// <summary>Fehlerdetails (fehlgeschlagene Quellen) bei Failed.</summary>
    public string? StatusMessage { get; set; }
}