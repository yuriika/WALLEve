# WALL-EVE - EVE Online Companion App

**Eine umfassende Blazor-basierte Companion-Anwendung für EVE Online mit Wallet-Tracking, Transaktions-Analyse, Market-Integration und interaktiver Map-Visualisierung.**

---

## 📋 Inhalt / Table of Contents

- [Deutsch](#deutsch)
  - [Features](#features)
  - [Technologie-Stack](#technologie-stack)
  - [Einrichtung](#einrichtung)
  - [Map-Funktionen](#map-funktionen)
  - [Datenbanken](#datenbanken)
  - [Fehlerbehebung](#fehlerbehebung)
- [English](#english)
  - [Features](#features-1)
  - [Technology Stack](#technology-stack)
  - [Setup](#setup)
  - [Map Features](#map-features)
  - [Databases](#databases)
  - [Troubleshooting](#troubleshooting)

---

![img1.png](img1.png)

# Deutsch

## Features

### 💰 Wallet & Transactions
- **Wallet Journal & Transactions**: Vollständige Übersicht über alle ISK-Bewegungen
- **Tax-Linking**: Automatische Verknüpfung von Steuern mit Market-Transaktionen
- **Market Orders**: Integration von aktiven und historischen Market Orders
- **Transaction Persistence**: Wallet-Links werden lokal gespeichert für schnellere Ladezeiten

### 🗺️ Interaktive Map-Visualisierung
- **3 View-Modi**:
  - **Region View**: Alle Regionen des EVE Universe
  - **System View**: Detaillierte Ansicht einer einzelnen Region mit allen Systemen
  - **Local Environment**: Systeme innerhalb X Sprünge um die aktuelle Position

- **Live-Daten Integration**:
  - Dynamische Node-Größe basierend auf System-Aktivität (Kills + Jumps)
  - Roter Border für PvP-Aktivität (Ship Kills)
  - Echtzeit-Daten aus ESI (aktualisiert jede Stunde)

- **Erweiterte Visualisierung**:
  - **Constellation-Gruppierung**: Farbcodierte Verbindungen
    - 🟢 **Grün**: Intra-Constellation (eng verbundene Systeme)
    - 🔵 **Cyan**: Cross-Constellation (verschiedene Cluster, gleiche Region)
    - 🟠 **Orange (gestrichelt)**: Cross-Region (Region-Übergänge)
  - **Security Status**: Highsec (grün), Lowsec (gelb), Nullsec (rot)
  - **Character Tracking**: Live-Verfolgung der Charakter-Position
  - **Cross-Region Dummy-Nodes**: Virtuelle Nodes am Map-Rand für Region-Übergänge

### 🤖 AI-Trading (NEU!)
- **Ollama Integration**: Lokale LLM-Integration für intelligente Marktanalyse
- **Market Data Collection**: Automatische Sammlung von Marktdaten alle 5 Minuten
  - Tracked 5 Haupthandels-Hubs (Jita, Amarr, Dodixie, Rens, Hek)
  - Sammelt Daten für beliebte Items (PLEX, Skill Injectors, Mineralien)
  - Erfasst Stationsinformationen für Buy/Sell Orders
- **Trading Opportunities**: AI-basierte Erkennung von Trading-Chancen
  - Station Trading (Buy Low, Sell High in gleicher Region)
  - Spread-Analyse (automatische Erkennung von Profit-Margins)
  - Confidence-Scoring (60-95% basierend auf Marktdaten)
  - Anzeige von Stationsnamen über SDE-Integration
- **Market Snapshots**: Historische Preisdaten für Trend-Analyse
  - 7-Tage Retention für Performance
  - Beste Buy/Sell Preise, Volumen, Spreads
  - Location-Tracking für Best Orders
- **AI Dashboard**: `/trading` Route mit Live-Status und Opportunities
  - Sortierung nach Confidence, Profit oder Timestamp
  - Filterung nach Min. Confidence und Min. Profit
  - Fehlerbehandlung mit benutzerfreundlichen Meldungen

### 🔍 Market Finder (NEU!)
- **Interaktive Marktsuche**: Finde die besten Kauf- und Verkaufspreise für Items
  - Item-Auswahl mit Autocomplete (alle handelbaren Items aus SDE)
  - 3 Location-Modi: Aktuelle Position, Manuelles System, oder ganze Region
  - Jump-Distanz Filter (bei Position/System-Modus)
- **3-Stufige Hierarchie**: Übersichtliche Darstellung mit zuklappbaren Gruppierungen
  - **Ebene 1: Order-Typ** (Buy/Sell Orders getrennt)
  - **Ebene 2: Systeme** (sortiert nach Jump-Distanz)
  - **Ebene 3: Stationen** (mit jeweils beiden Order-Typen)
- **Smart Filtering**: Automatische Filterung nach Location und Jump-Reichweite
  - BFS-Algorithmus für präzise Jump-Berechnung
  - Echtzeit-Updates beim Ändern der Filter
- **Visuelle Hierarchie**: Klare Darstellung durch abgestufte Boxen und Farbcodierung
  - Grün für Buy Orders, Orange für Sell Orders
  - Jump-Distance Badges bei Position-Modus
  - Kompakte Order-Darstellung mit Preis und Volumen

### 🧾 Einkaufspreise / Cost Basis (NEU!)
- **Automatische Ermittlung echter Einkaufspreise** im Hintergrund (CostBasisCollectorService):
  - **Transaktions-Sink**: Spiegelt ESI-Wallet-Transaktionen täglich in eine lokale Tabelle
    (ESI liefert nur ~30 Tage zurück — durch die Spiegelung wächst die Historie dauerhaft)
  - **Ableitung**: Items mit Kauf-Transaktionen bekommen automatisch ihren echten
    Durchschnitts-Einkaufspreis (Source=Transaction), nach Marktwert priorisiert
  - Persistente Hintergrund-Jobs mit Fortschritt: unterbrochene Tasks werden nach
    App-Neustart automatisch an der abgebrochenen Stelle fortgesetzt
- **Übersichtsseite `/costbasis`**: Alle Bestands-Items mit Preis-Status
  - Sortierbar (Name, Menge, Marktwert, Sell-Preis, Status, Einkaufspreis)
  - Filter (offen / geschätzt / echt+manuell / alle) + Item-Suche
  - **Multiselektion zum Schätzen**: ausgewählte Items per Hintergrund-Job schätzen,
    Schätzmarkt pro Auswahl wählbar (Default: Jita, in den Einstellungen änderbar)
  - **Manueller Wert pro Item**: Preis + optionales Kaufdatum setzen (gewinnt immer)
  - Zurücksetzen einzelner Einträge (Item wieder "offen")
- **Schätzung als Fallback**: Keine Transaktion gefunden → Schätzung aus lokaler
  Markt-History (letzter Durchschnitt) bzw. ESI-Referenzpreis (Source=Estimate, als Vorschlag markiert)
- **Settings-Übersicht**: Alle Hintergrund-Tasks mit Status, Fortschritt, Fehler und
  Aktionen (Pause / Resume / Neu) + Einstellung des Standard-Schätzmarkts
- **Bestand-Tab**: Cost-Basis-Zeile zeigt die Quelle (Echt/Geschätzt/Manuell) mit
  Link zur Einkaufspreise-Verwaltung
- **ESI-Rate-Limit-Schutz**: Begrenzte Parallelität (max. 4) + Staffelung bei
  Transaktions-Seitenabrufen statt Request-Bursts; 429/420 werden mit Backoff behandelt

### 🔧 Weitere Features
- **SDE Integration**: Nutzt EVE's Static Data Export für Item-Namen, Locations, etc.
- **Multi-Character Vorbereitung**: Login/Logout ist vorhanden; echter Charakter-Switch ist noch nicht implementiert
- **ESI OAuth 2.0**: Sichere Authentifizierung via EVE SSO
- **ETag-Caching**: Effiziente ESI-Requests mit automatischem Caching
- **Automatisierte Tests**: xUnit-Testprojekt (`tests/WALLEve.Tests`, 24 Tests für
  Gebühren-/Gewinn-Logik, Hintergrund-Jobs und Cost-Basis-Fachlogik) — laufen
  vor jedem manuellen Test; Commits nur mit grünen Tests

## Technologie-Stack

- **Backend**: .NET 10, Blazor Server
- **Frontend**: Blazor (Razor Components), HTML5, CSS3
- **Map-Rendering**: Cytoscape.js v3.30.2 (JavaScript Graph Library)
- **AI**: Ollama (lokales LLM) - llama3.1:latest
- **Datenbanken**: SQLite (SDE + App-Daten)
- **API**: EVE ESI (EVE Swagger Interface)
- **Authentication**: OAuth 2.0 PKCE Flow

## Einrichtung

### 1. Voraussetzungen

- **.NET 10 SDK** (oder höher)
- **EVE Online Developer Account**
- **Ollama** (optional, für AI-Trading Features)

### 2. EVE Developer Application erstellen

1. Gehe zu [EVE Online Developers](https://developers.eveonline.com/)
2. Erstelle eine neue Application:
   - **Name**: WALL-EVE (oder beliebig)
   - **Callback URL**: `http://localhost:5080/callback`
   - **Connection Type**: Authentication & API Access
   - **Scopes**: Wähle folgende Scopes:
     ```
     esi-characters.read_standings.v1
     esi-skills.read_skills.v1
     esi-skills.read_skillqueue.v1
     esi-wallet.read_character_wallet.v1
     esi-wallet.read_corporation_wallets.v1
     esi-location.read_location.v1
     esi-location.read_online.v1
     esi-location.read_ship_type.v1
     esi-markets.read_character_orders.v1
     ```
3. Kopiere die **Client ID**

### 3. Client ID eintragen

Öffne `appsettings.json` und ersetze die leere Client ID:

```json
{
  "EveOnline": {
    "ClientId": "deine-client-id-hier",
    ...
  }
}
```

### 4. Projekt starten

```bash
# Dependencies installieren
dotnet restore

# Projekt kompilieren
dotnet build

# Projekt starten
dotnet run
```

Öffne **http://localhost:5080** im Browser.

> **Hinweis für neue Rechner / Erst-Setup:**
> - `~/.local/share/WALLEve/Data/wallet.db` wird **automatisch beim Start** über EF Core erstellt/aktualisiert.
> - Die SDE-Datei `sde.sqlite` wird **nicht automatisch** geladen; lade sie in der App unter **Einstellungen** herunter, falls keine lokale Kopie existiert.
> - `auth.dat` wird erst nach dem ersten Login angelegt.
> - Lokale ESI-Entwicklerdokumentation liegt unter `.esi-docs/`; bei Bedarf aktualisieren.

## Development-Initialisierung

Für Entwicklung auf einem neuen Rechner reichen diese Schritte:

```bash
# 1. Abhängigkeiten wiederherstellen und bauen
dotnet restore
dotnet build

# 2. App starten
dotnet run
```

### 5. SDE (Static Data Export) herunterladen

Die SDE wird **nicht automatisch heruntergeladen**. Wenn die Datei fehlt und `RequireSdeForStartup = true` ist, leitet die App beim Start auf die Seite **Einstellungen** weiter. Dort kannst du den Download manuell starten (~500 MB).

1. Gehe zu **Einstellungen** in der App
2. Klicke auf **"SDE herunterladen"**
3. Warte bis der Download abgeschlossen ist (~500 MB)

**Quelle**: https://www.fuzzwork.co.uk/dump/latest-sqlite.db.gz

### 6. Ollama für AI-Trading (Optional)

Für AI-gestützte Marktanalyse:

```bash
# Ollama installieren (Linux/Mac)
curl -fsSL https://ollama.com/install.sh | sh

# Ollama Model herunterladen
ollama pull llama3.1

# Ollama starten (läuft automatisch als Service)
# Verfügbar unter http://localhost:11434
```

**Test der Ollama-Integration**:
- Öffne http://localhost:5080/test/ollama
- Oder navigiere zu **AI Trading** in der App

**Windows**: Download von https://ollama.com/download

## Map-Funktionen

### Koordinaten-Transformation

Die Map nutzt EVE's X/Z-Koordinaten aus der SDE:

```
EVE Universe (3D) → 2D Map Projection
X (Meter) → X (Pixel)
Z (Meter) → Y (Pixel)
Y (ignoriert)
```

**Algorithmus**:
1. Bounding Box berechnen (Min/Max der Koordinaten)
2. Normalisierung auf 0-1 Bereich
3. Skalierung auf Canvas-Größe (z.B. 3000x2400px)
4. Padding hinzufügen (15% Rand)

### Dynamisches Node-Styling

**Node-Größe** (basierend auf `TotalActivity = ShipKills + NpcKills + ShipJumps`):
```javascript
width = Math.min(40, 18 + Math.log10(totalActivity + 1) * 5)
```

Beispiele:
- 0 Activity → 18px (Basis)
- 100 Activity → ~28px
- 1000 Activity → ~33px (Durchgangs-System)
- 5000 Activity → ~36px (Jita)

**PvP-Border** (basierend auf `PvpActivity = ShipKills + PodKills`):
```javascript
border-width = 1 + Math.min(3, Math.log10(pvpActivity + 1))
border-color = pvpActivity > 0 ? '#ff0000' : '#888'
```

## Datenbanken

Die App verwendet zwei separate SQLite-Datenbanken in `~/.local/share/WALLEve/Data/`:

### `sde.sqlite` (Static Data Export)
- **Größe**: ~500 MB
- **Quelle**: EVE SDE (Fuzzwork Dump)
- **Inhalt**:
  - `mapRegions`, `mapConstellations`, `mapSolarSystems`
  - `mapSolarSystemJumps` (Stargate-Verbindungen)
  - `invTypes` (Item-Namen, Typen)
  - `staStations` (NPC-Stationen)
- **Updates**: Kann in den Einstellungen aktualisiert werden

### `wallet.db` (App-Daten)
- **Größe**: Klein (wächst mit Nutzung)
- **Inhalt**:
  - `WalletEntryLinks` (Steuer-Transaktions-Verknüpfungen)
  - `CharacterInfo` (Charakter-Metadaten)
  - `MarketSnapshots` (Market-Momentaufnahmen, alle 5 Min)
    - Beste Buy/Sell Preise mit Location- und System-IDs
    - BestBuyLocationId, BestSellLocationId für Stations-Tracking
  - `MarketHistory` (Historische Marktdaten, täglich)
  - `TradingOpportunities` (AI-erkannte Trading-Chancen)
    - BuyLocationId, SellLocationId für Stations-Informationen
    - BuySystemId, SellSystemId für Map-Integration
  - `MarketTrends` (Preis-Trends und Analysen)
- **Migrations**: Entity Framework Core (automatisch beim Start)
- **Cleanup**: MarketSnapshots älter als 7 Tage werden automatisch gelöscht

## Fehlerbehebung

### Token-Probleme
```bash
rm ~/.local/share/WALLEve/auth.dat
```
→ Logout + erneutes Login erforderlich

### Wallet-Links zurücksetzen
```bash
rm ~/.local/share/WALLEve/Data/wallet.db
```
→ Alle manuellen Verknüpfungen gehen verloren

### Map lädt nicht
1. Überprüfe Browser-Konsole (F12) auf JavaScript-Fehler
2. Stelle sicher, dass SDE heruntergeladen wurde
3. Checke ob ESI erreichbar ist: https://esi.evetech.net/latest/status/

### Build-Fehler
```bash
# Clean & Rebuild
dotnet clean
dotnet build
```

## Projektstruktur

```
WALL-Eve/
├── Components/          # Blazor Components
│   ├── Map/            # Map-Visualisierung (MapCanvas, MapControls)
│   ├── Market/         # Market-Komponenten (MarketFinderView, MarketDataView)
│   └── Pages/          # Seiten (Index, Map, Wallet, Market, Trading, etc.)
├── Models/             # Data Models
│   ├── Map/            # Map-Models (MapConnection, SystemActivity)
│   ├── Esi/            # ESI Response Models
│   ├── AI/             # Ollama Request/Response Models
│   ├── Database/       # EF Core Entities (MarketSnapshot, TradingOpportunity)
│   └── Authentication/ # Auth Models
├── Services/           # Business Logic
│   ├── Map/            # Map-Services (MapDataService, RouteCalculation)
│   ├── Market/         # Market Analysis (MarketDataCollectorService, MarketDataService)
│   ├── AI/             # AI Services (OllamaService)
│   ├── Esi/            # ESI API Client
│   └── Sde/            # SDE Database Access
├── wwwroot/            # Static Assets
│   ├── js/             # JavaScript (cytoscape-map.js)
│   └── css/            # Stylesheets (wallet.css mit Market Finder Styling)
└── Data/               # Runtime Data (auth.dat, *.db)
```

## 📄 Lizenz

Dieses Projekt ist unter der **MIT License** lizenziert - siehe die [LICENSE](LICENSE) Datei für Details.

**Was bedeutet das?**
- ✅ Du kannst den Code frei verwenden, modifizieren und verteilen
- ✅ Du kannst ihn kommerziell nutzen
- ✅ Behalte einfach den Copyright-Hinweis und Lizenztext bei
- ❌ Keine Garantie oder Haftung

**Third-Party Lizenzen**: Dieses Projekt verwendet verschiedene Open-Source-Bibliotheken - siehe [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) für Details.

**EVE Online Haftungsausschluss**: EVE Online und alle zugehörigen Logos und Designs sind Eigentum von CCP hf. Dieses Projekt ist nicht mit CCP Games verbunden oder von ihnen unterstützt.

---

# English

## Features

### 💰 Wallet & Transactions
- **Wallet Journal & Transactions**: Complete overview of all ISK movements
- **Tax-Linking**: Automatic linking of taxes with market transactions
- **Market Orders**: Integration of active and historical market orders
- **Transaction Persistence**: Wallet links are stored locally for faster load times

### 🗺️ Interactive Map Visualization
- **3 View Modes**:
  - **Region View**: All regions of the EVE Universe
  - **System View**: Detailed view of a single region with all systems
  - **Local Environment**: Systems within X jumps of current location

- **Live Data Integration**:
  - Dynamic node size based on system activity (Kills + Jumps)
  - Red border for PvP activity (Ship Kills)
  - Real-time data from ESI (updated hourly)

- **Advanced Visualization**:
  - **Constellation Grouping**: Color-coded connections
    - 🟢 **Green**: Intra-Constellation (tightly connected systems)
    - 🔵 **Cyan**: Cross-Constellation (different clusters, same region)
    - 🟠 **Orange (dashed)**: Cross-Region (region transitions)
  - **Security Status**: Highsec (green), Lowsec (yellow), Nullsec (red)
  - **Character Tracking**: Live tracking of character position
  - **Cross-Region Dummy-Nodes**: Virtual nodes at map edge for region transitions

### 🤖 AI-Trading (NEW!)
- **Ollama Integration**: Local LLM integration for intelligent market analysis
- **Market Data Collection**: Automatic market data collection every 5 minutes
  - Tracks 5 major trade hubs (Jita, Amarr, Dodixie, Rens, Hek)
  - Collects data for popular items (PLEX, Skill Injectors, Minerals)
  - Captures station information for Buy/Sell orders
- **Trading Opportunities**: AI-based detection of trading opportunities
  - Station Trading (Buy Low, Sell High in same region)
  - Spread Analysis (automatic profit margin detection)
  - Confidence Scoring (60-95% based on market data)
  - Station name display via SDE integration
- **Market Snapshots**: Historical price data for trend analysis
  - 7-day retention for performance
  - Best Buy/Sell prices, Volume, Spreads
  - Location tracking for best orders
- **AI Dashboard**: `/trading` route with live status and opportunities
  - Sorting by Confidence, Profit, or Timestamp
  - Filtering by Min. Confidence and Min. Profit
  - Error handling with user-friendly messages

### 🔍 Market Finder (NEW!)
- **Interactive Market Search**: Find the best buy and sell prices for items
  - Item selection with autocomplete (all tradeable items from SDE)
  - 3 Location modes: Current position, Manual system, or entire region
  - Jump distance filter (for Position/System modes)
- **3-Level Hierarchy**: Clear presentation with collapsible groupings
  - **Level 1: Order Type** (Buy/Sell orders separated)
  - **Level 2: Systems** (sorted by jump distance)
  - **Level 3: Stations** (with both order types at each)
- **Smart Filtering**: Automatic filtering by location and jump range
  - BFS algorithm for precise jump calculation
  - Real-time updates when changing filters
- **Visual Hierarchy**: Clear display through graduated boxes and color coding
  - Green for Buy orders, Orange for Sell orders
  - Jump distance badges in Position mode
  - Compact order display with price and volume

### 🧾 Cost Basis / Purchase Prices (NEW!)
- **Automatic purchase-price detection in the background** (CostBasisCollectorService):
  - **Transaction Sink**: Mirrors ESI wallet transactions daily into a local table
    (ESI only serves ~30 days — the mirror grows the history permanently)
  - **Deduction**: Items with buy transactions automatically get their real average
    purchase price (Source=Transaction), prioritized by market value
  - Persisted background jobs with progress: interrupted jobs are automatically
    resumed at the breakpoint after an app restart
- **Overview page `/costbasis`**: All inventory items with price status
  - Sortable (name, quantity, market value, sell price, status, purchase price)
  - Filters (open / estimated / real+manual / all) + item search
  - **Multi-select estimation**: estimate selected items via background job,
    estimation market selectable per selection (default: Jita, changeable in settings)
  - **Manual value per item**: price + optional purchase date (always wins)
  - Reset individual entries (item becomes "open" again)
- **Estimation as fallback**: no transaction found → estimate from local market
  history (last average) or ESI reference price (Source=Estimate, marked as suggestion)
- **Settings overview**: all background jobs with status, progress, error and
  actions (Pause / Resume / Restart) + default estimation market setting
- **Inventory tab**: cost basis row shows the source (Real/Estimated/Manual) with
  link to the cost basis management
- **ESI rate-limit protection**: bounded parallelism (max 4) + staggering for
  transaction page fetches instead of request bursts; 429/420 handled with backoff

### 🔧 Additional Features
- **SDE Integration**: Uses EVE's Static Data Export for item names, locations, etc.
- **Multi-Character Ready**: Login/logout implemented; character switching is not yet available
- **ESI OAuth 2.0**: Secure authentication via EVE SSO
- **ETag-Caching**: Efficient ESI requests with automatic caching
- **Automated Tests**: xUnit test project (`WALLEve.Tests`) — run before manual
  testing; commits require green tests

## Technology Stack

- **Backend**: .NET 10, Blazor Server
- **Frontend**: Blazor (Razor Components), HTML5, CSS3
- **Map Rendering**: Cytoscape.js v3.30.2 (JavaScript Graph Library)
- **AI**: Ollama (local LLM) - llama3.1:latest
- **Databases**: SQLite (SDE + App Data)
- **API**: EVE ESI (EVE Swagger Interface)
- **Authentication**: OAuth 2.0 PKCE Flow

## Setup

### 1. Prerequisites

- **.NET 10 SDK** (or higher)
- **EVE Online Developer Account**
- **Ollama** (optional, for AI-Trading features)

### 2. Create EVE Developer Application

1. Go to [EVE Online Developers](https://developers.eveonline.com/)
2. Create a new Application:
   - **Name**: WALL-EVE (or custom)
   - **Callback URL**: `http://localhost:5080/callback`
   - **Connection Type**: Authentication & API Access
   - **Scopes**: Select the following scopes:
     ```
     esi-characters.read_standings.v1
     esi-skills.read_skills.v1
     esi-skills.read_skillqueue.v1
     esi-wallet.read_character_wallet.v1
     esi-wallet.read_corporation_wallets.v1
     esi-location.read_location.v1
     esi-location.read_online.v1
     esi-location.read_ship_type.v1
     esi-markets.read_character_orders.v1
     ```
3. Copy the **Client ID**

### 3. Enter Client ID

Open `appsettings.json` and replace the empty Client ID:

```json
{
  "EveOnline": {
    "ClientId": "your-client-id-here",
    ...
  }
}
```

### 4. Start Project

```bash
# Install dependencies
dotnet restore

# Build project
dotnet build

# Start project
dotnet run
```

- Open **http://localhost:5080** in your browser.

### 5. Download SDE (Static Data Export)

The SDE is **not downloaded automatically**. If the file is missing and `RequireSdeForStartup = true`, the app redirects to **Settings** on startup. There you can start the download manually (~500 MB).

1. Go to **Settings** in the app
2. Click **"Download SDE"**
3. Wait until download completes (~500 MB)

**Source**: https://www.fuzzwork.co.uk/dump/latest-sqlite.db.gz

### 6. Ollama for AI-Trading (Optional)

For AI-powered market analysis:

```bash
# Install Ollama (Linux/Mac)
curl -fsSL https://ollama.com/install.sh | sh

# Download Ollama model
ollama pull llama3.1

# Start Ollama (runs automatically as service)
# Available at http://localhost:11434
```

**Test Ollama Integration**:
- Open http://localhost:5080/test/ollama
- Or navigate to **AI Trading** in the app

**Windows**: Download from https://ollama.com/download

## Map Features

### Coordinate Transformation

The map uses EVE's X/Z coordinates from SDE:

```
EVE Universe (3D) → 2D Map Projection
X (Meters) → X (Pixels)
Z (Meters) → Y (Pixels)
Y (ignored)
```

**Algorithm**:
1. Calculate bounding box (Min/Max of coordinates)
2. Normalize to 0-1 range
3. Scale to canvas size (e.g., 3000x2400px)
4. Add padding (15% margin)

### Dynamic Node Styling

**Node Size** (based on `TotalActivity = ShipKills + NpcKills + ShipJumps`):
```javascript
width = Math.min(40, 18 + Math.log10(totalActivity + 1) * 5)
```

Examples:
- 0 Activity → 18px (base)
- 100 Activity → ~28px
- 1000 Activity → ~33px (transit system)
- 5000 Activity → ~36px (Jita)

**PvP Border** (based on `PvpActivity = ShipKills + PodKills`):
```javascript
border-width = 1 + Math.min(3, Math.log10(pvpActivity + 1))
border-color = pvpActivity > 0 ? '#ff0000' : '#888'
```

## Databases

The app uses two separate SQLite databases in `~/.local/share/WALLEve/Data/`:

### `sde.sqlite` (Static Data Export)
- **Size**: ~500 MB
- **Source**: EVE SDE (Fuzzwork Dump)
- **Contents**:
  - `mapRegions`, `mapConstellations`, `mapSolarSystems`
  - `mapSolarSystemJumps` (Stargate connections)
  - `invTypes` (Item names, types)
  - `staStations` (NPC Stations)
- **Updates**: Can be updated in Settings

### `wallet.db` (App Data)
- **Size**: Small (grows with usage)
- **Contents**:
  - `WalletEntryLinks` (Tax-Transaction associations)
  - `CharacterInfo` (Character metadata)
  - `MarketSnapshots` (Market snapshots, every 5 min)
    - Best Buy/Sell prices with location and system IDs
    - BestBuyLocationId, BestSellLocationId for station tracking
  - `MarketHistory` (Historical market data, daily)
  - `TradingOpportunities` (AI-detected trading opportunities)
    - BuyLocationId, SellLocationId for station information
    - BuySystemId, SellSystemId for map integration
  - `MarketTrends` (Price trends and analysis)
- **Migrations**: Entity Framework Core (automatic on startup)
- **Cleanup**: MarketSnapshots older than 7 days are automatically deleted

## Troubleshooting

### Token Issues
```bash
rm ~/.local/share/WALLEve/auth.dat
```
→ Logout + re-login required

### Reset Wallet Links
```bash
rm ~/.local/share/WALLEve/Data/wallet.db
```
→ All manual associations will be lost

### Map Not Loading
1. Check browser console (F12) for JavaScript errors
2. Ensure SDE has been downloaded
3. Verify ESI is reachable: https://esi.evetech.net/latest/status/

### Build Errors
```bash
# Clean & Rebuild
dotnet clean
dotnet build
```

## Project Structure

```
WALL-Eve/
├── Components/          # Blazor Components
│   ├── Map/            # Map Visualization (MapCanvas, MapControls)
│   ├── Market/         # Market Components (MarketFinderView, MarketDataView)
│   └── Pages/          # Pages (Index, Map, Wallet, Market, Trading, etc.)
├── Models/             # Data Models
│   ├── Map/            # Map Models (MapConnection, SystemActivity)
│   ├── Esi/            # ESI Response Models
│   ├── AI/             # Ollama Request/Response Models
│   ├── Database/       # EF Core Entities (MarketSnapshot, TradingOpportunity)
│   └── Authentication/ # Auth Models
├── Services/           # Business Logic
│   ├── Map/            # Map Services (MapDataService, RouteCalculation)
│   ├── Market/         # Market Analysis (MarketDataCollectorService, MarketDataService)
│   ├── AI/             # AI Services (OllamaService)
│   ├── Esi/            # ESI API Client
│   └── Sde/            # SDE Database Access
├── wwwroot/            # Static Assets
│   ├── js/             # JavaScript (cytoscape-map.js)
│   └── css/            # Stylesheets (wallet.css with Market Finder styling)
└── Data/               # Runtime Data (auth.dat, *.db)
```

---

## 📄 License

This project is licensed under the **MIT License** - see the [LICENSE](LICENSE) file for details.

**What does this mean?**
- ✅ You can use, modify, and distribute this code freely
- ✅ You can use it commercially
- ✅ Just keep the copyright notice and license text
- ❌ No warranty or liability

**Third-Party Licenses**: This project uses various open-source libraries - see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for details.

**EVE Online Disclaimer**: EVE Online and all associated logos and designs are the property of CCP hf. This project is not affiliated with or endorsed by CCP Games.

## 🔗 Links

- **EVE Online**: https://www.eveonline.com/
- **EVE Developers**: https://developers.eveonline.com/
- **ESI Documentation**: https://esi.evetech.net/ui/
- **Fuzzwork SDE**: https://www.fuzzwork.co.uk/dump/
- **Cytoscape.js**: https://js.cytoscape.org/
- **Ollama**: https://ollama.com/

## 🙏 Credits

- **CCP Games** - EVE Online, ESI API, SDE
- **Fuzzwork** - SQLite SDE Dumps
- **Cytoscape.js** - Graph visualization library
- **Ollama** - Local LLM runtime
