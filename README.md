# WALL-EVE - an EVE Companion

Blazor-basierte Companion-App für EVE Online mit Wallet-Tracking, Transaktions-Analyse und Market-Integration.

## Features

- **Wallet Journal & Transactions**: Vollständige Übersicht über alle ISK-Bewegungen
- **Tax-Linking**: Automatische Verknüpfung von Steuern mit Market-Transaktionen
- **Market Orders**: Integration von aktiven und historischen Market Orders
- **Transaction Persistence**: Wallet-Links werden lokal gespeichert für schnellere Ladezeiten
- **SDE Integration**: Nutzt EVE's Static Data Export für Item-Namen, Locations, etc.
- **Multi-Character Ready**: Vorbereitet für Multi-Character Support

## Einrichtung

### 1. EVE Developer Application erstellen

1. Gehe zu [EVE Online Developers](https://developers.eveonline.com/)
2. Erstelle eine neue Application mit:
   - **Callback URL**: `http://localhost:5000/callback`
   - **Connection Type**: Authentication & API Access
   - **Scopes**: Wähle alle benötigten Scopes (siehe appsettings.json)

### 2. Client ID eintragen

Öffne `appsettings.json` und ersetze `YOUR_CLIENT_ID_HERE`:

```json
"ClientId": "deine-client-id-hier"
```

### 3. Starten

```bash
dotnet restore
dotnet run
```

Öffne http://localhost:5000 im Browser.

Unter Einstellungen kann die aktuelle Sde (Static Data Export) Datei als sqlite Version (von https://www.fuzzwork.co.uk/dump/) heruntergeladen werden.

## Datenbanken

Die App verwendet zwei separate SQLite-Datenbanken in `~/.local/share/WALLEve/Data/`:

- **`sde.sqlite`** - EVE Static Data Export (von extern, wird bei Updates ersetzt)
- **`wallet.db`** - App-eigene Daten (Wallet-Links, Character-Info, etc.)

Beide Dateinamen sind in `appsettings.json` konfigurierbar.

## .NET Version

Das Projekt ist für .NET 10 konfiguriert.

## Fehlerbehebung

- **Token-Probleme**: Lösche `~/.local/share/WALLEve/auth.dat`
- **Wallet-Links zurücksetzen**: Lösche `~/.local/share/WALLEve/Data/wallet.db`

## ESI-Dokumentation

Die offizielle EVE Online ESI-Dokumentation ist lokal verfügbar unter `.esi-docs/`.
Diese wird für API-Referenzen und Best Practices verwendet.

- **Quelle**: https://github.com/esi/esi-docs
- **Online-Version**: https://developers.eveonline.com/docs/
