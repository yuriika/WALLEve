# WALL-EVE Development Guide

> Last reviewed against `dev` at `c89a983` on 2026-09-11.
>
> This document describes the current implementation and its engineering rules. It is not a roadmap or a changelog. GitHub milestones and issues are the authoritative implementation backlog.

## 1. Engineering rules

### Tests before commits and user tests

Run the test suite before every commit and before handing the app to the user for a manual test:

```bash
dotnet test tests/WALLEve.Tests/WALLEve.Tests.csproj --no-restore
```

For every feature change, decide whether tests must be added or changed:

- calculations, state transitions, filters, fallback chains, persistence, and sync behavior require tests;
- pure layout/CSS changes normally do not, but the omission must be deliberate;
- bug fixes require a regression test unless the behavior can only be verified by Razor compilation or a focused manual smoke test.

### Log review

After application tests, review application `warn`, `fail`, and `crit` entries. EF SQL debug output and routine `HttpClient` debug entries are not failures. ESI 420/429/5xx responses, unhandled exceptions, and repeated warnings must become a fix or tracked issue.

### Data-integrity rules

- Never turn an ESI failure into an apparently valid empty result.
- Never persist a partial multi-page response as a complete snapshot.
- Keep the previous complete snapshot when a refresh fails and mark it stale.
- Preserve owner, item, location, container, source, freshness, and completeness until a named read model deliberately aggregates them.
- Unknown values remain unknown; reference prices are not executable market quotes.
- Charge each fee once and expose every assumption used by a financial calculation.
- All normal database changes use additive EF Core migrations. Existing `wallet.db` data must survive upgrades.

### Trading/AI boundary

Trading calculations are deterministic. The existing `IOllamaService` is legacy/optional infrastructure and must not be required to calculate prices, fees, profit, ranking, or recommendations. A future provider-neutral LLM may explain an already-computed result, but may not invent or alter economic inputs.

## 2. Product direction

WALL-EVE is a local economic cockpit for EVE Online:

1. trustworthy holdings;
2. stockpiles and shopping lists;
3. auditable inventory, order, station, and route-trading decisions;
4. portfolio history;
5. personal mining analysis;
6. character industry planning.

The interface serves beginners and advanced users through progressive disclosure: show a clear action first, then make the complete calculation, evidence, source age, and uncertainty inspectable.

The detailed internal comparison and accepted design live under the ignored `.brainstorming/` directory. Actionable work is tracked at:

- Issues: https://github.com/yuriika/WALLEve/issues
- Milestones: https://github.com/yuriika/WALLEve/milestones

## 3. Technology and runtime

- .NET 10 / C# 14
- Blazor Interactive Server
- Entity Framework Core 10 with SQLite
- EVE Online OAuth 2.0 PKCE
- EVE ESI through named `HttpClient` instances
- EVE SDE through a separate SQLite database
- Cytoscape.js for the universe map
- xUnit with in-memory SQLite fixtures

Default local URL: `http://localhost:5080`

Runtime data is resolved from `Environment.SpecialFolder.LocalApplicationData`. On macOS, the app preserves compatibility with an existing database under:

```text
~/Library/Application Support/WALLEve/Data/
```

The main files are:

- `wallet.db` — application state and economic data;
- `sde.sqlite` — static universe/type data;
- `auth.dat` — encrypted multi-character token store.

Do not inspect, print, or commit runtime credentials.

## 4. Current user-facing implementation

### Authentication and characters

- OAuth 2.0 PKCE flow.
- Encrypted token storage through ASP.NET Core Data Protection.
- Multiple remembered characters and active-character switching.
- Character overview, location, online state, current ship, skills, and wallet balance where scopes permit.

Key files:

- `Services/Authentication/EveAuthenticationService.cs`
- `Services/Authentication/TokenStorageService.cs`
- `Services/Authentication/AuthStoreFile.cs`
- `Components/Shared/AccountSwitcher.razor`
- `Components/Pages/Character.razor`

### Wallet

- Wallet journal and transaction display.
- Tax, escrow, order, and transaction linking.
- Persistent manual verification/rejection of links.
- Local transaction mirror used by cost-basis collection.

Key files:

- `Services/Wallet/WalletService.cs`
- `Services/Wallet/WalletLinkService.cs`
- `Components/Pages/Wallet.razor`
- `Components/Wallet/`

### Market and inventory

- Active and historic character orders.
- Regional market orders and market history.
- Market snapshots for tracked/favorite inventory types.
- Market finder by item, position/system/region, and jump radius.
- Inventory list with current cost-basis and valuation fields.
- Order-book position and price-change simulation.
- Deterministic inventory-sell opportunities and sell simulation.

Key files:

- `Services/Market/MarketDataCollectorService.cs`
- `Services/Market/MarketDataService.cs`
- `Services/Market/InventoryService.cs`
- `Services/Market/OrderIntelligenceService.cs`
- `Services/Market/FeeCalculatorService.cs`
- `Services/Market/MarketAnalysisService.cs`
- `Components/Pages/Market.razor`
- `Components/Pages/Trading.razor`
- `Components/Pages/CostBasis.razor`
- `Components/Market/`

These features are alpha quality. The correctness blockers in section 9 must be fixed before treating trading recommendations as authoritative.

### Background jobs and sync controls

- Persistent job progress/status.
- Pause/resume/restart support for user-started work.
- Sync overview on the character page.
- Manual trigger flags and `SyncWakeService` push wake-up, avoiding a blind 60-second wait.
- Inventory cache warm-up at startup.

Key files:

- `Services/Market/BackgroundJobManager.cs`
- `Services/Market/CostBasisCollectorService.cs`
- `Services/Market/SyncOverviewService.cs`
- `Services/Market/SyncTriggerService.cs`
- `Services/Market/SyncWakeService.cs`
- `Services/Market/InventoryWarmupService.cs`

### Map

- Region, system, and local-environment views.
- Character-position tracking.
- SDE stargate graph and systems-within-N-jumps BFS.
- ESI system jumps and kill statistics.
- Security/activity visualization.

`RouteCalculationService` is still a stub. A systems-within-radius result is not an exact route and must not be presented as one.

Key files:

- `Services/Map/MapDataService.cs`
- `Services/Map/MapStatisticsService.cs`
- `Services/Map/RouteCalculationService.cs`
- `Components/Pages/Map.razor`
- `Components/Map/`
- `wwwroot/js/cytoscape-map.js`

## 5. Architecture

```text
Blazor components
    │
    ├── Authentication services ── EVE SSO / encrypted auth.dat
    ├── ESI services ───────────── ESI + in-memory ETag cache
    ├── Wallet services ────────── wallet.db
    ├── Market services ────────── wallet.db + ESI + SDE
    ├── Map services ───────────── SDE graph + ESI statistics
    └── SDE services ───────────── sde.sqlite

Hosted services
    ├── MarketDataCollectorService
    ├── CostBasisCollectorService
    └── InventoryWarmupService
```

### Dependency injection

Registrations live in `Program.cs`:

- interfaces and scoped business services;
- singleton token/ESI-cache/SDE/map state where state is intentionally shared;
- `WalletDbContext` as scoped EF Core context;
- hosted collectors that create scopes for scoped dependencies;
- named clients `EveApi`, `SdeDownload`, and legacy optional `Ollama`.

Follow the existing interface-based constructor-injection pattern. Do not instantiate `HttpClient` or EF contexts ad hoc.

## 6. Data stores

### `wallet.db`

`Data/WalletDbContext.cs` currently maps:

| Entity | Purpose |
|---|---|
| `WalletCharacter`, `WalletCorporation` | synced owner metadata |
| `WalletEntryLink` | journal/transaction/order links and manual decisions |
| `WalletTransactionRecord` | local character transaction mirror |
| `CostBasisEntry` | one current basis record per character/type |
| `MarketSnapshot` | tracked region/type market snapshots |
| `MarketHistory` | daily region/type market history |
| `TradingOpportunity` | current persisted opportunity records |
| `MarketTrend` | persisted trend records |
| `MarketFavorit` | character item favorites |
| `BackgroundJob` | resumable job state |
| `AppSetting` | persistent key/value settings |

Startup calls `Database.MigrateAsync()`. New entities and index changes require a migration and a migration test against a representative copy of the current schema.

### `sde.sqlite`

`Services/Sde/SdeDbContext.cs` and the SDE services provide item names, market groups, systems, regions, constellations, stations, coordinates, and stargate connections. The user downloads/updates the Fuzzwork SQLite conversion from Settings.

Do not add application-owned mutable state to the SDE database.

## 7. ESI integration

Local ESI documentation:

- `.esi-docs/`
- `.esi-docs/openapi.json`
- https://developers.eveonline.com/

Important current integrations include:

- character public data, location, ship, online state, and skills;
- character wallet balance, journal, and transactions;
- character active and historic market orders;
- character assets;
- regional orders, market history, and reference prices;
- public systems, stations, jumps, and kills;
- authenticated structures where accessible.

### Request rules

- Use `IEsiApiService` and the named `EveApi` client.
- Send the configured User-Agent.
- Honor ETag/304 and cache-expiry headers.
- Bound pagination concurrency and honor cancellation.
- Honor ESI error-limit and rate-limit headers, including `Retry-After`.
- Treat 401/403, 404, 420/429, 5xx, cancellation, and malformed payloads distinctly.
- Do not retry permanent authorization/scope errors as transient failures.

The current API does not yet expose completeness/freshness consistently to every consumer. This is a blocking backlog item.

## 8. Sync-job registry

Every user-visible background sync must have:

1. one stable `JobType` constant in its executor;
2. one definition in `SyncOverviewService.Definitions`;
3. documentation in this table;
4. deterministic resume/cancel behavior covered by tests.

| Job type | Purpose | Scope | Schedule/trigger |
|---|---|---|---|
| `CostBasisSink` | mirror ESI wallet transactions locally | ESI transaction window, deduplicated by transaction ID | automatic daily; manually forceable |
| `CostBasisDeduction` | derive transaction-backed basis for matchable types | mirrored character purchases | automatic when needed |
| `CostBasisEstimate` | create visibly estimated fallback prices | selected type IDs and region | manual from `/costbasis` |
| `InventoryScan` | estimate currently open types, then analyze inventory | whole current character inventory | manual from `/trading` |
| `IndustryJobsSync` | mirror active/historical character industry jobs | ESI industry-jobs window (~90 days), deduplicated by job ID | automatic daily; manually forceable |

`SyncTriggerService` stores one-shot force flags in `AppSettings`. `SyncWakeService` wakes the collector immediately; the Blazor circuit refreshes progress without browser polling.

## 9. Known correctness blockers

These are tracked in milestone **M0 — Data Trust** and block new product features:

1. `OrderBookView` is not resolved as a Razor component (`RZ10012`).
2. Stored purchase basis is charged a buy fee again in trading analysis.
3. Paginated ESI errors/partial responses are not represented safely to consumers.
4. Inventory aggregation by `TypeId` loses locations and container identity.
5. Owned stack quantity is incorrectly used as market liquidity.
6. Latest market snapshots may be applied without asset-location relevance.
7. `adjusted_price * 0.95` is presented as a buy fallback despite not being an executable quote.
8. Buy-order range is not fully included in competition/position calculations.
9. Deterministic analysis is coupled to Ollama-shaped services and fields and uses an invented confidence score.
10. Exact route calculation is not implemented.
11. A clean build also reports one nullability warning in `MarketFavorit` and three test-code/analyzer warnings; issue #23 tracks the warning baseline together with the separate Razor fix.

Do not paper over these with warnings alone. Fix the domain model or calculation and add regression coverage.

## 10. Cost-basis semantics

Current implementation:

- mirrors character wallet transactions locally;
- stores one `CostBasisEntry` per character/type;
- supports transaction-derived, estimated, and manual sources;
- currently derives a recent-purchase average and therefore is not a complete inventory-cost ledger.

Target invariant, tracked in the backlog:

- acquisition cost enters exactly once;
- a perpetual weighted average tracks quantity on hand and inventory value;
- sells reduce quantity/value at the current average without changing the unit average;
- transfers do not create profit;
- manufactured, mined/looted, contract, manual, and unknown origins remain distinguishable;
- later FIFO trade matching measures realized trade outcomes separately and does not replace inventory valuation.

Until that migration lands, any displayed cost-basis profit is provisional.

## 11. Trading design constraints

ESI exposes character/corporation/region market data as read operations. WALL-EVE may assist execution using EVE UI endpoints such as opening market details and setting waypoints, but it does not create or modify orders.

Every future actionable opportunity must include:

- exact owner/item/origin/destination;
- executable quantity from cumulative order-book depth;
- quote side, market/location, source, and freshness;
- purchase/sale/broker/tax/relist/transport costs, each once;
- required capital and cargo;
- route and observed risk evidence;
- conservative/realistic/optimistic result ranges where fill time is uncertain;
- a reproducible algorithm version and inspectable inputs.

External providers are optional enrichments:

- zKillboard may add cached, delayed loss evidence for route risk;
- EVE Ref may bootstrap historical data or provide reference fixtures;
- neither replaces ESI orders or blocks core operation when unavailable.

## 12. Development setup

```bash
dotnet restore
dotnet build
dotnet test tests/WALLEve.Tests/WALLEve.Tests.csproj --no-restore
dotnet run
```

Open `http://localhost:5080`.

If `sde.sqlite` is missing and SDE is required, use Settings to download it. Do not work around a missing SDE by inventing names or location mappings.

### EVE developer application

The callback URL is:

```text
http://localhost:5080/callback
```

The documented application setup requests character standings, skills/queue, wallet, location/online/ship, and character-order scopes. A requested scope does not prove that an endpoint is implemented. Check the service code and current ESI OpenAPI before adding a feature; request only the scopes actually needed and handle reauthorization explicitly.

## 13. Code patterns

- Business services use interfaces and constructor injection.
- Pure calculations should be static/pure where practical and receive complete input objects.
- Hosted services create DI scopes and honor cancellation.
- Database queries avoid N+1 I/O; batch IDs and preload dictionaries.
- Store UTC timestamps.
- Use `long` for EVE quantities/IDs where API ranges require it and `decimal` for new monetary calculations. Existing `double` money fields should be migrated deliberately, not mixed silently.
- UI receives prepared read models; it must not reimplement fee or opportunity formulas.
- New sync definitions must be registered as described in section 8.

## 14. Verification checklist

Before considering an issue complete:

- [ ] A failing regression/unit test demonstrated the old behavior where applicable.
- [ ] Focused tests pass.
- [ ] Full test suite passes.
- [ ] `dotnet build` passes without new warnings.
- [ ] Additive migration was tested from the prior schema where applicable.
- [ ] Partial/stale/error behavior was tested for ESI-backed work.
- [ ] Logs were reviewed after a manual smoke test where applicable.
- [ ] German and English README sections remain equivalent if user-facing behavior changed.
- [ ] GitHub issue acceptance criteria are demonstrably satisfied.

## 15. Documentation policy

- `README.md`: user-facing, shipped behavior only, German and English in sync.
- `DEVELOPMENT.md`: current architecture, invariants, setup, sync registry, and known limitations only.
- GitHub Issues/Milestones: authoritative roadmap and acceptance criteria.
- `.brainstorming/`: untracked research and design; never required at runtime and never committed.
- Delete or rewrite obsolete plans instead of appending a competing status section.

## 16. Licensing

WALL-EVE is MIT-licensed. jEveAssets is GPL-2.0. Behavioral comparison is permitted, but do not copy GPL implementation code into WALL-EVE. Record any new third-party runtime dependency and its license in `THIRD-PARTY-NOTICES.md`.
