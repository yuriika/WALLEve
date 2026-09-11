using WALLEve.Services.Authentication.Interfaces;
using WALLEve.Services.Market.Interfaces;

namespace WALLEve.Services.Market;

/// <summary>
/// Warmt den Inventar-Cache beim App-Start für den zuletzt angemeldeten
/// Charakter auf. Dadurch ist der Bestand-Tab sofort geladen, wenn der
/// User ihn öffnet — die ESI-Abrufe (Assets, Marktpreise, Transaktionen)
/// laufen einmal im Hintergrund statt beim ersten Tab-Klick.
/// </summary>
public class InventoryWarmupService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<InventoryWarmupService> _logger;

    public InventoryWarmupService(IServiceProvider services, ILogger<InventoryWarmupService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Kurz warten, bis die App vollständig gestartet ist und der
        // TokenStorage den gespeicherten Login geladen hat.
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (stoppingToken.IsCancellationRequested) return;

        using var scope = _services.CreateScope();
        var authService = scope.ServiceProvider.GetRequiredService<IEveAuthenticationService>();
        var authState = await authService.GetAuthStateAsync();

        if (authState?.IsValid != true)
        {
            _logger.LogInformation("Inventory warmup skipped — no authenticated character.");
            return;
        }

        try
        {
            var inventoryService = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            _logger.LogInformation("Inventory warmup started for character {CharacterId}...", authState.CharacterId);

            var items = await inventoryService.GetInventoryAsync(authState.CharacterId);
            var overview = await inventoryService.GetPortfolioOverviewAsync(authState.CharacterId);

            _logger.LogInformation(
                "Inventory warmup done: {Count} item types, {Quantity} units, market value {Value:N0} ISK",
                items.Count, overview.TotalQuantity, overview.TotalMarketValue);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Inventory warmup failed — will be loaded on first tab visit instead.");
        }
    }
}