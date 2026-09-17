using Microsoft.EntityFrameworkCore;
using WALLEve.Components;
using WALLEve.Configuration;
using WALLEve.Data;
using WALLEve.Services.Authentication;
using WALLEve.Services.Authentication.Interfaces;
using WALLEve.Services.Esi;
using WALLEve.Services.Esi.Interfaces;
using WALLEve.Services.Sde;
using WALLEve.Services.Sde.Interfaces;
using WALLEve.Services.Wallet;
using WALLEve.Services.Wallet.Interfaces;
using WALLEve.Services.Holdings;
using WALLEve.Services.Holdings.Interfaces;
using WALLEve.Services.Mining;
using WALLEve.Services.Mining.Interfaces;
using WALLEve.Services.Portfolio;
using WALLEve.Services.Portfolio.Interfaces;
using WALLEve.Services.Stockpiles;
using WALLEve.Services.Stockpiles.Interfaces;
using WALLEve.Services.Map;
using WALLEve.Services.Map.Interfaces;
using WALLEve.Services.Market;
using WALLEve.Services.Market.Interfaces;
using WALLEve.Services.Risk;
using WALLEve.Services.Risk.Interfaces;
using WALLEve.Models.Configuration;

var builder = WebApplication.CreateBuilder(args);

SQLitePCL.Batteries.Init();

// Add configuration
builder.Services.Configure<ApplicationSettings>(
    builder.Configuration.GetSection("Application"));
builder.Services.Configure<EveOnlineSettings>(
    builder.Configuration.GetSection("EveOnline"));
builder.Services.Configure<WALLEve.Models.Configuration.WalletOptions>(
    builder.Configuration.GetSection("EveOnline:Wallet"));
builder.Services.Configure<AISettings>(
    builder.Configuration.GetSection("AI"));

// Add Blazor Server services
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Register HTTP client factory
var appSettings = builder.Configuration.GetSection("Application").Get<ApplicationSettings>() ?? new();

// Named HTTP Clients für verschiedene Zwecke
builder.Services.AddHttpClient("EveApi", client =>
{
    client.DefaultRequestHeaders.Add("User-Agent", appSettings.UserAgent);
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddHttpClient("SdeDownload", client =>
{
    client.DefaultRequestHeaders.Add("User-Agent", appSettings.UserAgent);
    client.Timeout = TimeSpan.FromMinutes(10); // Längerer Timeout für Downloads
});

var aiSettings = builder.Configuration.GetSection("AI").Get<AISettings>() ?? new();
builder.Services.AddHttpClient("Ollama", client =>
{
    client.BaseAddress = new Uri(aiSettings.Ollama.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(aiSettings.Ollama.TimeoutSeconds);
});

// Optionale zKillboard-Risikoquelle (#73): User-Agent, Compression (GZip/Deflate),
// lokaler Cache, Request-Abstand und Provider-Health im Adapter selbst.
builder.Services.Configure<ZkillboardSettings>(
    builder.Configuration.GetSection("Zkillboard"));

var zkillboardSettings = builder.Configuration.GetSection("Zkillboard").Get<ZkillboardSettings>() ?? new();
builder.Services.AddHttpClient("Zkillboard", client =>
{
    client.BaseAddress = new Uri(zkillboardSettings.BaseUrl);
    client.DefaultRequestHeaders.Add("User-Agent", zkillboardSettings.UserAgent);
    client.Timeout = TimeSpan.FromSeconds(15);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
});

builder.Services.AddScoped<IZkillboardClient, ZkillboardClient>();
builder.Services.AddScoped<IUniverseActivitySource, EsiUniverseActivitySource>();
builder.Services.AddScoped<IRouteRiskService, RouteRiskService>();

// Register application services
builder.Services.AddSingleton<ITokenStorageService, TokenStorageService>();
builder.Services.AddScoped<IEveAuthenticationService, EveAuthenticationService>();
builder.Services.AddSingleton<IEsiCacheService, EsiCacheService>();
builder.Services.AddScoped<IEsiApiService, EsiApiService>();
builder.Services.AddScoped<WALLEve.Services.Esi.Interfaces.IEsiUiActionService, WALLEve.Services.Esi.EsiUiActionService>();

// SDE Services
builder.Services.AddSingleton<SdeDbContext>(); // Shared DbContext
builder.Services.AddSingleton<ISdeUpdateService, SdeUpdateService>();
builder.Services.AddSingleton<ISdeUniverseService, SdeUniverseService>();
builder.Services.AddSingleton<ISdeCharacterService, SdeCharacterService>();

// Map Services
builder.Services.AddSingleton<IMapDataService, MapDataService>();
builder.Services.AddSingleton<IMapStatisticsService, MapStatisticsService>();
builder.Services.AddSingleton<IRouteCalculationService, RouteCalculationService>();

// Wallet Database (separate SQLite DB for app data)
// Prefer the macOS ~/Library/Application Support path, fallback to ~/.local/share
var walletSettings = builder.Configuration.GetSection("EveOnline:Wallet").Get<WALLEve.Models.Configuration.WalletOptions>() ?? new();
var walletDbDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    appSettings.AppDataFolder,
    appSettings.DataFolder);
// On macOS, LocalApplicationData returns ~/.local/share/ but the existing data
// might be at ~/Library/Application Support/ (from earlier app versions)
var macOsLegacyPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    "Library", "Application Support",
    appSettings.AppDataFolder, appSettings.DataFolder);
if (Directory.Exists(macOsLegacyPath) && File.Exists(Path.Combine(macOsLegacyPath, walletSettings.LocalFileName)))
{
    walletDbDir = macOsLegacyPath;
    Console.WriteLine($"Using legacy macOS DB path: {macOsLegacyPath}");
}
var walletDbPath = Path.Combine(walletDbDir, walletSettings.LocalFileName);

Console.WriteLine($"Wallet DB Path: {walletDbPath}");

builder.Services.AddDbContext<WalletDbContext>(options =>
    options.UseSqlite($"Data Source={walletDbPath}"));

// SDE NST Service
builder.Services.AddScoped<ISdeNstService, SdeNstService>();

// Issue #67: begrenzter, cachekonformer Messlauf des Regionalscans (Discovery).
// Nur bei expliziter Konfiguration ("Measurement:RegionalScan:Enabled": true) —
// einmalig beim Start, KEIN Produktionscollector.
var regionalScanMeasurementSettings =
    builder.Configuration.GetSection("Measurement:RegionalScan")
        .Get<RegionalScanMeasurementSettings>() ?? new();
if (regionalScanMeasurementSettings.Regions.Length == 0)
{
    // Fallback: vier Handelszentren + ein Randgebiet (nur wenn keine Regionen konfiguriert sind).
    regionalScanMeasurementSettings.Regions = new[] { 10000002, 10000043, 10000032, 10000030, 10000068 };
}
builder.Services.AddSingleton(regionalScanMeasurementSettings);
builder.Services.AddSingleton(new RegionalScanMeasurementOptions
{
    DatabasePath = walletDbPath,
    ArtifactDirectory = walletDbDir,
    EsiBaseUrl = builder.Configuration["EveOnline:EsiBaseUrl"] ?? "https://esi.evetech.net/latest",
    ProbeGzip = regionalScanMeasurementSettings.ProbeGzip
});
builder.Services.AddScoped<IRegionalScanMeasurementService, RegionalScanMeasurementService>();
if (regionalScanMeasurementSettings.Enabled)
{
    builder.Services.AddHostedService<RegionalScanMeasurementHostedService>();
}

// Wallet services
builder.Services.AddScoped<IWalletLinkService, WalletLinkService>();
builder.Services.AddScoped<IWalletService, WalletService>();

// Holdings services (M1): atomare Character-Snapshot-Synchronisation
builder.Services.AddScoped<IHoldingsSyncService, HoldingsSyncService>();
builder.Services.AddScoped<IHoldingsLocationResolver, HoldingsLocationResolver>();
builder.Services.AddScoped<IPortfolioSnapshotService, PortfolioSnapshotService>();

// Mining services (M5 #39): idempotente Synchronisation des persönlichen Mining-Ledgers
builder.Services.AddScoped<IMiningSyncService, MiningSyncService>();
// M5 #48: Lese-Auswertung des Ledgers nach Zeitraum/Erztyp/System mit Marktbewertung
builder.Services.AddScoped<IMiningValuationService, MiningValuationService>();
builder.Services.AddScoped<IPortfolioHistoryService>(sp =>
    new PortfolioHistoryService(
        sp.GetRequiredService<WalletDbContext>(),
        typeIds => sp.GetRequiredService<WALLEve.Services.Sde.Interfaces.ISdeUniverseService>()
            .GetTypeGroupsAsync(typeIds)));
builder.Services.AddScoped<IHoldingsAggregateService, HoldingsAggregateService>();

// Stockpile services (M2 #36): persistierte Ziele ohne Bestandsberechnung
builder.Services.AddScoped<IStockpileService, StockpileService>();
// Stockpile-Berechnung (M2 #43): physisch/eingehend/gebunden getrennt, Shortage nur gegen physischen Bestand
builder.Services.AddScoped<IStockpileCalculationService, StockpileCalculationService>();
// Stockpile-Übersicht für die UI (M2 #53): Zeilen plus Quellen-/Freshness-Metadaten
builder.Services.AddScoped<IStockpileOverviewService, StockpileOverviewService>();

// Market Analysis services
// AI Services
builder.Services.AddScoped<WALLEve.Services.AI.Interfaces.IOllamaService, WALLEve.Services.AI.OllamaService>();
builder.Services.AddScoped<WALLEve.Services.Market.Interfaces.IMarketAnalysisService, WALLEve.Services.Market.MarketAnalysisService>();
builder.Services.AddScoped<WALLEve.Services.Market.Interfaces.IMarketDataService, WALLEve.Services.Market.MarketDataService>();
// Regionen-Cache (#69): bewusst SINGLETON statt scoped — der Cache muss das
// 5-Minuten-Fenster zwischen den Collector-Loops und bis zur UI-Scope überleben
// (Review-Fix: scoped verwarf die Instanz nach jedem Collector-Loop und verbarg
// die Messgrundlage/RegionScanBasis vor der UI). ESI wird pro Scan aus einem
// frischen Scope aufgelöst, daher keine Captive Dependency.
builder.Services.AddSingleton<WALLEve.Services.Market.Interfaces.IRegionalMarketCacheService>(sp =>
    new WALLEve.Services.Market.RegionalMarketCacheService(
        sp.GetRequiredService<IServiceScopeFactory>(),
        sp.GetRequiredService<ILogger<WALLEve.Services.Market.RegionalMarketCacheService>>()));
builder.Services.AddScoped<WALLEve.Services.Market.Interfaces.IFeeCalculatorService, WALLEve.Services.Market.FeeCalculatorService>();
// Manuelle Gebühren-Overrides (Issue #46): optionale Sektion "FeeCalculator" —
// fehlende Felder lassen die App bei automatischen/konservativen Sätzen.
builder.Services.Configure<FeeOverrideSettings>(builder.Configuration.GetSection("FeeCalculator"));
builder.Services.AddScoped<WALLEve.Services.Market.Interfaces.IInventoryService, WALLEve.Services.Market.InventoryService>();
builder.Services.AddScoped<WALLEve.Services.Market.Interfaces.IBackgroundJobManager, WALLEve.Services.Market.BackgroundJobManager>();
builder.Services.AddScoped<WALLEve.Services.Market.Interfaces.ICostBasisService, WALLEve.Services.Market.CostBasisService>();
builder.Services.AddScoped<WALLEve.Services.Market.Interfaces.ICostBasisLedgerService, WALLEve.Services.Market.CostBasisLedgerService>();
builder.Services.AddScoped<WALLEve.Services.Market.Interfaces.IHubSelectionService, WALLEve.Services.Market.HubSelectionService>();
builder.Services.AddScoped<WALLEve.Services.Market.Interfaces.ISyncOverviewService, WALLEve.Services.Market.SyncOverviewService>();
builder.Services.AddScoped<WALLEve.Services.Market.Interfaces.ISyncTriggerService, WALLEve.Services.Market.SyncTriggerService>();
builder.Services.AddSingleton<WALLEve.Services.Market.Interfaces.ISyncWakeService, WALLEve.Services.Market.SyncWakeService>();
builder.Services.AddScoped<WALLEve.Services.Market.Interfaces.IOrderIntelligenceService, WALLEve.Services.Market.OrderIntelligenceService>();
builder.Services.AddScoped<WALLEve.Services.Trading.Interfaces.ITradeProfileService, WALLEve.Services.Trading.TradeProfileService>();
builder.Services.AddScoped<WALLEve.Services.Trading.Interfaces.ITradeStatusService, WALLEve.Services.Trading.TradeStatusService>();
builder.Services.AddScoped<WALLEve.Services.Trading.Interfaces.IRecommendationAttributionService, WALLEve.Services.Trading.RecommendationAttributionService>();
builder.Services.AddScoped<WALLEve.Services.Trading.Interfaces.ITradingActionCardService, WALLEve.Services.Trading.TradingActionCardService>();
builder.Services.AddScoped<WALLEve.Services.Trading.Interfaces.ITradeSignalDecisionService, WALLEve.Services.Trading.TradeSignalDecisionService>();
builder.Services.AddSingleton<WALLEve.Services.Trading.TradeProfileFilter>();
builder.Services.AddScoped<WALLEve.Services.Trading.Interfaces.IStationTradeAnalysisService, WALLEve.Services.Trading.StationTradeAnalysisService>();
// Trading-Meldungen (Issue #70): Feed als Singleton — überlebt Blazor-Reconnects
// und ist gegen paralleles Polling dedupliziert. Browser-Benachrichtigungen und
// ihr Berechtigungszustand sind pro Circuit (scoped), damit ein Circuit den
// anderen nicht beeinflusst.
builder.Services.AddSingleton(new WALLEve.Services.Notifications.TradingNotificationOptions());
builder.Services.AddSingleton<WALLEve.Services.Notifications.ITradingNotificationService, WALLEve.Services.Notifications.TradingNotificationService>();
builder.Services.AddScoped<WALLEve.Services.Notifications.IBrowserNotificationService, WALLEve.Services.Notifications.BrowserNotificationService>();
builder.Services.AddScoped<WALLEve.Services.Notifications.BrowserNotificationState>();
builder.Services.AddMemoryCache();

// Background service for continuous market data collection
builder.Services.AddHostedService<MarketDataCollectorService>();

// Warmt den Inventar-Cache beim Start auf (schnellerer erster Tab-Klick)
builder.Services.AddHostedService<InventoryWarmupService>();

// Cost-Basis-Ermittlung im Hintergrund (Transaktions-Sink + Ableitung)
builder.Services.AddHostedService<CostBasisCollectorService>();

// Add data protection for secure token storage
builder.Services.AddDataProtection();

var app = builder.Build();

// Initialize Wallet Database
// and executes Migrations
using (var scope = app.Services.CreateScope())
{
    var walletDb = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
    await walletDb.InitializeDatabaseAsync();
}

// Configure the HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// OAuth callback endpoint
app.MapGet("/callback", async (
    HttpContext context,
    IEveAuthenticationService authService) =>
{
    var code = context.Request.Query["code"].ToString();
    var state = context.Request.Query["state"].ToString();

    if (string.IsNullOrEmpty(code))
    {
        return Results.Redirect("/?error=no_code");
    }

    var success = await authService.HandleCallbackAsync(code, state);

    return success
        ? Results.Redirect("/character")
        : Results.Redirect("/?error=auth_failed");
});

// Test endpoint for Ollama connection
// Entfernt (Issue #32): der deterministische Analysepfad benötigt keinen
// LLM-Server; ein separater Ollama-Test ist nicht mehr Teil der App.

Console.WriteLine("===========================================");
Console.WriteLine($"  {appSettings.Name} v{appSettings.Version} gestartet!");
Console.WriteLine($"  Öffne {appSettings.Server.Url} im Browser");
Console.WriteLine("===========================================");

app.Run(appSettings.Server.Url);
