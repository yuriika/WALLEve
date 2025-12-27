# WALL-EVE - an EVE Companion

Blazor-basierte Companion-App für EVE Online.

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

## .NET Version

Das Projekt ist für .NET 10 konfiguriert.

## Fehlerbehebung

Bei Token-Problemen: Lösche `~/.local/share/WALLEve/auth.dat`

## ESI-Dokumentation

Die offizielle EVE Online ESI-Dokumentation ist lokal verfügbar unter `.esi-docs/`.
Diese wird für API-Referenzen und Best Practices verwendet.

- **Quelle**: https://github.com/esi/esi-docs
- **Online-Version**: https://developers.eveonline.com/docs/
