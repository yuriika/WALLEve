using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WALLEve.Data;

namespace WALLEve.Tests;

/// <summary>
/// Erzeugt einen in-memory WalletDbContext für Tests.
/// SQLite :memory: mit offengehaltener Connection — lebt für die Dauer des Tests.
/// </summary>
public static class TestDb
{
    public static WalletDbContext Create()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<WalletDbContext>()
            .UseSqlite(connection)
            .Options;

        var db = new WalletDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }
}