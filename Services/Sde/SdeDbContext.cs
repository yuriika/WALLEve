using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using WALLEve.Configuration;

namespace WALLEve.Services.Sde;

/// <summary>
/// Verwaltet die SQLite-Verbindung zur SDE-Datenbank
/// </summary>
public class SdeDbContext : IDisposable
{
    private readonly ILogger<SdeDbContext> _logger;
    private readonly string _dbPath;
    private SqliteConnection? _connection;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);

    public SdeDbContext(
        IOptions<EveOnlineSettings> settings,
        IOptions<ApplicationSettings> appSettings,
        ILogger<SdeDbContext> logger)
    {
        _logger = logger;

        _dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            appSettings.Value.AppDataFolder,
            appSettings.Value.DataFolder,
            settings.Value.Sde.LocalFileName);

        _logger.LogInformation("SDE DbContext initialized with path: {Path}", _dbPath);
    }

    /// <summary>
    /// Gibt die aktuelle Datenbankverbindung zurück
    /// </summary>
    public SqliteConnection Connection
    {
        get
        {
            if (_connection?.State != ConnectionState.Open)
            {
                throw new InvalidOperationException(
                    "Database connection is not open. Call EnsureConnectionAsync() first.");
            }
            return _connection;
        }
    }

    /// <summary>
    /// Prüft ob die Datenbank verfügbar ist
    /// </summary>
    public bool IsDatabaseAvailable()
    {
        return File.Exists(_dbPath);
    }

    /// <summary>
    /// Stellt sicher, dass eine Verbindung zur Datenbank besteht
    /// </summary>
    public async Task EnsureConnectionAsync()
    {
        await _connectionLock.WaitAsync();
        try
        {
            if (_connection?.State == ConnectionState.Open)
                return;

            _connection?.Dispose();
            _connection = new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly");
            await _connection.OpenAsync();
            _logger.LogDebug("SDE database connection opened");
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    /// <summary>
    /// Prüft ob die Verbindung offen ist
    /// </summary>
    public async Task<bool> IsConnectionOpenAsync()
    {
        if (!IsDatabaseAvailable())
        {
            _logger.LogWarning("SDE database file not found at {Path}", _dbPath);
            return false;
        }

        try
        {
            await EnsureConnectionAsync();
            return _connection?.State == ConnectionState.Open;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking SDE database availability");
            return false;
        }
    }

    public void Dispose()
    {
        _connection?.Dispose();
        _connectionLock?.Dispose();
    }
}
