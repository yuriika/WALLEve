using WALLEve.Components;
using WALLEve.Configuration;
using WALLEve.Services.Authentication;
using WALLEve.Services.Authentication.Interfaces;
using WALLEve.Services.Esi;
using WALLEve.Services.Esi.Interfaces;
using WALLEve.Services.Sde;
using WALLEve.Services.Sde.Interfaces;

var builder = WebApplication.CreateBuilder(args);

SQLitePCL.Batteries.Init();

// Add configuration
builder.Services.Configure<ApplicationSettings>(
    builder.Configuration.GetSection("Application"));
builder.Services.Configure<EveOnlineSettings>(
    builder.Configuration.GetSection("EveOnline"));

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
builder.Services.AddScoped<IEsiApiService, EsiApiService>();
builder.Services.AddSingleton<ISdeUpdateService, SdeUpdateService>();
builder.Services.AddSingleton<ISdeService, SdeService>();

// Add data protection for secure token storage
builder.Services.AddDataProtection();

var app = builder.Build();

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
