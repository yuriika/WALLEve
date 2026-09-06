# WALL-EVE Development Guide

> **Commit-Regel:** Vor jedem Commit laufen die Tests (`dotnet test` im Ordner
> `tests/WALLEve.Tests`) — **nur Commits mit grünen Tests**. Vor einem manuellen
> Test durch den Nutzer werden die Tests ebenfalls zuerst ausgeführt.

> **AI Assistant Note**: This document provides a quick overview of the project architecture and current state. Read this first in new sessions to understand the codebase without needing to explore from scratch.

## Project Overview

**WALL-EVE** is a Blazor Server-based companion app for EVE Online that tracks wallets, analyzes transactions, integrates market data, and provides **interactive map visualization**. Built with .NET 10, Entity Framework Core, SQLite, and Cytoscape.js.

### Key Technologies
- **Framework**: .NET 10, Blazor Server (InteractiveServer render mode)
- **UI**: Razor Components, Bootstrap-based
- **Database**: SQLite (Entity Framework Core)
- **HTTP**: IHttpClientFactory with named clients
- **Authentication**: EVE Online OAuth 2.0 (PKCE flow)
- **External APIs**: EVE Online ESI (EVE Swagger Interface)
- **Map Visualization**: Cytoscape.js v3.30.2
- **AI**: Ollama (Local LLM) - llama3.1:latest ⭐ NEW

### Project Purpose
- Track ISK movements across wallet journal and transactions
- Automatically link market transactions with taxes and escrow entries
- Display market orders (active and historical)
- **Interactive map with live character tracking and system statistics** ⭐
- **AI-powered market analysis using local Ollama** ⭐ NEW - IMPLEMENTED!

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────┐
│ Presentation Layer (Blazor Server)                          │
├─────────────────────────────────────────────────────────────┤
│  Components/Pages/                                          │
│  ├── Home.razor           - Landing page                    │
│  ├── Character.razor      - Character overview              │
│  ├── Wallet.razor         - Wallet journal & transactions   │
│  ├── Market.razor         - Market orders display           │
│  ├── Map.razor            - Interactive map ⭐              │
│  ├── Trading.razor        - AI Trading Dashboard (NEW) ⭐   │
│  └── Settings.razor       - App configuration               │
│                                                              │
│  Components/Map/          - Map visualization (NEW) ⭐      │
│  ├── MapCanvas.razor      - Cytoscape.js canvas (978 LOC)  │
│  ├── MapControls.razor    - View mode & settings           │
│  └── SystemInfoTooltip    - Hover system statistics        │
│                                                              │
│  Components/Market/       - Market components (NEW) ⭐      │
│  ├── MarketFinderView.razor - 3-level hierarchical finder  │
│  └── MarketDataView.razor   - Snapshot visualization       │
│                                                              │
│  Components/Wallet/       - Wallet-specific UI components   │
│  Components/Character/    - Character-specific UI           │
└─────────────────────────────────────────────────────────────┘
                            ▼
┌─────────────────────────────────────────────────────────────┐
│ Service Layer (Business Logic)                              │
├─────────────────────────────────────────────────────────────┤
│  Services/Esi/                                              │
│  ├── EsiApiService        - ESI HTTP calls (all endpoints)  │
│  └── EsiCacheService      - In-memory ETag-based cache      │
│                                                              │
│  Services/Authentication/                                   │
│  └── EveAuthenticationService - OAuth 2.0 token mgmt        │
│                                                              │
│  Services/Map/            - Map & Navigation (NEW) ⭐       │
│  ├── MapDataService       - SDE queries, graph building     │
│  ├── MapStatisticsService - Live ESI statistics caching     │
│  └── RouteCalculationService - Pathfinding (stub)           │
│                                                              │
│  Services/Sde/                                              │
│  ├── SdeUpdateService     - Downloads SDE from Fuzzwork     │
│  └── SdeUniverseService   - Queries SDE (names, systems)    │
│                                                              │
│  Services/Wallet/                                           │
│  ├── WalletService        - Transaction processing          │
│  └── WalletLinkService    - Persistent link management      │
│                                                              │
│  Services/AI/             - AI Integration (NEW) ⭐         │
│  └── OllamaService        - Ollama LLM API client           │
│                                                              │
│  Services/Market/         - Market Analysis (NEW) ⭐        │
│  ├── MarketDataCollectorService - Background data collector │
│  ├── CostBasisCollectorService - Cost basis background jobs│
│  ├── CostBasisService - UI-facing cost basis API (NEW) ⭐  │
│  ├── BackgroundJobManager - persisted job state (NEW) ⭐  │
│  └── MarketAnalysisService - AI opportunity detection       │
└─────────────────────────────────────────────────────────────┘
                            ▼
┌─────────────────────────────────────────────────────────────┐
│ Data Layer (Entity Framework Core)                          │
├─────────────────────────────────────────────────────────────┤
│  Data/WalletDbContext.cs                                    │
│                                                              │
│  Database: ~/.local/share/WALLEve/Data/wallet.db            │
│  ├── WalletCharacter      - Character metadata              │
│  ├── WalletCorporation    - Corporation metadata            │
│  ├── WalletEntryLink      - Transaction links (core table)  │
│  ├── MarketSnapshots      - Market data (5min) (NEW) ⭐     │
│  ├── MarketHistory        - Historical data (daily) (NEW) ⭐│
│  ├── TradingOpportunities - AI opportunities (NEW) ⭐       │
│  ├── MarketTrends         - Price trends (NEW) ⭐           │
│  ├── WalletTransactionRecords - Local ESI tx mirror (NEW) ⭐│
│  ├── CostBasisEntries     - Prices per item (NEW) ⭐        │
│  ├── BackgroundJobs       - Persisted job state (NEW) ⭐    │
│  └── AppSettings          - KV settings (e.g. region) (NEW) │
│                                                              │
│  Database: ~/.local/share/WALLEve/Data/sde.sqlite           │
│  └── EVE Static Data Export (from Fuzzwork)                 │
│      ├── mapSolarSystems  - System data with coordinates    │
│      ├── mapSolarSystemJumps - Stargate connections         │
│      ├── mapRegions       - Region data                     │
│      └── mapConstellations - Constellation grouping         │
└─────────────────────────────────────────────────────────────┘
                            ▼
┌─────────────────────────────────────────────────────────────┐
│ External APIs & Client Libraries                            │
├─────────────────────────────────────────────────────────────┤
│  • EVE Online ESI: https://esi.evetech.net/                 │
│  • EVE OAuth: https://login.eveonline.com/                  │
│  • Fuzzwork SDE: https://www.fuzzwork.co.uk/dump/           │
│  • Cytoscape.js: Graph visualization library (CDN)          │
│  • Ollama: http://localhost:11434 (Local LLM) (NEW) ⭐      │
└─────────────────────────────────────────────────────────────┘
```

---

## Critical Files & Paths

### Services (Most Important)

#### Map Services (NEW)
- **`Services/Map/MapDataService.cs`** (552 lines) ⭐
  - Singleton service for SDE queries
  - Region/system/constellation data loading
  - Graph building for pathfinding
  - **BFS algorithm** for "systems within N jumps"
  - Cross-region connection handling
  - Live ESI system statistics (kills, jumps)

- **`Services/Map/MapStatisticsService.cs`** (190 lines) ⭐
  - Singleton service with 5-minute cache
  - Bulk system statistics from ESI
  - Combines kills + jumps data
  - PvP activity calculation

- **`Services/Map/RouteCalculationService.cs`** (60 lines) ⭐
  - **STUB IMPLEMENTATION** (planned feature)
  - Prepared for Dijkstra pathfinding
  - ESI route comparison
  - All methods currently return placeholder errors

#### ESI & Wallet Services
- **`Services/Esi/EsiApiService.cs`** (687 lines)
  - All ESI HTTP endpoints (wallet, market, character, universe)
  - ETag caching, rate limit handling, error recovery
  - Pagination support with parallel fetching
  - **Currently missing**: Regional market data endpoints are implemented via `GetRegionalMarketOrdersAsync`, `GetAllRegionalMarketOrdersAsync`, `GetMarketHistoryAsync`, and `GetMarketPricesAsync`

- **`Services/Wallet/WalletService.cs`** (1000+ lines)
  - Transaction linking engine (context-based, heuristic, manual)
  - Tax calculation with skill percentages
  - Market order linking via escrow matching
  - Transaction chain building

- **`Services/Authentication/EveAuthenticationService.cs`**
  - OAuth 2.0 PKCE flow
  - Token storage via Data Protection API
  - Automatic token refresh

### Map Components (NEW)

- **`Components/Pages/Map.razor`** ⭐
  - Main map page with 3 view modes:
    1. **Region View**: All 113 EVE regions
    2. **System View**: Systems in selected region
    3. **Local Environment**: Systems within N jumps of character
  - Live character tracking (60s updates)
  - View mode switching, region/system selectors
  - Statistics display toggle

- **`Components/Map/MapCanvas.razor`** (978 lines) ⭐
  - Cytoscape.js integration via JSInterop
  - Coordinate transformation: EVE 3D meters → 2D pixels
  - Collision detection algorithm
  - Dynamic node styling:
    - **Size**: Logarithmic scaling by activity
    - **Color**: Security status (green/yellow/red)
    - **Border**: PvP activity (red thickness)
  - Edge coloring by constellation/region

- **`Components/Map/MapControls.razor`** ⭐
  - View mode selector
  - Region/system autocomplete
  - Jump radius slider (1-10 jumps)
  - Live tracking toggle
  - Statistics panel

- **`Components/Map/SystemInfoTooltip.razor`** ⭐
  - Hover tooltips with system stats
  - 24h data: Jumps, Ship Kills, Pod Kills, NPC Kills
  - Security status, constellation, region

### Data Models

#### Map Models (NEW)
All in `Models/Map/`:
- **`MapRegionNode.cs`** - Region data with coordinates
- **`MapSolarSystemNode.cs`** - System data (ID, name, security, coordinates)
- **`MapConnection.cs`** - Stargate connections with metadata
- **`MapState.cs`** - Current map view state
- **`MapViewMode.cs`** - Enum: Region/System/LocalEnvironment
- **`SystemActivity.cs`** - Live activity data
- **`SystemStatistics.cs`** - ESI statistics

#### Routing Models (Prepared, Not Implemented)
- **`RouteResult.cs`** - Pathfinding results
- **`RouteComparisonResult.cs`** - Local vs ESI comparison
- **`RoutingAlgorithm.cs`** - Enum: LocalDijkstra/EsiApi/Both
- **`RoutingPreference.cs`** - Enum: Shorter/Safer/LessSecure

#### Market/Wallet Models
- **`Models/Esi/Markets/`** - Market-related API models
  - `MarketOrder.cs` - Active orders
  - `MarketOrderHistory.cs` - Historical orders with state

- **`Models/Esi/Wallet/`** - Wallet API models
  - `WalletJournalEntry.cs` - Journal entries
  - `WalletTransaction.cs` - Market transactions

- **`Models/Wallet/`** - View models for UI
  - `WalletEntryViewModel.cs` - Rich journal entry display
  - `TransactionChain.cs` - Full transaction lifecycle
  - `GroupedMarketTransaction.cs` - Combined transaction + tax

### Database Context
- **`Data/WalletDbContext.cs`**
  - Entity Framework Core DbContext
  - Tables: WalletCharacter, WalletCorporation, WalletEntryLink
  - Complex indexing for fast link queries

### JavaScript Interop
- **`wwwroot/js/cytoscape-map.js`** ⭐
  - Cytoscape.js graph initialization
  - Dynamic graph updates
  - Node highlighting (character position)
  - Hover tooltip callbacks to C#
  - Pan/zoom controls

---

## Map Visualization System (NEW MAJOR FEATURE)

### Overview
The map is a **fully functional interactive visualization** of the EVE Online universe using Cytoscape.js, integrated with live ESI data and character tracking.

### 3 View Modes

```
┌──────────────────────────────────────────────────────────┐
│ REGION VIEW (113 regions)                                │
│                                                           │
│   [The Forge]          [Domain]                          │
│        ●──────────────────●                              │
│        │                  │                              │
│   [Lonetrek]        [Tash-Murkon]                       │
│        ●                  ●                              │
│                                                           │
│  - Shows all EVE regions as nodes                        │
│  - Connections = jumpgate links between regions          │
│  - Node size = number of systems in region               │
└──────────────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────────────┐
│ SYSTEM VIEW (systems in selected region)                 │
│                                                           │
│   [Jita] ← Your Character                               │
│     ●═══════════════●  [Maurasi]                        │
│     ║               │                                    │
│     ║  [Perimeter]  │                                    │
│     ●═══════●       │                                    │
│         ║           │                                    │
│         ●───────────●                                    │
│     [Sobaseki]   [New Caldari]                          │
│                                                           │
│  Color Coding:                                           │
│  ═══ Green (solid) - Same constellation                 │
│  ─── Cyan (solid)  - Same region, different const.      │
│  ─ ─ Orange (dash) - Cross-region connection            │
│                                                           │
│  - Blue border = Character's current system              │
│  - Red border thickness = PvP activity                   │
│  - Node size = System activity (kills + jumps)           │
└──────────────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────────────┐
│ LOCAL ENVIRONMENT (N jumps from character)                │
│                                                           │
│  Jump Radius: ├────●────┤ 5 jumps                       │
│                                                           │
│         [System A]                                        │
│              ●                                            │
│             ╱ ╲                                          │
│            ●   ●   [Your Location]                       │
│           ╱     ╲       ●══════════●                     │
│          ●       ●─────╱             ╲                   │
│       [System B]    [System C]    [System D]            │
│                                                           │
│  - Shows only systems within configurable jump range     │
│  - Updates when character moves                          │
│  - BFS algorithm from current position                   │
└──────────────────────────────────────────────────────────┘
```

### Live Data Integration

**Character Tracking:**
- Polls character location every 60 seconds (configurable)
- Auto-highlights current system (blue border)
- Auto-switches region view when character moves
- Toggle on/off in controls

**System Statistics (ESI):**
- Updated every 5 minutes
- **Kills**: Ship kills, Pod kills, NPC kills (24h)
- **Jumps**: System traffic (24h)
- **PvP Activity**: Ship kills + Pod kills
- Cached for performance

**Visual Indicators:**
```javascript
// Node size calculation (logarithmic)
width = 18 + Math.log10(kills + jumps + 1) * 5

// Security status colors
Highsec (≥0.5): Green (#00ff00)
Lowsec (0.1-0.4): Yellow (#ffff00)
Nullsec (≤0.0): Red (#ff0000)

// PvP border
border-width = 1 + Math.min(3, Math.log10(pvpActivity + 1))
border-color = pvpActivity > 0 ? '#ff0000' : '#888'
```

### Advanced Features

**Cross-Region Dummy Nodes:**
- Virtual nodes at map edges representing neighboring regions
- Positioned based on connection direction
- Shows region name + connection count
- Clickable to switch region

**Collision Detection:**
```csharp
// Iterative push-apart algorithm
while (hasCollisions)
{
    foreach (node pair in overlapping nodes)
    {
        vector = node2.pos - node1.pos
        distance = length(vector)
        overlap = minDistance - distance

        // Push nodes apart
        node1.pos -= normalize(vector) * overlap/2
        node2.pos += normalize(vector) * overlap/2
    }
}
```

**Constellation-Based Grouping:**
- Green edges: Same constellation
- Cyan edges: Same region, different constellations
- Orange dashed: Cross-region connections
- Helps identify natural travel routes

---

## ESI API Integration

### Local Documentation
**IMPORTANT**: ESI documentation is available locally at:
- **Path**: `.esi-docs/` (Git submodule from https://github.com/esi/esi-docs)
- **Online**: https://developers.eveonline.com/
- **Swagger JSON / OpenAPI**: `https://esi.evetech.net/meta/openapi.json` ⭐
  - Hinweis: Der alte Endpoint `/latest/swagger.json` existiert nicht mehr.
  - Alternative: Offizielle Developer Docs unter https://developers.eveonline.com/docs/

### Currently Integrated Endpoints

#### Character Data
- `GET /characters/{character_id}/` - Public character info
- `GET /characters/{character_id}/location/` - **Current location** (for map tracking) ⭐
- `GET /characters/{character_id}/ship/` - Current ship
- `GET /characters/{character_id}/wallet/` - Wallet balance
- `GET /characters/{character_id}/wallet/journal/` - Journal entries (paginated)
- `GET /characters/{character_id}/wallet/transactions/` - Transactions (paginated)
- `GET /characters/{character_id}/orders/` - Active market orders
- `GET /characters/{character_id}/orders/history/` - Historical market orders
- `GET /characters/{character_id}/skills/` - Skill levels

#### Universe Data (For Map) ⭐
- `GET /universe/systems/{system_id}/kills/` - **System kills (24h)** ⭐
- `GET /universe/system_jumps/` - **All system jumps (24h)** ⭐
- `GET /universe/types/{type_id}/` - Type information
- `GET /universe/stations/{station_id}/` - Station info
- `GET /universe/structures/{structure_id}/` - Structure info (requires auth)
- `GET /characters/{character_id}/` - Character name
- `GET /corporations/{corporation_id}/` - Corporation info
- `GET /alliances/{alliance_id}/` - Alliance info

#### Public Endpoints
- `GET /universe/systems/{system_id}/kills/` - System PvP statistics
- `GET /universe/system_jumps/` - All system traffic

### Missing Endpoints (Needed for AI Market Analysis Feature)

#### Regional Market Data (Public - No Auth Required)
```
GET /markets/{region_id}/orders/
  - Returns: All active buy/sell orders in a region
  - Parameters: region_id, type_id (optional), order_type (buy/sell/all), page
  - Cache: 5 minutes
  - Max items per page: 1000
  - Use case: Real-time market snapshot for arbitrage/station trading

GET /markets/{region_id}/history/
  - Returns: Historical daily statistics for a type
  - Parameters: region_id, type_id
  - Cache: Daily at 11:05 EVE time
  - Returns: average, highest, lowest, volume, order_count per day
  - Max: 500 days of history
  - Use case: Trend analysis, price predictions

GET /markets/prices/
  - Returns: Global average/adjusted prices for all ~20,000 types
  - Cache: 1 hour
  - Use case: Quick price reference, industry calculations

GET /markets/{region_id}/types/
  - Returns: List of type_ids available in region
  - Use case: Discovery of tradeable items

GET /markets/groups/
GET /markets/groups/{market_group_id}/
  - Market group hierarchy
  - Use case: Categorization of trading opportunities
```

### ESI Best Practices (Already Implemented)
- **ETag Caching**: All requests use If-None-Match headers
- **Rate Limiting**: X-Ratelimit-* headers are monitored
- **Error Budget**: X-ESI-Error-Limit-* tracking warns at <20%
- **Pagination**: Automatic multi-page fetching with X-Pages header
- **User-Agent**: Custom UA header identifies the app
- **Bounded Pagination (NEW)**: Multi-page fetches (wallet transactions) use
  semaphore-limited parallelism (max 4 concurrent) + 250ms staggering instead
  of fire-all bursts — ESI's token system costs 2/1/5/0 per 2xx/3xx/4xx/5xx
  and a 100-errors-per-minute budget leads to 420 on ALL routes

---

## Transaction Linking System

### How It Works
The app has a sophisticated multi-phase linking system to connect related wallet entries:

#### Phase 1: Context-Based Linking (100% Confidence)
- Uses ESI's `context_id` field to directly link entries
- Links: `transaction_tax`, `market_escrow`, `market_escrow_release`, `brokers_fee`

#### Phase 2: Heuristic Tax Linking (85-90% Confidence)
- Matches transactions to tax entries within configurable time window (default: 60s)
- Validates tax percentage (2-8% range with 10% tolerance)
- Checks SecondPartyId == 1000132 (SCC - Secure Commerce Commission)
- Adjusts for Accounting skill (reduces tax by up to 0.5%)

#### Phase 3: Escrow Pair Matching (90% Confidence)
- Links `market_escrow` (when order placed) with `market_escrow_release` (when order filled/expired)
- Amount matching with ±1 cent tolerance
- Time range: up to 90 days (maximum order lifetime)

#### Phase 4: Market Order Linking
- Matches escrow entries to actual MarketOrder objects
- 60-second time window matching
- Stores order metadata (OrderId, TypeId, Price, Volume)

### Link Storage (WalletEntryLink Table)
```sql
CREATE TABLE WalletEntryLink (
    Id INTEGER PRIMARY KEY,
    SourceEntryId BIGINT,
    TargetEntryId BIGINT,
    CharacterId INTEGER,
    CorporationId INTEGER,
    Division INTEGER,
    Type TEXT,  -- 'DirectContextId', 'HeuristicTax', 'EscrowPair', 'MarketOrder', etc.
    Confidence TEXT,  -- 'Direct', 'HeuristicHigh', 'HeuristicMedium', 'HeuristicLow', 'Manual'
    CreatedAt DATETIME,
    IsManuallyVerified BOOLEAN,
    IsManuallyRejected BOOLEAN,
    CreatedBy TEXT,  -- 'Heuristic', 'Manual', 'AI' (future)
    UserNotes TEXT
)
```

**Note**: The `CreatedBy` field already supports "AI" as a source, preparing for future ML integration!

---

## Configuration

### appsettings.json Structure
```json
{
  "Application": {
    "DataPath": "~/.local/share/WALLEve/Data"
  },
  "EveOnline": {
    "ClientId": "YOUR_CLIENT_ID_HERE",
    "CallbackUrl": "http://localhost:5080/callback",
    "SsoBaseUrl": "https://login.eveonline.com/v2/oauth",
    "EsiBaseUrl": "https://esi.evetech.net/latest",
    "Scopes": ["esi-wallet.read_character_wallet.v1", "esi-location.read_location.v1", ...],

    "Map": {
      "CharacterTrackingIntervalSeconds": 60,
      "CharacterTrackingEnabledByDefault": true
    },

    "Wallet": {
      "LocalFileName": "wallet.db",
      "TaxLinkingTimeWindowSeconds": 60,
      "TaxMinPercentage": 0.02,
      "TaxMaxPercentage": 0.08,
      "TaxTolerancePercentage": 0.10
    },

    "Sde": {
      "LocalFileName": "sde.sqlite",
      "DownloadUrl": "https://www.fuzzwork.co.uk/dump/sqlite-latest.sqlite.bz2"
    }
  }
}
```

> **Hinweis**: Die Felder `AuthorizationEndpoint` und `TokenEndpoint` werden aus `SsoBaseUrl` abgeleitet; sie sind keine separaten Einstellungen.

### Required EVE Developer Application Setup
1. Create app at https://developers.eveonline.com/
2. Set callback URL: `http://localhost:5080/callback`
3. Enable required scopes (see appsettings.json)
4. **Important scopes for map**: `esi-location.read_location.v1`, `esi-location.read_ship_type.v1`
5. Copy Client ID to appsettings.json

### Service Registration (Program.cs)
```csharp
// Map services (Singleton)
builder.Services.AddSingleton<IMapDataService, MapDataService>();
builder.Services.AddSingleton<IMapStatisticsService, MapStatisticsService>();
builder.Services.AddSingleton<IRouteCalculationService, RouteCalculationService>();

// Other services
builder.Services.AddScoped<IEsiApiService, EsiApiService>();
builder.Services.AddScoped<IEveAuthenticationService, EveAuthenticationService>();
// ... etc
```

---

## Current Features

### Working Features
- ✅ OAuth 2.0 authentication with EVE Online
- ✅ Multi-character support (switching between characters)
- ✅ **Interactive map with 3 view modes** ⭐ NEW
- ✅ **Live character position tracking** ⭐ NEW
- ✅ **Real-time system statistics from ESI** ⭐ NEW
- ✅ **Constellation-based edge coloring** ⭐ NEW
- ✅ **Systems within N jumps calculation (BFS)** ⭐ NEW
- ✅ **Cross-region dummy nodes** ⭐ NEW
- ✅ **Dynamic node styling by activity** ⭐ NEW
- ✅ Wallet journal display with filtering
- ✅ Transaction history with detailed breakdown
- ✅ Automatic tax linking (heuristic + context-based)
- ✅ Market order display (active + historical)
- ✅ Transaction chain visualization (Order → Transaction → Tax → Escrow Release)
- ✅ SDE integration for item/location names
- ✅ Persistent link storage in local database
- ✅ Manual verification/rejection of links
- ✅ ETag-based caching for ESI calls

### Known Limitations
- ❌ **Route calculation not implemented** (stub exists, Dijkstra planned)
- ❌ **Single character only** — no character switching yet; logout/login is supported
- ⚠️ AI/Market features are optional; without Ollama only basic market data collection is available

---

## Development Tips for AI Assistants

### When Starting a New Session
1. Read this document first
2. Check the TODO section below for current implementation status
3. If exploring specific functionality, start with the service layer files
4. Use the local `.esi-docs/` for ESI API reference
5. **Map features are fully functional** - focus on AI/market analysis next

### Code Patterns to Follow
- **Services**: Interface-based DI, injected via constructor
- **HTTP Calls**: Always use named HttpClient from IHttpClientFactory
- **Error Handling**: Comprehensive logging with status code checks
- **Database**: Entity Framework Core with migrations
- **Blazor**: InteractiveServer render mode, use `@rendermode InteractiveServer`
- **JSInterop**: Use `IJSRuntime` for JavaScript communication

### Map-Specific Patterns
- **Graph algorithms**: BFS for jump distance, collision detection for layout
- **Coordinate transformation**: EVE uses meters in 3D space, map uses 2D pixels
- **Singleton services**: MapDataService, MapStatisticsService (shared state)
- **Cytoscape.js**: Initialize once, update dynamically

### Important EVE Online Constants
- **Major Trade Hubs**:
  - Jita (Region 10000002, System 30000142) - Largest market
  - Amarr (Region 10000043, System 30002187)
  - Dodixie (Region 10000032, System 30002659)
  - Rens (Region 10000030, System 30002510)
  - Hek (Region 10000042, System 30002053)

- **Tax/Fee Rates**:
  - Sales Tax: 2.5% base (reduced by Accounting skill, min ~2%)
  - Broker Fee: 3% base (reduced by Broker Relations skill)
  - Relist Fee: 100 ISK per order modification

- **SCC (Secure Commerce Commission)**: Character ID 1000132 (receives all taxes)

- **Security Status**:
  - Highsec: 0.5 to 1.0
  - Lowsec: 0.1 to 0.4
  - Nullsec: -1.0 to 0.0

### Testing Notes
- Use `/tmp` for temporary files
- Local databases in `~/.local/share/WALLEve/Data/`
- App runs on `http://localhost:5080`
- SDE database ~500MB (contains all EVE universe data)

---

# ✅ AI-Powered Market Analysis - IMPLEMENTED!

> **Status**: ✅ **COMPLETED** (December 2025)
> **Achievement**: Successfully implemented local AI-powered market analysis using Ollama
> **Features**: Automatic data collection, AI opportunity detection, Trading Dashboard

---

## 🎉 What's Been Implemented

### Phase 1: Data Collection Foundation ✅
**Status**: **COMPLETE**

**ESI API Extensions**:
- ✅ `GetRegionalMarketOrdersAsync()` - Regional market orders with pagination
- ✅ `GetMarketHistoryAsync()` - Historical price data
- ✅ `GetMarketPricesAsync()` - Global average prices
- ✅ `GetAllRegionalMarketOrdersAsync()` - Multi-page collection helper

**Data Models**:
- ✅ `RegionalMarketOrder` - Buy/sell orders with location, price, volume
- ✅ `MarketHistoryEntry` - Daily statistics (avg, high, low, volume)
- ✅ `MarketPrice` - Global pricing data

**Database Schema** (`wallet.db`):
- ✅ `MarketSnapshots` - Real-time price snapshots (every 5 min)
  - Best buy/sell prices, volumes, spreads
  - Indexed on RegionId + TypeId + Timestamp
- ✅ `MarketHistory` - Historical statistics (daily)
- ✅ `TradingOpportunities` - AI-detected opportunities
  - Confidence scores (60-95%)
  - AI reasoning, profit estimates
  - Map integration fields (BuySystemId, SellSystemId, JumpDistance)
- ✅ `MarketTrends` - Price trend analysis

**Background Services**:
- ✅ `MarketDataCollectorService` - Hosted service running every 5 minutes
  - Tracks 5 major hubs: Jita, Amarr, Dodixie, Rens, Hek
  - Monitors 10 items: PLEX, Skill Injectors, Minerals
  - Automatic cleanup (7-day retention)
  - **Optimized**: Long volume fields, reduced logging

### Phase 2: Ollama Integration ✅
**Status**: **COMPLETE**

**AI Services**:
- ✅ `OllamaService` - HTTP client for Ollama API (localhost:11434)
  - `GenerateAsync()` - Simple text generation
  - `GenerateJsonAsync<T>()` - Structured JSON responses
  - `IsAvailableAsync()` - Connection health check
  - `GetAvailableModelsAsync()` - Model discovery
  - JSON extraction with markdown handling

**Configuration**:
- ✅ `AISettings` - Configuration class for Ollama + Market Analysis
- ✅ `appsettings.json` - Ollama endpoint, model selection (llama3.1:latest)
- ✅ HttpClient factory with timeout configuration

**Market Analysis**:
- ✅ `MarketAnalysisService` - Opportunity detection
  - `TestOllamaConnectionAsync()` - Diagnostics endpoint
  - `AnalyzeMarketDataAsync()` - Spread-based opportunity finder
  - Currently heuristic (>3% spread), ready for full AI integration

**Test Endpoints**:
- ✅ `/test/ollama` - Connection test with EVE-specific prompt

### Phase 3: UI Dashboard ✅
**Status**: **COMPLETE**

**Trading Page** (`/trading`):
- ✅ AI status indicator (Online/Offline)
- ✅ Opportunities counter
- ✅ Opportunity cards with:
  - Item name (from SDE)
  - Station name display (via SDE location lookup)
  - Buy/Sell prices, estimated profit
  - Confidence score with percentage
  - AI model used
  - Reasoning text
  - Time tracking (detected, expires)
- ✅ Sorting functionality:
  - Sort by Confidence (default)
  - Sort by Profit
  - Sort by Timestamp (newest)
- ✅ Filtering controls:
  - Minimum Confidence filter (0-100%)
  - Minimum Profit filter (ISK amount)
  - Reactive updates with `@bind:after`
- ✅ Manual "Analyze Market" button
- ✅ Loading states and error handling
- ✅ Error banner with dismissible UI

**Navigation**:
- ✅ New "🤖 AI Trading" menu item in "Wirtschaft" section

### Optimizations & Fixes ✅
- ✅ **Logging**: EF Core debug logs suppressed (99% less spam)
- ✅ **Overflow Fix**: Volume fields changed from `int` to `long`
- ✅ **Performance**: Reduced verbose logging in MarketDataCollector
- ✅ **Database Migration**: `FixMarketSnapshotVolumeOverflow`

### Phase 4: UI Enhancements & Code Quality ✅
**Status**: **COMPLETE** (December 2025)

**Station/Location Integration**:
- ✅ Added `BuyLocationId` and `SellLocationId` to `TradingOpportunity` model
- ✅ Added `BestBuyLocationId`, `BestSellLocationId`, `BestBuySystemId`, `BestSellSystemId` to `MarketSnapshot` model
- ✅ Extended `ISdeUniverseService` with `GetLocationNameAsync(long locationId)`
- ✅ Modified `MarketDataCollectorService` to capture full order objects for location data
- ✅ Updated `MarketAnalysisService` to populate location fields in opportunities
- ✅ Trading UI displays station names from SDE (mapDenormalize table)
- ✅ Database migrations: `20251229_AddLocationToTradingOpportunity`, `20251229_AddLocationToMarketSnapshot`

**UI Improvements**:
- ✅ Sorting controls (Confidence/Profit/Timestamp)
- ✅ Filtering inputs (Min Confidence, Min Profit)
- ✅ Reactive UI updates with Blazor `@bind:after` directive
- ✅ Dictionary caching for type names and location names
- ✅ Error banner component with dismissible UI
- ✅ Enhanced transaction item display with location context

**Code Documentation**:
- ✅ XML documentation for `OllamaService` methods:
  - `IsAvailableAsync()` - Connection health check
  - `GetAvailableModelsAsync()` - Model discovery
  - `GenerateAsync()` - Text generation with context
  - `GenerateJsonAsync<T>()` - Structured JSON responses
- ✅ XML documentation for `MarketAnalysisService` methods:
  - `TestOllamaConnectionAsync()` - Diagnostic endpoint
  - `AnalyzeMarketDataAsync()` - Opportunity detection algorithm

**Error Handling**:
- ✅ User-facing error messages in Trading page
- ✅ Error banner with close button
- ✅ Try-catch blocks in Ollama connection checks
- ✅ Graceful handling of SDE unavailability

---

## 📊 Current Performance

**Test Results** (December 29, 2025):
- ✅ 13 Ollama models available
- ✅ 45 market snapshots collected successfully
- ✅ 30 trading opportunities created
- ✅ Top confidence: 86% (Isogen station trading)
- ✅ No errors, clean logs
- ✅ Average spread detected: 5-19%

**Database Stats**:
- MarketSnapshots: ~290 entries
- TradingOpportunities: 30 active
- Coverage: 5 regions × 10 items = 50 markets tracked

---

# 🚧 TODO: Future Enhancements

## Vision: Market Analysis Dashboard

```
╔══════════════════════════════════════════════════════════════╗
║  📊 WALL-EVE - AI Market Analysis               🔄 Live Data ║
╠══════════════════════════════════════════════════════════════╣
║                                                               ║
║  ┌─────────────────────┐  ┌─────────────────────┐           ║
║  │ 🔥 Hot Deals        │  │ 📈 Trending Up      │           ║
║  │                     │  │                     │           ║
║  │ PLEX                │  │ Tritanium +12.5%    │           ║
║  │ Buy:  3.20M ISK     │  │ Current: 6.82 ISK   │           ║
║  │ Sell: 3.50M ISK     │  │ 7-day avg: 6.05 ISK │           ║
║  │ Profit: ~280k ISK   │  │                     │           ║
║  │                     │  │ AI: Strong buy      │           ║
║  │ Confidence: 92%     │  │ Confidence: 87%     │           ║
║  │ [Details] [Track]   │  │ [Details] [Alert]   │           ║
║  └─────────────────────┘  └─────────────────────┘           ║
║                                                               ║
║  ┌──────────────────────────────────────────────────┐       ║
║  │ 💎 Station Trading Opportunities                 │       ║
║  ├────────┬────────┬────────┬──────────┬───────────┤       ║
║  │ Item   │ Spread │ Volume │ Est. ROI │ AI Score  │       ║
║  ├────────┼────────┼────────┼──────────┼───────────┤       ║
║  │ Skill: │  15.2% │  High  │ 3.2%/hr  │ ⭐⭐⭐⭐⭐ │       ║
║  │ Cyber. │        │        │          │           │       ║
║  ├────────┼────────┼────────┼──────────┼───────────┤       ║
║  │ Fac.   │   8.7% │  Med   │ 1.8%/hr  │ ⭐⭐⭐⭐   │       ║
║  │ Ammo   │        │        │          │           │       ║
║  ├────────┼────────┼────────┼──────────┼───────────┤       ║
║  │ Ships: │   4.3% │  Low   │ 0.9%/hr  │ ⭐⭐⭐     │       ║
║  │ Frigate│        │        │          │           │       ║
║  └────────┴────────┴────────┴──────────┴───────────┘       ║
║                                                               ║
║  ┌──────────────────────────────────────────────────┐       ║
║  │ 🌍 Regional Arbitrage (Map Integration) ⭐       │       ║
║  ├──────────────────────────────────────────────────┤       ║
║  │ Jita → Amarr: Faction Modules                   │       ║
║  │ Buy Price:   45.2M ISK                           │       ║
║  │ Sell Price:  47.1M ISK                           │       ║
║  │ Profit:      +1.9M ISK (+4.2%)                   │       ║
║  │                                                   │       ║
║  │ 🤖 AI Reasoning:                                 │       ║
║  │ "Low competition in Amarr market. Historical    │       ║
║  │  data shows consistent 3-5% spread. Volume is    │       ║
║  │  sufficient for 5-10 units per day."             │       ║
║  │                                                   │       ║
║  │ Distance: 23 jumps (12 HS, 8 LS, 3 NS) ⭐       │       ║
║  │ Risk: ⚠️ Medium (lowsec route)                   │       ║
║  │ [Show Route on Map] [Execute] [Watchlist]       │       ║
║  └──────────────────────────────────────────────────┘       ║
║                                                               ║
║  ┌──────────────────────────────────────────────────┐       ║
║  │ 📊 Your Trading Performance (Last 7 Days)        │       ║
║  ├──────────────────────────────────────────────────┤       ║
║  │ Total Profit:        +1.2B ISK                   │       ║
║  │ Best Performing:     PLEX (+180M ISK)            │       ║
║  │ ROI:                 8.3%                         │       ║
║  │ Completed Trades:    47                          │       ║
║  │ Most Traded Region:  The Forge (Jita) ⭐         │       ║
║  │                                                   │       ║
║  │ 🤖 AI Suggestion:                                │       ║
║  │ "Consider diversifying into minerals. Your PLEX │       ║
║  │  concentration is high (65% of portfolio).       │       ║
║  │  Tritanium shows strong upward trend."           │       ║
║  └──────────────────────────────────────────────────┘       ║
║                                                               ║
╚══════════════════════════════════════════════════════════════╝
```

**Note**: ⭐ indicates integration with existing map features (route calculation, distance, security analysis)

---

## Phase 1: Data Collection Foundation

### 1.1 Extend ESI API Service
**File**: `Services/Esi/EsiApiService.cs`

Add new methods:
```csharp
// Regional market orders (all active buy/sell orders)
Task<List<RegionalMarketOrder>> GetRegionalMarketOrdersAsync(
    int regionId,
    int? typeId = null,
    string orderType = "all")

// Historical price statistics
Task<List<MarketHistoryEntry>> GetMarketHistoryAsync(
    int regionId,
    int typeId)

// Global average prices
Task<List<MarketPrice>> GetMarketPricesAsync()

// List tradeable types in region
Task<List<int>> GetRegionalTypesAsync(int regionId)

// Market group hierarchy
Task<List<MarketGroup>> GetMarketGroupsAsync()
Task<MarketGroup> GetMarketGroupAsync(int groupId)
```

### 1.2 Create New Models
**Path**: `Models/Esi/Markets/`

```csharp
// RegionalMarketOrder.cs
public class RegionalMarketOrder
{
    public long OrderId { get; set; }
    public int TypeId { get; set; }
    public long LocationId { get; set; }
    public int SystemId { get; set; }
    public int VolumeTotal { get; set; }
    public int VolumeRemain { get; set; }
    public int MinVolume { get; set; }
    public double Price { get; set; }
    public bool IsBuyOrder { get; set; }
    public int Duration { get; set; }
    public DateTime Issued { get; set; }
    public string Range { get; set; }  // "station", "region", "1", "2", etc.
}

// MarketHistoryEntry.cs
public class MarketHistoryEntry
{
    public DateTime Date { get; set; }
    public double Average { get; set; }
    public double Highest { get; set; }
    public double Lowest { get; set; }
    public long Volume { get; set; }
    public long OrderCount { get; set; }
}

// MarketPrice.cs
public class MarketPrice
{
    public int TypeId { get; set; }
    public double? AveragePrice { get; set; }
    public double? AdjustedPrice { get; set; }
}
```

### 1.3 Database Schema Extensions
**File**: `Data/WalletDbContext.cs`

Add new DbSets and create migration:

```csharp
// Market snapshot for real-time tracking
public class MarketSnapshot
{
    public int Id { get; set; }
    public int RegionId { get; set; }
    public int TypeId { get; set; }
    public DateTime Timestamp { get; set; }

    public double? BestBuyPrice { get; set; }
    public double? BestSellPrice { get; set; }
    public long BuyVolume { get; set; }  // Changed to long for large volumes
    public long SellVolume { get; set; }  // Changed to long for large volumes
    public double? Spread { get; set; }

    // Location tracking (NEW - Phase 4)
    public int? BestBuySystemId { get; set; }
    public int? BestSellSystemId { get; set; }
    public long? BestBuyLocationId { get; set; }  // Station/Citadel ID
    public long? BestSellLocationId { get; set; }  // Station/Citadel ID

    // Indexes: (RegionId, TypeId, Timestamp)
}

// Historical price data
public class MarketHistory
{
    public int Id { get; set; }
    public int RegionId { get; set; }
    public int TypeId { get; set; }
    public DateTime Date { get; set; }

    public double Average { get; set; }
    public double Highest { get; set; }
    public double Lowest { get; set; }
    public long Volume { get; set; }
    public long OrderCount { get; set; }

    // Indexes: (TypeId, Date), (RegionId, TypeId)
}

// AI-generated trading opportunities
public class TradingOpportunity
{
    public int Id { get; set; }
    public int TypeId { get; set; }
    public string OpportunityType { get; set; }  // "arbitrage", "station_trading", "trend"

    // Regions (null for single-region opportunities)
    public int? BuyRegionId { get; set; }
    public int? SellRegionId { get; set; }

    // Map integration ⭐
    public int? BuySystemId { get; set; }
    public int? SellSystemId { get; set; }
    public int? JumpDistance { get; set; }
    public string RouteSecurityAnalysis { get; set; }  // JSON: {highsec: 10, lowsec: 5, nullsec: 2}

    // Location tracking (NEW - Phase 4) ⭐
    public long? BuyLocationId { get; set; }  // Station/Citadel for buy order
    public long? SellLocationId { get; set; }  // Station/Citadel for sell order

    // Pricing
    public double? BuyPrice { get; set; }
    public double? SellPrice { get; set; }
    public double EstimatedProfit { get; set; }
    public double RequiredCapital { get; set; }

    // AI Assessment
    public double Confidence { get; set; }  // 0-100
    public string AIModel { get; set; }  // e.g., "llama3.1:8b"
    public string Reasoning { get; set; }  // AI explanation

    // Lifecycle
    public DateTime DetectedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string Status { get; set; }  // "active", "executed", "expired", "invalid"

    // Performance tracking
    public DateTime? ExecutedAt { get; set; }
    public double? ActualProfit { get; set; }

    // Indexes: (Status, ExpiresAt), (TypeId, Status)
}

// Market trend analysis
public class MarketTrend
{
    public int Id { get; set; }
    public int TypeId { get; set; }
    public int RegionId { get; set; }

    // Trend data
    public string TrendType { get; set; }  // "bullish", "bearish", "sideways", "volatile"
    public double Strength { get; set; }  // 0-100
    public double? PredictedChange { get; set; }  // Predicted % change

    // Time window
    public string TimeWindow { get; set; }  // "24h", "7d", "30d"
    public DateTime AnalyzedAt { get; set; }

    // AI metadata
    public string AIModel { get; set; }
    public string Features { get; set; }  // JSON for debugging

    // Indexes: (TypeId, AnalyzedAt), (TrendType, Strength)
}
```

### 1.4 Background Data Collection Service
**New File**: `Services/Market/MarketDataCollectorService.cs`

```csharp
public class MarketDataCollectorService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MarketDataCollectorService> _logger;

    // Major trade hubs to track
    private readonly int[] _trackedRegions = { 10000002, 10000043, 10000032 };  // Jita, Amarr, Dodixie

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await CollectMarketDataAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);  // Every 5 minutes
        }
    }

    private async Task CollectMarketDataAsync(CancellationToken ct)
    {
        // 1. Fetch regional market orders for tracked items
        // 2. Calculate spread, volume, best prices
        // 3. Store snapshot in database
        // 4. Once per day: Fetch historical data
    }
}
```

---

## Phase 2: Ollama Integration

### 2.1 Ollama Service
**New File**: `Services/AI/OllamaService.cs`

```csharp
public class OllamaService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OllamaService> _logger;
    private const string DefaultModel = "llama3.1:8b";

    public OllamaService(HttpClient httpClient, ILogger<OllamaService> logger)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri("http://localhost:11434");
        _logger = logger;
    }

    public async Task<string> GenerateAsync(string prompt, object? context = null, string model = DefaultModel)
    {
        var request = new
        {
            model = model,
            prompt = context != null
                ? $"{prompt}\n\nContext:\n{JsonSerializer.Serialize(context)}"
                : prompt,
            stream = false,
            options = new
            {
                temperature = 0.7,
                top_p = 0.9
            }
        };

        var response = await _httpClient.PostAsJsonAsync("/api/generate", request);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaResponse>();
        return result?.Response ?? string.Empty;
    }

    public async Task<T> GenerateJsonAsync<T>(string prompt, object? context = null, string model = DefaultModel)
    {
        var jsonPrompt = $"{prompt}\n\nIMPORTANT: Return ONLY valid JSON, no additional text.";
        var response = await GenerateAsync(jsonPrompt, context, model);

        // Extract JSON from response (handle markdown code blocks)
        var json = ExtractJson(response);
        return JsonSerializer.Deserialize<T>(json);
    }
}
```

### 2.2 Market Analysis Service with Map Integration
**New File**: `Services/Market/MarketAnalysisService.cs`

```csharp
public class MarketAnalysisService
{
    private readonly OllamaService _ollama;
    private readonly WalletDbContext _dbContext;
    private readonly IMapDataService _mapDataService;  // ⭐ Map integration
    private readonly ILogger<MarketAnalysisService> _logger;

    // Find arbitrage opportunities with route calculation ⭐
    public async Task<List<TradingOpportunity>> FindArbitrageOpportunitiesAsync(
        List<RegionalMarketOrder> orders,
        int[] regions)
    {
        var prompt = @"
            Analyze these EVE Online market orders and find arbitrage opportunities.

            Consider:
            - Price spreads between regions (buy low in one, sell high in another)
            - Available volumes (can we actually trade this?)
            - Transaction costs: 2.5% sales tax + 3% broker fee on both ends
            - Minimum profit threshold: 5% after fees

            Return JSON array of opportunities:
            [{
                ""typeId"": int,
                ""buyRegion"": int,
                ""sellRegion"": int,
                ""buySystemId"": int,
                ""sellSystemId"": int,
                ""buyPrice"": double,
                ""sellPrice"": double,
                ""estimatedProfit"": double,
                ""confidence"": 0-100,
                ""reasoning"": ""string""
            }]
        ";

        var opportunities = await _ollama.GenerateJsonAsync<List<ArbitrageOpportunityDto>>(
            prompt,
            orders);

        // ⭐ Calculate routes for each opportunity
        foreach (var opp in opportunities)
        {
            // Use map service to calculate jump distance and security analysis
            var systems = await _mapDataService.GetSystemsWithinJumpsAsync(
                opp.BuySystemId,
                100); // Max jumps to check

            if (systems.Any(s => s.SolarSystemId == opp.SellSystemId))
            {
                // Calculate route security breakdown
                // Store in opportunity.RouteSecurityAnalysis
            }
        }

        return ConvertAndSaveOpportunities(opportunities, "arbitrage");
    }

    // Detect price trends using historical data
    public async Task<List<MarketTrend>> DetectTrendsAsync(
        int typeId,
        List<MarketHistoryEntry> history)
    {
        var prompt = @"
            Analyze this price history for an EVE Online item.

            Identify:
            - Trend direction (bullish/bearish/sideways)
            - Trend strength (0-100)
            - Volatility level
            - Predicted price change for next 7 days (percentage)
            - Key support/resistance levels

            Return JSON:
            {
                ""trendType"": ""bullish"" | ""bearish"" | ""sideways"" | ""volatile"",
                ""strength"": 0-100,
                ""predictedChange"": double (percentage),
                ""reasoning"": ""string""
            }
        ";

        var trend = await _ollama.GenerateJsonAsync<TrendAnalysisDto>(
            prompt,
            history);

        return ConvertAndSaveTrend(typeId, trend);
    }

    // Find station trading opportunities (buy/sell in same location)
    public async Task<List<TradingOpportunity>> FindStationTradingAsync(
        List<RegionalMarketOrder> orders)
    {
        var prompt = @"
            Analyze market orders for station trading opportunities.

            Look for:
            - Large spread between best buy and sell orders (>5% after fees)
            - High trading volume (indicates liquidity)
            - Low competition (few active orders)
            - Reasonable volumes (can be traded multiple times per day)

            Calculate estimated ROI per hour based on:
            - Spread size
            - Volume availability
            - Typical fill time

            Return top 10 opportunities as JSON array.
        ";

        var opportunities = await _ollama.GenerateJsonAsync<List<StationTradingDto>>(
            prompt,
            orders);

        return ConvertAndSaveOpportunities(opportunities, "station_trading");
    }
}
```

### 2.3 Configuration
**File**: `appsettings.json`

Add new section:
```json
{
  "AI": {
    "Ollama": {
      "BaseUrl": "http://localhost:11434",
      "DefaultModel": "llama3.1:8b",
      "Timeout": 30000
    },
    "MarketAnalysis": {
      "UpdateIntervalMinutes": 15,
      "MinimumProfitPercentage": 5.0,
      "MaxOpportunitiesPerType": 10,
      "TrackedRegions": [10000002, 10000043, 10000032],
      "TrackedItemCategories": ["PLEX", "Skill Injectors", "Faction Modules"],
      "UseMapForRouteCalculation": true
    }
  }
}
```

---

## Phase 3: UI Components

### 3.1 Market Analysis Dashboard
**New File**: `Components/Pages/MarketAnalysis.razor`

Main dashboard page showing all opportunities with map integration.

### 3.2 Opportunity Cards
**New File**: `Components/Market/OpportunityCard.razor`

Reusable component for displaying individual trading opportunities.
- Show route button (links to Map page with route highlighted) ⭐

### 3.3 Trend Chart
**New File**: `Components/Market/TrendChart.razor`

Interactive price history chart with trend indicators.

---

## Phase 4: Intelligence Features

### 4.1 Smart Alerts
- Price threshold alerts (e.g., "PLEX below 3M ISK")
- Trend reversal notifications
- Undercut warnings for user's active orders
- High-confidence opportunity alerts
- **Route safety alerts** (e.g., "Safe route available") ⭐

### 4.2 Performance Tracking
- Track predicted vs. actual profits
- AI model accuracy metrics
- Portfolio ROI over time
- Best/worst performing trading strategies
- **Regional performance analysis** (which hubs are most profitable) ⭐

### 4.3 Continuous Learning
- Store opportunity outcomes
- Feedback loop: Did the AI prediction come true?
- Adjust confidence scoring based on historical accuracy
- A/B testing different Ollama models

---

## Implementation Checklist

### Week 1-2: Foundation
- [ ] Add regional market endpoints to EsiApiService
- [ ] Create new market data models
- [ ] Design and create database schema (migration)
- [ ] Implement MarketDataCollectorService (background service)
- [ ] Test data collection for Jita market
- [ ] Create basic market data viewer page (raw data display)

### Week 3-4: Ollama Integration
- [ ] Install and configure Ollama locally
- [ ] Test Ollama with simple prompts (curl/Postman)
- [ ] Implement OllamaService in C#
- [ ] Create MarketAnalysisService
- [ ] Implement arbitrage detection with AI
- [ ] **Integrate map service for route calculation** ⭐
- [ ] Test with small dataset, validate JSON parsing
- [ ] Add error handling and retry logic

### Week 5-6: Analysis Features
- [ ] Implement trend analysis with historical data
- [ ] Implement station trading analyzer
- [ ] Create TradingOpportunity database records
- [ ] Build opportunity scoring/ranking system
- [ ] Add opportunity expiration logic (background cleanup)
- [ ] Performance tracking (predicted vs actual)
- [ ] **Route security analysis integration** ⭐

### Week 7-8: UI & Polish
- [ ] Create MarketAnalysis.razor dashboard page
- [ ] Build OpportunityCard component
- [ ] Add filtering/sorting for opportunities
- [ ] Implement trend visualization (charts)
- [ ] Add watchlist functionality
- [ ] **"Show Route on Map" button integration** ⭐
- [ ] Alert system (in-app notifications)
- [ ] User settings for AI preferences (model selection, risk tolerance)

### Week 9: Testing & Optimization
- [ ] Prompt engineering (improve AI responses)
- [ ] Compare different Ollama models (llama3.1 vs mistral vs qwen)
- [ ] Optimize database queries (indexing, caching)
- [ ] Load testing (handle large market datasets)
- [ ] UI/UX refinements based on usage
- [ ] Documentation updates

---

## Architecture: AI-Powered Market Analysis with Map Integration

```
┌───────────────────────────────────────────────────────────┐
│ User Interface (Blazor)                                    │
│                                                            │
│  • MarketAnalysis.razor - Main dashboard                  │
│  • OpportunityCard.razor - Trading opportunity display    │
│    └─ [Show Route on Map] button ⭐                       │
│  • TrendChart.razor - Price history visualization         │
│  • AlertPanel.razor - Smart notifications                 │
│  • Map.razor - Route visualization (existing) ⭐          │
└───────────────────────────────────────────────────────────┘
                          ▼
┌───────────────────────────────────────────────────────────┐
│ Service Layer                                              │
│                                                            │
│  ┌─────────────────────┐  ┌──────────────────────────┐   │
│  │ MarketAnalysis      │  │ OllamaService            │   │
│  │ Service             │──│ (AI Integration)         │   │
│  │                     │  │                          │   │
│  │ • FindArbitrage ────┼──│ • GenerateAsync()        │   │
│  │ • DetectTrends      │  │ • GenerateJsonAsync()    │   │
│  │ • FindStationTrades │  │ • Model selection        │   │
│  └─────────┬───────────┘  └──────────────────────────┘   │
│            │                         │                     │
│            ▼                         ▼                     │
│  ┌─────────────────────┐  ┌──────────────────────────┐   │
│  │ MapDataService ⭐   │  │ Ollama (localhost:11434) │   │
│  │                     │  │                          │   │
│  │ • GetSystemsWithin  │  │ • llama3.1:8b            │   │
│  │   Jumps (BFS)       │  │ • mistral:7b             │   │
│  │ • Calculate route   │  │ • qwen2.5:14b            │   │
│  │   security          │  └──────────────────────────┘   │
│  └─────────────────────┘                                  │
│            ▲                                               │
│  ┌─────────────────────┐                                  │
│  │ MarketDataCollector │                                  │
│  │ Service (Background)│                                  │
│  │                     │                                  │
│  │ • Every 5 min:      │                                  │
│  │   Fetch orders      │                                  │
│  │ • Daily: History    │                                  │
│  └─────────────────────┘                                  │
└───────────────────────────────────────────────────────────┘
                          ▼
┌───────────────────────────────────────────────────────────┐
│ Data Layer (EF Core + SQLite)                             │
│                                                            │
│  • MarketSnapshot     - Real-time order book snapshots    │
│  • MarketHistory      - Historical daily statistics       │
│  • TradingOpportunity - AI opportunities + route data ⭐  │
│  • MarketTrend        - Trend analysis results            │
└───────────────────────────────────────────────────────────┘
                          ▼
┌───────────────────────────────────────────────────────────┐
│ External APIs                                              │
│                                                            │
│  • ESI: /markets/{region_id}/orders/                      │
│  • ESI: /markets/{region_id}/history/                     │
│  • ESI: /markets/prices/                                  │
│  • ESI: /universe/systems/{system_id}/kills/ ⭐           │
│  • ESI: /universe/system_jumps/ ⭐                        │
└───────────────────────────────────────────────────────────┘
```

**Note**: ⭐ indicates integration points between Map and Market Analysis features

---

## Ollama Setup Guide

### Installation
```bash
# Linux installation
curl -fsSL https://ollama.com/install.sh | sh

# Verify installation
ollama --version

# Start Ollama service
ollama serve
```

### Recommended Models
```bash
# Fast, balanced (recommended for real-time analysis)
ollama pull llama3.1:8b

# Alternative: Good performance
ollama pull mistral:7b

# Better reasoning (slower, more resource intensive)
ollama pull qwen2.5:14b

# Test a model
ollama run llama3.1:8b
>>> Analyze this EVE Online market data: Buy orders at 100 ISK, Sell orders at 120 ISK...
```

### Testing Ollama from CLI
```bash
# Test generation endpoint
curl http://localhost:11434/api/generate -d '{
  "model": "llama3.1:8b",
  "prompt": "Explain arbitrage trading in EVE Online in 2 sentences.",
  "stream": false
}'

# Expected response:
# {
#   "model": "llama3.1:8b",
#   "response": "Arbitrage trading in EVE Online involves...",
#   ...
# }
```

### Monitoring Ollama
```bash
# Check running models
ollama list

# View logs
journalctl -u ollama -f

# Resource usage
htop  # Look for 'ollama' process
```

---

## Expected Outcomes

### Short-Term (Week 1-4)
- Working data collection from ESI
- Basic Ollama integration with test prompts
- Database storing market snapshots
- Proof-of-concept: AI detects 1-2 arbitrage opportunities
- **Route calculation for arbitrage opportunities** ⭐

### Mid-Term (Week 5-8)
- Functional market analysis dashboard
- Real-time opportunity detection (arbitrage, station trading)
- Trend analysis with historical data
- 10-20 tracked items across major hubs
- **Map integration: "Show Route on Map" functionality** ⭐

### Long-Term (Week 9+)
- Full-featured AI trading assistant
- Performance tracking (predicted vs actual profits)
- Multi-model comparison (which AI is most accurate?)
- Portfolio optimization suggestions
- **Route safety scoring** (avoid lowsec/nullsec routes) ⭐
- Automated alerts via Discord/Telegram (future enhancement)

---

## Success Metrics

### Technical Metrics
- **Data Collection**: Successfully fetch and store market data for 100+ items
- **AI Response Time**: <5 seconds for analysis requests
- **Database Size**: <500MB for 30 days of market history
- **Cache Hit Rate**: >70% for ESI calls (via ETag)
- **Map Integration**: Route calculation <1 second for any two systems ⭐

### Business Metrics
- **Opportunity Detection Rate**: 5-10 high-confidence opportunities per hour
- **Prediction Accuracy**: >60% for 7-day trend predictions
- **User Engagement**: Daily active usage of market analysis features
- **Profitability**: Users report actual profits from AI suggestions
- **Route Optimization**: AI suggests safer/shorter routes 80% of the time ⭐

---

## Risk Mitigation

### Technical Risks
- **Ollama Availability**: Handle service downtime gracefully (fallback to heuristics)
- **ESI Rate Limits**: Respect error budget, implement exponential backoff
- **Database Growth**: Regular cleanup of old snapshots, efficient indexing
- **AI Hallucinations**: Validate AI responses, cap confidence scores, manual override
- **Map Service Dependency**: Cache route calculations, handle service unavailability ⭐

### EVE Online Market Risks
- **Market Manipulation**: AI detects but warns users, doesn't encourage manipulation
- **CCP Policy Changes**: Monitor EVE Online TOS, ensure compliance
- **Market Crashes**: AI should warn about high-risk opportunities
- **Scams**: Filter out known scam items (e.g., "exotic dancers")
- **Route Safety**: Warn about lowsec/nullsec routes, calculate risk scores ⭐

---

## Recent Development Activity

### Git Commit History (Latest)
```
f3ffa49 - Update README.md
d26385d - Merge pull request #1
bbff8be - Exception Handling
e5b5630 - ETag Caching durch ESI verwenden (ETag caching via ESI)
34979dc - Cleanup

Map-related commits:
c98029a - Erweiterungen (Extensions)
b848e52 - Verbindungen zu anderen Regionen in Systemansicht. Versuch 1
ef296f2 - aktueller Stand Map...
9d078ac - aktueller stand (current state)
760d866 - Aktueller Stand, hauptsächlich Backend
```

### Current Branch
- **Branch**: `master`
- **Uncommitted**: `CONTRIBUTING.md`
- **Feature Branch**: `feature/MarktAnalyseAI` (market analysis planning)

---

## Notes for Future AI Assistants

### Map Feature (Complete)
- Fully functional 3-view map system
- Live character tracking working
- ESI statistics integration complete
- Cross-region connections implemented
- Route calculation service exists as **stub only** (planned future feature)
- Can be leveraged for market analysis (distance, security, routing)

### Market Analysis Feature (✅ IMPLEMENTED!)
- ✅ Basic AI-powered market analysis complete
- ✅ Ollama integration working (llama3.1:latest)
- ✅ Automatic data collection every 5 minutes
- ✅ Trading opportunities with confidence scoring
- 🚧 **Next Steps**:
  - Enhanced AI prompts for deeper analysis
  - Full arbitrage routing with Map integration
  - Transaction linking enhancement with ML
  - Sentiment analysis from EVE forums/Reddit (advanced)
  - Integration with zKillboard for war-related price spikes
  - Multi-character portfolio analysis

### Integration Opportunities
- **Map + Market**: Calculate arbitrage profitability including travel time/risk
- **Map + Wallet**: Analyze trading patterns by region
- **AI + Map**: Suggest optimal trade routes based on character location
- **AI + Wallet**: Detect profitable trading patterns from transaction history

---

## 🚧 Phase 5: Advanced UI & Location-Based Filtering (IN ARBEIT)

> **Status**: In Entwicklung (Dezember 2025)
> **Ziel**: Market Data Visualisierung + Location-basierte Trading Filters mit Map-Integration

### 5.1: Market Page Restructuring ✅

**Tab-System implementiert**:
- ✅ Market.razor umgebaut zu Tab-basiertem Layout
- ✅ **Tab 1: "Meine Orders"** - Character Market Orders (bestehend)
- ✅ **Tab 2: "Marktdaten"** - MarketSnapshot Visualisierung (NEU)

**MarketDataView Komponente** (`Components/Market/MarketDataView.razor`):
- ✅ **Statistik-Übersicht**:
  - Total Snapshots, getrackte Items, Regionen, Datenbereich
- ✅ **Filter-Controls**:
  - Item-Auswahl (Dropdown mit SDE-Namen)
  - Region-Auswahl (Alle oder spezifische Region)
  - Zeitraum-Filter (24h / 3 Tage / 7 Tage)
- ✅ **Aktueller Marktstatus**:
  - Single-Region View: Buy/Sell Preise, Spread, Volumes
  - Multi-Region View: Regionen-Vergleich
- ✅ **Snapshot-Tabelle**:
  - Neueste 50 Datenpunkte
  - Zeitstempel, Preise, Spreads, Volumes
- 🚧 **Chart Placeholder**: Preisverlauf (für Chart.js Integration vorbereitet)

**MarketDataService** (`Services/Market/MarketDataService.cs`):
- ✅ `GetTrackedTypeIdsAsync()` - Liste aller getrackten Items
- ✅ `GetTrackedRegionIdsAsync()` - Liste aller getrackten Regionen
- ✅ `GetMarketSnapshotsAsync()` - Snapshots nach Item/Region/Zeitraum
- ✅ `GetLatestSnapshotAsync()` - Aktuellster Snapshot für Item+Region
- ✅ `GetMarketDataStatisticsAsync()` - Statistiken mit SDE-Namen

### 5.2: Location-Based Trading Filters ✅

**Trading Page Filter-Erweiterung**:
- ✅ **4 Location-Filter-Modi**:
  1. **Alle Stationen** - Keine Filterung (default)
  2. **Aktuelle Station** - Filter: `BuyLocationId` oder `SellLocationId` == Character Location
  3. **Aktuelles System** - Filter: `BuySystemId` oder `SellSystemId` == Character System
  4. **X Sprünge entfernt** - Filter: Systems innerhalb 1-20 Jumps (BFS via Map Service)

**Character Location Detection**:
- ✅ `LoadCurrentLocationAsync()` - Lädt Position beim Page-Load
- ✅ ESI Integration: `GetLocationAsync(characterId)`
- ✅ SDE Integration: Station- und System-Namen aus mapDenormalize/mapSolarSystems
- ✅ UI zeigt aktuelle Position: "📌 Jita IV - Moon 4 (Jita)"

**Map Service Integration** ⭐:
- ✅ `GetSystemsWithinJumpsAsync()` - BFS-Algorithmus für Jump-Distance
- ✅ `_systemsWithinJumps` HashSet für schnelle Lookups
- ✅ `PassesLocationFilter()` - Filter-Logik für alle Modi
- ✅ Reactive Updates via `@bind:after` - Filter werden sofort angewandt

**Filter-Logik** (`PassesLocationFilter()`):
```csharp
case "current":  // BuyLocationId == _currentLocationId OR SellLocationId == _currentLocationId
case "system":   // BuySystemId == _currentSystemId OR SellSystemId == _currentSystemId
case "jumps":    // _systemsWithinJumps.Contains(BuySystemId) OR .Contains(SellSystemId)
```

### 5.3: UI/UX Improvements ✅

**Filter Container Design**:
- ✅ Zweireihiges Layout: Basic Filters (Zeile 1) + Location Filters (Zeile 2)
- ✅ Trennlinie zwischen Filter-Sektionen
- ✅ Card-Style mit Background + Border
- ✅ Responsive Layout mit Flex-Wrap

**Location Info Display**:
- ✅ `current-location-info` Box mit Primary-Color Border
- ✅ System-Name in Klammern (Secondary Color)
- ✅ Loading-Indicator während Location-Fetch

**CSS Enhancements**:
- ✅ `.filters-container` - Hauptcontainer mit Card-Styling
- ✅ `.filters-row` - Flex-Layout für Filter-Elemente
- ✅ `.location-filters` - Border-Top Trennlinie
- ✅ `.current-location-info` - Highlighted Location Display

### Database Schema (Unchanged)

Nutzt bestehende Felder:
- `TradingOpportunity.BuyLocationId` / `SellLocationId` (Phase 4)
- `TradingOpportunity.BuySystemId` / `SellSystemId` (Phase 4)
- `MarketSnapshot` Tabelle (Phase 1)

### Technical Implementation

**Dependencies**:
- `IEsiApiService` - Character Location
- `ISdeUniverseService` - Location/System Names
- `IMapDataService` - Jump Distance Calculation ⭐
- `IMarketDataService` - MarketSnapshot Queries (NEU)

**Performance Optimizations**:
- Dictionary Caching für Location Names (bestehend)
- HashSet für System-Jump Lookups (`_systemsWithinJumps`)
- Lazy Loading: Jump Calculation nur bei Filter-Auswahl "jumps"

### Known Limitations

- ❌ Charts noch nicht implementiert (Placeholder vorhanden)
- ⚠️ Character Location wird nur beim Page-Load geladen (kein Auto-Refresh)
- ⚠️ Jump-Distance Filter nutzt BFS, nicht die geplante Route-Calculation API
- ℹ️ Filter funktionieren nur wenn BuySystemId/SellSystemId in TradingOpportunities gesetzt sind

### Next Steps for Phase 5

1. **Chart Integration** (Chart.js oder ähnlich):
   - Preisverlauf-Diagramm (Buy/Sell über Zeit)
   - Spread-Trend-Visualisierung
   - Volumen-Diagramm
2. **Advanced Filters**:
   - Region-Multi-Select für Trading Page
   - Item-Type Filter für Trading Opportunities
3. **Performance**:
   - Pagination für MarketSnapshot-Tabelle (>50 Einträge)
   - Virtual Scrolling für große Datensätze
4. **Auto-Refresh**:
   - Character Location alle 60 Sekunden aktualisieren
   - MarketData Live-Updates (SignalR?)

---

## 🚧 Phase 6: Market Finder mit 3-Level Hierarchie ✅

> **Status**: ✅ **COMPLETE** (Dezember 2025)
> **Ziel**: Interaktive Marktsuche mit hierarchischer Darstellung und Jump-Distanz Integration

### 6.1: Market Finder Komponente ✅

**File**: `Components/Market/MarketFinderView.razor` (~1000 Zeilen)

**Features**:
- ✅ **Item-Suche mit Autocomplete**:
  - Lädt alle handelbaren Items aus SDE (`GetAllMarketItemsAsync`)
  - Live-Filtering bei Eingabe (zeigt Top 10 Matches)
  - Autocomplete-Dropdown mit Hover-Effekten

- ✅ **3 Location-Modi**:
  1. **Position Mode**: Aktuelle Character-Position + Max Jumps (0-20)
  2. **System Mode**: Manuelles System + Max Jumps (mit System-Autocomplete)
  3. **Region Mode**: Ganze Region auswählen (Dropdown)

- ✅ **Result Limit**: Konfigurierbarer Slider (5-50 Orders)

**3-Stufige Hierarchie**:
```
Level 1: Order Type (Buy/Sell)
  ├─ Level 2: Systems (sortiert nach Jump-Distanz)
  │   ├─ Level 3: Stations
  │   │   ├─ Main Orders (gleicher Typ wie Level 1)
  │   │   └─ Other Orders (entgegengesetzter Typ, zugeklappt)
```

**Zuklapbare UI**:
- ✅ Alle 3 Ebenen einzeln auf-/zuklappbar
- ✅ Pfeil-Icons (▶/▼) zeigen Expand/Collapse-Status
- ✅ "Andere Orders" standardmäßig zugeklappt (gestrichelter Border)
- ✅ Smooth Transitions beim Auf-/Zuklappen

### 6.2: Backend-Logik ✅

**Datenstruktur** (Helper Classes):
```csharp
OrderTypeGroup {
  bool IsBuyGroup
  List<SystemGroup> Systems
  int TotalOrders
}

SystemGroup {
  int SystemId
  string SystemName
  int JumpDistance
  List<StationGroup> Stations
  double? BestPrice
}

StationGroup {
  long LocationId
  string LocationName
  List<RegionalMarketOrder> MainOrders
  List<RegionalMarketOrder> OtherOrders  // Entgegengesetzter Order-Typ
  double? BestMainPrice
}
```

**BuildOrderTypeGroup Algorithmus**:
1. Gruppiere Orders nach SystemId
2. Für jedes System: Gruppiere nach LocationId
3. Für jede Station:
   - MainOrders = Orders des gleichen Typs wie Haupt-Gruppe
   - OtherOrders = Orders aus _buyOrders/_sellOrders (entgegengesetzt)
4. Sortiere Systeme nach JumpDistance (aufsteigend)
5. Sortiere Stationen alphabetisch

**Jump-Distanz Berechnung** ⭐:
- ✅ `GetJumpDistanceValue()` - Nutzt `_systemsWithinJumps` HashSet
- ✅ `FormatJumpDistance()` - Formatiert als String ("0 jumps", "≤ 5 jumps", "Out of range")
- ✅ BFS-Integration via `MapDataService.GetSystemsWithinJumpsAsync()`

### 6.3: CSS Styling ✅

**File**: `wwwroot/css/wallet.css` (~270 Zeilen neue Styles)

**Hierarchie-Abstufung**:
- ✅ `.order-type-group` - Level 1: 2px border, Box-Shadow, Gradient-Header
- ✅ `.system-group` - Level 2: 1px border, dunklerer Hintergrund
- ✅ `.station-group` - Level 3: 1px border, noch dunklerer Hintergrund
- ✅ `.order-row` - Individuelle Orders: 3px farbiger left-border

**Farbcodierung**:
- ✅ Buy Orders: Grün (`#10b981`)
- ✅ Sell Orders: Orange (`#f59e0b`)
- ✅ Jump Badge: Blau (`#4a9eff`)

**Hover-Effekte**:
- ✅ Alle klickbaren Header haben `background` Transitions
- ✅ Order-Rows highlighten bei Hover
- ✅ "Other Orders"-Toggle hebt sich deutlich ab (gestrichelt → solid)

**Responsive Design**:
- ✅ Mobile: System-Header flex-wrap für Jump-Badge
- ✅ Desktop: 2-spaltige Order-Listen (Buy/Sell nebeneinander)

### 6.4: SDE Integration ✅

**Neue SDE-Methoden** (`SdeUniverseService.cs`):
```csharp
Task<Dictionary<int, string>> GetAllMarketItemsAsync()
  - Query: SELECT typeID, typeName FROM invTypes WHERE marketGroupID IS NOT NULL AND published = 1
  - ~15,000+ handelbare Items

Task<Dictionary<int, string>> GetAllRegionsAsync()
  - Query: SELECT regionID, regionName FROM mapRegions
  - Alle EVE-Regionen

Task<Dictionary<int, string>> SearchSolarSystemsAsync(string searchQuery, int maxResults = 10)
  - Query: LIKE-Suche in mapSolarSystems
  - Für System-Autocomplete
```

**Interface-Update** (`ISdeUniverseService.cs`):
- ✅ 3 neue Methoden-Signaturen hinzugefügt

### 6.5: Market.razor Tab-Integration ✅

**Tab-System**:
- ✅ Tab 1: "📋 Meine Orders" (bestehend)
- ✅ Tab 2: "📈 Marktdaten" (Phase 5)
- ✅ Tab 3: "🔍 Market Finder" (NEU - Phase 6)

**Integration**:
```razor
@if (_activeTab == "finder")
{
    <WALLEve.Components.Market.MarketFinderView />
}
```

### 6.6: Technische Details ✅

**State Management**:
- ✅ `_buyOrdersExpanded`, `_sellOrdersExpanded` - Level 1 (Order Type)
- ✅ `_expandedSystems` (HashSet<int>) - Level 2 (Systeme)
- ✅ `_expandedStations` (HashSet<long>) - Level 3 (Stationen)
- ✅ `_expandedOtherOrderTypes` (HashSet<string>) - "Andere Orders" Toggle
  - Key-Format: `"{locationId}_{orderType}"` (z.B. "60003760_sell")

**Toggle-Methoden**:
```csharp
void ToggleOrderType(bool isBuy)
void ToggleSystem(int systemId)
void ToggleStation(long locationId)
void ToggleOtherOrderType(long locationId, bool isBuyGroup)
```

**Performance-Optimierungen**:
- ✅ Dictionary-Caching für Item/Location/System-Namen
- ✅ HashSet für schnelle Contains-Lookups (Jump-Distanz)
- ✅ Lazy Loading: Orders nur laden wenn Item ausgewählt
- ✅ BFS-Berechnung nur bei Position/System-Modus

### 6.7: User Experience ✅

**Default State**:
- ✅ Buy Orders: Aufgeklappt
- ✅ Sell Orders: Aufgeklappt
- ✅ Systeme: **Zugeklappt**
- ✅ Stationen: **Zugeklappt**
- ✅ "Andere Orders": **Zugeklappt**

**Visuelle Hierarchie**:
1. Order-Type hat dicksten Border (2px) + Gradient
2. Systeme haben mittleren Border (1px) + Best Price Badge
3. Stationen haben dünnen Border + Station Price
4. Orders haben left-border (3px) für Buy/Sell

**Sortierung**:
- ✅ Systeme: Jump-Distanz (aufsteigend), dann Name
- ✅ Stationen: Alphabetisch
- ✅ Orders: Preis (Buy: absteigend, Sell: aufsteigend)

### Testing Checklist ✅

- ✅ Item-Autocomplete funktioniert
- ✅ Region-Dropdown lädt Regionen aus SDE
- ✅ System-Autocomplete zeigt Matching-Systeme
- ✅ Position-Mode lädt Character-Location
- ✅ Jump-Distance wird korrekt berechnet (BFS)
- ✅ Orders werden korrekt gruppiert (Buy/Sell getrennt)
- ✅ Systeme sortiert nach Jump-Distanz
- ✅ Alle Toggle-Funktionen arbeiten
- ✅ "Andere Orders" standardmäßig zugeklappt
- ✅ CSS Hierarchie klar erkennbar
- ✅ Responsive Layout (Mobile/Desktop)
- ✅ Hover-Effekte funktionieren
- ✅ Build erfolgreich (0 Warnungen, 0 Fehler)

### Bekannte Limitierungen

- ⚠️ Jump-Distance ist Approximation (BFS innerhalb MaxJumps, nicht exakte Distanz)
- ⚠️ "Out of range" Systeme werden mit 999 sortiert (immer am Ende)
- ℹ️ Character-Position wird nur beim Page-Load geladen (kein Auto-Refresh)
- ℹ️ Keine Preis-Trends oder Charts (zukünftige Erweiterung)

### Next Steps (Zukünftige Erweiterungen)

1. **Exakte Jump-Distance**: Dijkstra-Pathfinding via `RouteCalculationService`
2. **Price History Charts**: Integration mit MarketSnapshots für Trends
3. **Profit Calculator**: Automatische Berechnung inkl. Steuern/Broker-Fees
4. **Watchlist**: Favoriten-System für häufig gesuchte Items
5. **Quick Actions**: "Set Destination" (In-Game Link), "Set Buy Order", etc.
6. **Map Integration**: "Show on Map"-Button für Systems

---

**Last Updated**: 2025-12-31
**Project Version**: Alpha
- **Map Feature**: Complete ✅
- **AI Trading Feature**: Core Implementation Complete ✅
  - Phase 1: Data Collection ✅
  - Phase 2: Ollama Integration ✅
  - Phase 3: UI Dashboard ✅
  - Phase 4: UI Enhancements & Code Quality ✅
    - Station/Location integration
    - Sorting and filtering
    - XML documentation
    - Error handling improvements
  - Phase 5: Advanced UI & Location-Based Filtering ✅ (COMPLETE)
    - Market Data Visualisierung mit Tabs
    - Location-based Trading Filters
    - Map Service Integration für Jump-Distance
  - Phase 6: Market Finder mit 3-Level Hierarchie ✅ (COMPLETE)
    - Interaktive Marktsuche mit Item-Autocomplete
    - 3-stufige zuklapbare Hierarchie (Order Type → Systems → Stations)
    - Jump-Distanz Sortierung und BFS-Integration
    - Visuelle Hierarchie mit abgestuften Boxen
- **Status**: Production-ready for market analysis and trading

**Recent Improvements (December 2025)**:
- ✅ **Market Finder**: 3-stufige hierarchische Marktsuche (Phase 6)
  - Item-Autocomplete mit allen handelbaren Items aus SDE
  - 3 Location-Modi: Position, System, Region
  - Jump-Distanz Sortierung mit BFS-Integration
  - Zuklapbare Hierarchie: Order Type → Systems → Stations
  - "Andere Orders" in jeder Station (zugeklappt)
  - Visuelle Hierarchie mit abgestuften Boxen und Farbcodierung
- ✅ **Market Page Tabs**: "Meine Orders" + "Marktdaten" + "Market Finder"
- ✅ **MarketDataView Component**: Statistiken, Filter, Snapshot-Tabelle
- ✅ **MarketDataService**: Query-API für MarketSnapshots
- ✅ **Location-Based Filters**: 4 Modi (Alle/Station/System/Jumps)
- ✅ **Character Location Detection**: Live Position-Tracking
- ✅ **Map Integration**: Jump-Distance Calculation via BFS
- ✅ **SDE Erweiterungen**: GetAllMarketItemsAsync, GetAllRegionsAsync, SearchSolarSystemsAsync
- ✅ Station names displayed in trading opportunities (via SDE integration)
- ✅ Sorting by Confidence, Profit, or Timestamp
- ✅ Filtering by minimum confidence and profit thresholds
- ✅ Comprehensive XML documentation for AI services
- ✅ Enhanced error handling with user-friendly error banners
- ✅ Dictionary caching for performance optimization
- ✅ Location tracking throughout the data pipeline

**Next Major Milestones**:
1. **Chart Integration** - Preisverlauf/Spread-Visualisierung (Chart.js)
2. **Enhanced AI prompting** - Bessere Opportunity Detection mit LLM
3. **Full arbitrage routing** - Route calculation mit Profit-Analyse
4. **Real-time alerts** - Notifications für neue Opportunities
5. **Multi-character portfolio** - Tracking über mehrere Characters
6. **Route safety analysis** - Highsec/Lowsec/Nullsec Risk-Bewertung

---

## Cost Basis System (NEU — September 2026)

Ziel: Jedes Bestands-Item bekommt einen Einkaufspreis (Cost Basis), damit
ROI/Gewinn/Opportunity-Score belastbar sind. Ermittlung in einer Fallback-Kette,
als persistierte Hintergrund-Jobs.

### Warum lokal spiegeln?
ESI liefert Wallet-Transaktionen nur **~30 Tage / 10.000 Einträge** zurück
(esi-issues #1172, EVE-Forum). Ohne Spiegelung wäre die Cost Basis für ältere
Käufe dauerhaft unbekannt. Der tägliche **Transaktions-Sink** kopiert alle
Seiten (dedup per TransactionId) in `WalletTransactionRecords` — die lokale
Historie wächst damit über das ESI-Fenster hinaus.

### Datenfluss
```
ESI Transactions ──► WalletTransactionRecords (Sink, täglich)
                          │
                          ▼
              CostBasisEntries (Deduction, nach Marktwert)
              ├── Source=Transaction  (echter Kaufpreis, final)
              ├── Source=Estimate     (Schätzung, Vorschlag)
              └── Source=Manual       (Nutzer, gewinnt immer)
                          │
                          ▼
              InventoryService liest Cost Basis aus DB (kein ESI-Call mehr)
```

### Services
- **`CostBasisCollectorService`** (BackgroundService, 60s-Loop):
  - `CostBasisSink`-Job: täglich Transaktionen spiegeln (24h-Intervall)
  - `CostBasisDeduction`-Job: nur Items mit vorhandener Kauf-Transaktion,
    die noch keinen Eintrag haben → neuer Job nur wenn matchbare offen sind
    (verhindert Job-Spam für unmatchbare Items)
  - `CostBasisEstimate`-Job: vom Nutzer angestoßen, Parameter
    `{"typeIds":[...],"regionId":N}`; Quelle: letzter MarketHistory-Durchschnitt
    → Fallback ESI adjusted_price
  - Auto-Resume: Jobs werden beim Start als `Interrupted` markiert und beim
    nächsten Loop an `Current` fortgesetzt (Arbeitsliste im `ParametersJson`)
- **`CostBasisService`** (scoped, UI-facing): Übersicht, Schätz-Job anstoßen,
  manueller Wert, Reset, Default-Region (AppSettings-Key `CostBasis.DefaultRegionId`,
  Fallback Jita 10000002)
- **`BackgroundJobManager`** + `IBackgroundJobManager`: persistierter Job-Zustand
  (Status, Current/Total, Fehler) für die Settings-Übersicht

### UI
- **`/costbasis`** (`Components/Pages/CostBasis.razor`): Tabelle mit Sort/Filter/
  Multiselektion, Schätzen mit Markt-Dropdown, Wert-Modal (Preis + Kaufdatum),
  Zurücksetzen, aktiver Job-Banner mit Pause/Resume
- **Settings** (`Components/Pages/Settings.razor`): "Cost Basis & Hintergrund-Tasks" —
  Jobs-Tabelle (Status, Fortschritt, Fehler, Pause/Resume/Neu) + Standard-Schätzmarkt
- **Bestand-Tab**: Cost-Basis-Zeile mit Quellen-Label (Echt/Geschätzt/Manuell) + Link
- **NavMenu**: Neuer Eintrag "🧾 Einkaufspreise"

### Migrationen
- `AddCostBasisAndBackgroundJobs` (2026-09-06): WalletTransactionRecords,
  CostBasisEntries, BackgroundJobs
- `AddAppSettings` (2026-09-06): AppSettings (Key-Value)

### ESI-Compliance
- Transaktions-Paging mit Semaphore (max 4 parallel) + 250ms Staffelung
- Fehler vermeiden (5 Tokens/Fehler, 100 Fehler/Min → 420 auf alles):
  Estimate-Jobs schätzen History-Fälle ohne ESI, adjusted_price nur einmal abrufen
- 429/420: `EsiRateLimitException` + Retry-After (bestehendes Handling)