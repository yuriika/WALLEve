using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WALLEve.Configuration;
using WALLEve.Data;
using WALLEve.Models.Authentication;
using WALLEve.Models.Database;
using WALLEve.Models.Esi.Alliance;
using WALLEve.Models.Esi.Character;
using WALLEve.Models.Esi.Corporation;
using WALLEve.Models.Esi.Markets;
using WALLEve.Models.Measurement;
using WALLEve.Models.Esi.Universe;
using WALLEve.Models.Esi.Wallet;
using WALLEve.Services.Authentication.Interfaces;
using WALLEve.Services.Esi.Interfaces;
using WALLEve.Services.Market;
using WALLEve.Services.Market.Interfaces;
using WALLEve.Services.Trading;
using WALLEve.Services.Trading.Interfaces;

namespace WALLEve.Tests;

/// <summary>
/// Regressionstests für Issue #32: der deterministische Analysepfad (Wallet/Markt/
/// Bestand/Trading und ihre Jobs) darf weder in der DI-Konstruktion noch zur
/// Laufzeit einen LLM-Server (Ollama) benötigen — unerreichbarer LLM-Endpunkt,
/// identische Ergebnisse mit/ohne LLM, DI-/Job-Smoke ohne LLM-Server.
/// </summary>
public class DeterministicAnalysisWithoutLlmTests
{
    private const int CharacterId = 90073315;

    // ------------------------------------------------------------------
    // Fakes (nur die im deterministischen Pfad genutzten Member)
    // ------------------------------------------------------------------

    private sealed class FakeAuthService(bool authenticated) : IEveAuthenticationService
    {
        public Task<EveAuthState?> GetAuthStateAsync()
            => Task.FromResult<EveAuthState?>(authenticated
                ? new EveAuthState { AccessToken = "tok", RefreshToken = "ref", CharacterId = CharacterId, CharacterName = "Test" }
                : null);

        public Task<bool> IsAuthenticatedAsync() => Task.FromResult(authenticated);
        public string GetLoginUrl() => "http://login";
        public Task<bool> HandleCallbackAsync(string code, string state) => Task.FromResult(true);
        public Task<string?> GetAccessTokenAsync() => Task.FromResult<string?>(authenticated ? "tok" : null);
        public Task LogoutAsync() => Task.CompletedTask;
        public Task<List<KnownCharacter>> GetAllCharactersAsync() => Task.FromResult(new List<KnownCharacter>());
        public Task<bool> SwitchCharacterAsync(int characterId) => Task.FromResult(true);

        // Interface-Vertrag ohne Nutzung in diesen Tests: explizite leere
        // Accessoren statt Feld-Event, damit CS0067 nicht feuert.
        event EventHandler<bool>? IEveAuthenticationService.AuthenticationStateChanged
        {
            add { }
            remove { }
        }
    }

    private sealed class FakeInventoryService : IInventoryService
    {
        public List<InventoryItem> Items { get; } = new();

        public Task<List<InventoryItem>> GetInventoryAsync(int characterId) => Task.FromResult(Items);
        public Task<PortfolioOverview> GetPortfolioOverviewAsync(int characterId)
            => Task.FromResult(new PortfolioOverview());
        public Task<List<InventoryItem>> GetPrioritizedItemsAsync(int characterId,
            InventorySortMode sortMode = InventorySortMode.Opportunity)
            => Task.FromResult(Items);
    }

    /// <summary>Fake: nur GetCharacterSkillsAsync wird in der Analyse verwendet; Rest wirft.</summary>
    private sealed class FakeEsiApiService : IEsiApiService
    {
        public Task<CharacterSkills?> GetCharacterSkillsAsync()
            => Task.FromResult<CharacterSkills?>(new CharacterSkills());

        public Task<CharacterOverview?> GetCharacterOverviewAsync() => throw new NotImplementedException();
        public Task<EveCharacter?> GetCharacterAsync(int characterId) => throw new NotImplementedException();
        public Task<EveCorporation?> GetCorporationAsync(int corporationId) => throw new NotImplementedException();
        public Task<EveAlliance?> GetAllianceAsync(int allianceId) => throw new NotImplementedException();
        public Task<double?> GetWalletBalanceAsync(int characterId) => throw new NotImplementedException();
        public Task<CharacterLocation?> GetLocationAsync(int characterId) => throw new NotImplementedException();
        public Task<CharacterShip?> GetCurrentShipAsync(int characterId) => throw new NotImplementedException();
        public Task<CharacterOnlineStatus?> GetOnlineStatusAsync(int characterId) => throw new NotImplementedException();
        public Task<SolarSystem?> GetSolarSystemAsync(int systemId) => throw new NotImplementedException();
        public Task<EveType?> GetTypeAsync(int typeId) => throw new NotImplementedException();
        public Task<StructureLookupResult> GetStructureAsync(long structureId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<CharacterAsset>?> GetCharacterAssetsAsync(int characterId) => throw new NotImplementedException();
        public Task<List<WalletJournalEntry>?> GetWalletJournalAsync(int characterId, int page = 1) => throw new NotImplementedException();
        public Task<List<WalletTransaction>?> GetWalletTransactionsAsync(int characterId) => throw new NotImplementedException();
        public Task<List<WalletJournalEntry>?> GetAllWalletJournalPagesAsync(int characterId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<WalletTransaction>?> GetAllWalletTransactionsPagesAsync(int characterId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<MarketOrder>?> GetMarketOrdersAsync(int characterId) => throw new NotImplementedException();
        public Task<List<MarketOrderHistory>?> GetMarketOrderHistoryAsync(int characterId) => throw new NotImplementedException();
        public Task<List<WalletJournalEntry>?> GetCorporationWalletJournalAsync(int corporationId, int division, int page = 1) => throw new NotImplementedException();
        public Task<List<WalletTransaction>?> GetCorporationWalletTransactionsAsync(int corporationId, int division) => throw new NotImplementedException();
        public Task<List<SystemJumps>?> GetSystemJumpsAsync() => throw new NotImplementedException();
        public Task<List<SystemKills>?> GetSystemKillsAsync() => throw new NotImplementedException();
        public Task<List<RegionalMarketOrder>?> GetRegionalMarketOrdersAsync(int regionId, int? typeId = null, string orderType = "all", int page = 1) => throw new NotImplementedException();
        public Task<List<RegionalMarketOrder>?> GetAllRegionalMarketOrdersAsync(int regionId, int? typeId = null, string orderType = "all", CancellationToken ct = default, Action<RegionalScanPageTelemetry>? telemetrySink = null) => throw new NotImplementedException();
        public Task<List<MarketHistoryEntry>?> GetMarketHistoryAsync(int regionId, int typeId) => throw new NotImplementedException();
        public Task<List<MarketPrice>?> GetMarketPricesAsync() => throw new NotImplementedException();
    }

    // ------------------------------------------------------------------
    // Helfer: DI-Container wie in Program.cs für den deterministischen Pfad,
    // ABER ohne IOllamaService und ohne Ollama-HttpClient. Der LLM-Endpunkt
    // ist dennoch als AISettings konfiguriert (Umgebung), wird aber nie besucht.
    // ------------------------------------------------------------------

    private static ServiceProvider BuildDeterministicContainer(
        WalletDbContext db, FakeInventoryService inventory, FakeAuthService auth, string llmBaseUrl)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(db);
        services.AddSingleton<IInventoryService>(inventory);
        services.AddSingleton<IEveAuthenticationService>(auth);
        services.AddSingleton<IEsiApiService>(new FakeEsiApiService());
        services.AddSingleton<IFeeCalculatorService, FeeCalculatorService>();
        services.AddSingleton<ITradeStatusService, TradeStatusService>();
        services.AddSingleton<IMarketAnalysisService, MarketAnalysisService>();
        services.AddSingleton<IBackgroundJobManager>(new BackgroundJobManager(db));
        services.AddSingleton<IOptions<AISettings>>(Options.Create(new AISettings
        {
            Ollama = new OllamaSettings { BaseUrl = llmBaseUrl, DefaultModel = "llama3.1:8b", TimeoutSeconds = 5 }
        }));

        var provider = services.BuildServiceProvider();
        // Beim Auflösen wird der LLM-Endpunkt NICHT kontaktiert — genau das prüfen
        // die Tests unten über den Verbindungszähler des instrumentierten Endpunkts.
        return provider;
    }

    /// <summary>Item mit Cost Basis, das mit echtem Gewinn verkauft werden kann (8.450 ISK bei 1000 Stück).</summary>
    private static InventoryItem ProfitableItem(int typeId, int qty = 1000)
        => new()
        {
            TypeId = typeId,
            TypeName = $"Item {typeId}",
            TotalQuantity = qty,
            CostBasisPerUnit = 90.0,
            BestSellPrice = 110.0,
            Locations = [new InventoryLocationAggregate { LocationId = 60003466, LocationType = "station", LocationFlag = "Hangar", Quantity = qty }],
            SellContexts = [new InventorySellContext { LocationId = 60003466, LocationType = "station", LocationLabel = "Station 60003466", Quantity = qty }]
        };

    /// <summary>Startet einen stillen TCP-Endpunkt, der Verbindungen zählt, aber nie antwortet.</summary>
    private static (TcpListener Listener, int Port, Func<int> ConnectionCount) StartSilentLlmEndpoint()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var accepted = 0;

        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    using var client = await listener.AcceptTcpClientAsync();
                    Interlocked.Increment(ref accepted);
                    client.Client.Close();
                }
                catch
                {
                    break; // Listener gestoppt
                }
            }
        });

        return (listener, port, () => Volatile.Read(ref accepted));
    }

    // ------------------------------------------------------------------
    // AC1: Tests laufen mit absichtlich unerreichbarem LLM-Endpunkt,
    // ohne dass dieser aufgerufen wird.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Analyze_WithUnreachableLlmEndpoint_CompletesWithoutCallingIt()
    {
        using var db = TestDb.Create();
        var (listener, port, connectionCount) = StartSilentLlmEndpoint();
        try
        {
            var inventory = new FakeInventoryService();
            inventory.Items.Add(ProfitableItem(1));
            // LLM-Endpunkt ist konfiguriert, antwortet aber nie (stiller Listener):
            // Ein Aufruf würde hängen oder nach Timeout scheitern — beides wäre rot.
            using var provider = BuildDeterministicContainer(db, inventory, new FakeAuthService(true), $"http://127.0.0.1:{port}/");
            var analysis = provider.GetRequiredService<IMarketAnalysisService>();

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var opportunities = await analysis.AnalyzeMarketDataAsync();
            stopwatch.Stop();

            var opp = Assert.Single(opportunities);
            Assert.Equal(8_450.0, opp.EstimatedProfit, 2);

            // Kein einziger Verbindungsversuch zum LLM-Endpunkt.
            Assert.Equal(0, connectionCount());
            // Kein Timeout-Warten auf den toten Endpunkt (wäre > 5 s gewesen).
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"Analysis took {stopwatch.Elapsed}");
        }
        finally
        {
            listener.Stop();
        }
    }

    // ------------------------------------------------------------------
    // AC2: Deterministische Eingaben liefern mit und ohne LLM dieselben
    // Berechnungsergebnisse.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Analyze_SameDeterministicInputs_SameResults_WithAndWithoutLlmEndpoint()
    {
        using var dbA = TestDb.Create();
        using var dbB = TestDb.Create();

        var inventoryA = new FakeInventoryService();
        inventoryA.Items.Add(ProfitableItem(1, qty: 500));
        var inventoryB = new FakeInventoryService();
        inventoryB.Items.Add(ProfitableItem(1, qty: 500));

        // "Mit LLM": stiller, antwortender Endpunkt ist konfiguriert (Port aktiv).
        var (listener, port, _) = StartSilentLlmEndpoint();
        try
        {
            using var providerWithLlm = BuildDeterministicContainer(dbA, inventoryA, new FakeAuthService(true), $"http://127.0.0.1:{port}/");
            // "Ohne LLM": Port geschlossen → Verbindung würde sofort abgelehnt.
            using var providerWithoutLlm = BuildDeterministicContainer(dbB, inventoryB, new FakeAuthService(true), $"http://127.0.0.1:{port + 1}/");

            var withLlm = (await providerWithLlm.GetRequiredService<IMarketAnalysisService>().AnalyzeMarketDataAsync()).Single();
            var withoutLlm = (await providerWithoutLlm.GetRequiredService<IMarketAnalysisService>().AnalyzeMarketDataAsync()).Single();

            // Identische Berechnung: dieselben Zahlen, ob der LLM-Endpunkt erreichbar ist oder nicht.
            Assert.Equal(withoutLlm.EstimatedProfit, withLlm.EstimatedProfit);
            Assert.Equal(withoutLlm.RequiredCapital, withLlm.RequiredCapital);
            Assert.Equal(withoutLlm.Score, withLlm.Score);
            Assert.Equal(withoutLlm.BuyPrice, withLlm.BuyPrice);
            Assert.Equal(withoutLlm.SellPrice, withLlm.SellPrice);
            Assert.Equal(withoutLlm.SellLocationId, withLlm.SellLocationId);
            Assert.Equal(withoutLlm.Evidence, withLlm.Evidence);
            Assert.Equal(withoutLlm.Provenance, withLlm.Provenance);
            // Ehrliche Provenienz (#33): deterministische Heuristik, keine AI-Angabe.
            Assert.Equal(TradingOpportunity.ProvenanceHeuristic, withLlm.Provenance);
            Assert.Equal(MarketAnalysisService.AlgorithmVersionInventorySell, withLlm.AlgorithmVersion);
            Assert.Equal(4_225.0, withLlm.EstimatedProfit, 2); // 500 × 8,45
        }
        finally
        {
            listener.Stop();
        }
    }

    // ------------------------------------------------------------------
    // AC3: Start-/Job-Smoke ohne LLM-Server; konkreter Auth-Blocker benannt.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Smoke_DiAndJobExecution_WorkWithoutLlmServer()
    {
        using var db = TestDb.Create();
        // Port 1: Verbindung wird sofort abgelehnt — LLM-Server ist nicht erreichbar.
        using var provider = BuildDeterministicContainer(db, new FakeInventoryService(), new FakeAuthService(true), "http://127.0.0.1:1/");

        // Start-Smoke: die Job-/Analyse-Dienste lassen sich ohne LLM-Server auflösen
        // (genau das tun MarketDataCollectorService und CostBasisCollectorService).
        var analysis = provider.GetRequiredService<IMarketAnalysisService>();
        var jobManager = provider.GetRequiredService<IBackgroundJobManager>();
        Assert.NotNull(analysis);
        Assert.NotNull(jobManager);

        // Job-Smoke: ein deterministischer Job läuft durch, ohne dass ein LLM
        // benötigt wird.
        var job = await jobManager.CreateJobAsync("DeterministicSmoke", "Smoke", CharacterId, total: 3);
        await jobManager.UpdateProgressAsync(job.Id, 3, 3);
        await jobManager.MarkCompletedAsync(job.Id);
        var reloaded = await jobManager.GetJobAsync(job.Id);
        Assert.Equal(BackgroundJobStatus.Completed, reloaded!.Status);

        // Hinweis (Auth-Blocker, dokumentiert statt Live-ESI-Test): Ein vollständiger
        // End-to-End-Joblauf (ESI-Sync, Bestand, Cost-Basis-Schätzung) benötigt ein
        // authentifiziertes Charakter-Token über den OAuth-Flow — Unit-Tests dürfen
        // keine Live-ESI-Abhängigkeit haben. Der deterministische Pfad selbst braucht
        // nur das Token als Eingabe und ist damit LLM-frei.
    }

    [Fact]
    public async Task Analyze_WithoutAuthentication_ReturnsEmptyWithoutLlmOrEsiInteraction()
    {
        using var db = TestDb.Create();
        using var provider = BuildDeterministicContainer(db, new FakeInventoryService(), new FakeAuthService(false), "http://127.0.0.1:1/");
        var analysis = provider.GetRequiredService<IMarketAnalysisService>();

        // Ohne gültige Authentifizierung liefert die Analyse leer zurück —
        // kein ESI- und kein LLM-Kontakt (früher Return im Service).
        var opportunities = await analysis.AnalyzeMarketDataAsync();

        Assert.Empty(opportunities);
    }
}