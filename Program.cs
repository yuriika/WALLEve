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
using WALLEve.Services.Map;
using WALLEve.Services.Map.Interfaces;

var builder = WebApplication.CreateBuilder(args);

SQLitePCL.Batteries.Init();

// Add configuration
builder.Services.Configure<ApplicationSettings>(
    builder.Configuration.GetSection("Application"));
builder.Services.Configure<EveOnlineSettings>(
    builder.Configuration.GetSection("EveOnline"));
builder.Services.Configure<WALLEve.Models.Configuration.WalletOptions>(
    builder.Configuration.GetSection("EveOnline:Wallet"));

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

// Register application services
builder.Services.AddSingleton<ITokenStorageService, TokenStorageService>();
builder.Services.AddScoped<IEveAuthenticationService, EveAuthenticationService>();
builder.Services.AddSingleton<IEsiCacheService, EsiCacheService>();
builder.Services.AddScoped<IEsiApiService, EsiApiService>();

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
// Build path similar to SDE database: ~/.local/share/WALLEve/Data/wallet.db
var walletSettings = builder.Configuration.GetSection("EveOnline:Wallet").Get<WALLEve.Models.Configuration.WalletOptions>() ?? new();
var walletDbPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    appSettings.AppDataFolder,
    appSettings.DataFolder,
    walletSettings.LocalFileName);

Console.WriteLine($"Wallet DB Path: {walletDbPath}");

builder.Services.AddDbContext<WalletDbContext>(options =>
    options.UseSqlite($"Data Source={walletDbPath}"));

// Wallet services
builder.Services.AddScoped<IWalletLinkService, WalletLinkService>();
builder.Services.AddScoped<IWalletService, WalletService>();

// Add data protection for secure token storage
builder.Services.AddDataProtection();

var app = builder.Build();

// Initialize Wallet Database
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

Console.WriteLine("===========================================");
Console.WriteLine($"  {appSettings.Name} v{appSettings.Version} gestartet!");
Console.WriteLine($"  Öffne {appSettings.Server.Url} im Browser");
Console.WriteLine("===========================================");

app.Run(appSettings.Server.Url);
