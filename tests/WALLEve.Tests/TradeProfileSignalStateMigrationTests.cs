using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using WALLEve.Data;
using WALLEve.Models.Database;

namespace WALLEve.Tests;

/// <summary>
/// Migrationstests für Issue #68 (Signal-Zustand der Handelsprofile): Die
/// additive Migration AddTradeProfileSignalState erweitert TradeProfiles um
/// CooldownMinutes, SignalDeactivated, LastReportedFingerprint und
/// LastReportedAt, ohne bestehende Profile oder Nutzerdaten anzutasten.
/// Bestandsprofile erhalten die neutralen Defaults (kein Cooldown, aktiv,
/// noch nie gemeldet).
/// </summary>
public class TradeProfileSignalStateMigrationTests
{
    /// <summary>Letzte Migration VOR der Signal-Zustands-Migration (Recommendation-Attributions, #66).</summary>
    private const string PreviousMigration = "20260915153922_AddRecommendationAttributions";

    private static WalletDbContext CreateDb(string connectionString)
        => new(new DbContextOptionsBuilder<WalletDbContext>()
            .UseSqlite(connectionString)
            .Options);

    [Fact]
    public async Task Migrate_PreservesExistingProfileAndAddsSignalStateColumns()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"walleve-signalstate-{Guid.NewGuid():N}.db");
        try
        {
            // Phase 1: Schema bis VOR der Signal-Zustands-Migration anlegen und
            // ein bestehendes Profil plus Nutzerdaten (Wallet-Transaktion) einfügen.
            await using (var db = CreateDb($"Data Source={dbPath}"))
            {
                await db.Database.GetService<IMigrator>().MigrateAsync(PreviousMigration);

                // Bestandsprofil über explizites SQL einfügen: die neue Spalten
                // existieren noch nicht, die alten NOT-NULL-Spalten werden gesetzt.
                await db.Database.ExecuteSqlRawAsync(
                    "INSERT INTO TradeProfiles (CharacterId, Name, UpdatedAt, AllowHighSec, AllowLowSec, AllowNullSec, MaxCapital) " +
                    "VALUES (90073315, 'Mein Profil', '2026-09-15 10:00:00', 1, 1, 0, 1000000)");

                db.WalletTransactionRecords.Add(new WalletTransactionRecord
                {
                    CharacterId = 90073315,
                    TransactionId = 3001,
                    TypeId = 44992,
                    Date = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Unspecified),
                    IsBuy = true,
                    IsPersonal = true,
                    JournalRefId = 888,
                    LocationId = 60003760,
                    Quantity = 10,
                    UnitPrice = 100
                });

                await db.SaveChangesAsync();
            }

            // Phase 2: auf die neueste Migration aktualisieren.
            await using (var db = CreateDb($"Data Source={dbPath}"))
            {
                await db.Database.GetService<IMigrator>().MigrateAsync();

                // Nutzerdaten unangetastet.
                Assert.Equal(1, await db.WalletTransactionRecords.CountAsync());

                // Bestandsprofil erhalten, neue Spalten mit neutralen Defaults.
                var profile = await db.TradeProfiles.SingleAsync();
                Assert.Equal("Mein Profil", profile.Name);
                Assert.Equal(1_000_000m, profile.MaxCapital);
                Assert.True(profile.AllowHighSec);
                Assert.False(profile.AllowNullSec);

                Assert.Equal(0, profile.CooldownMinutes);
                Assert.False(profile.SignalDeactivated);
                Assert.Null(profile.LastReportedFingerprint);
                Assert.Null(profile.LastReportedAt);

                // Neue Spalten sind persistierbar (Signal-Zustand roundtrip).
                profile.CooldownMinutes = 30;
                profile.SignalDeactivated = true;
                profile.LastReportedFingerprint = "ABC123";
                profile.LastReportedAt = new DateTime(2026, 9, 16, 12, 0, 0, DateTimeKind.Utc);
                await db.SaveChangesAsync();

                var reloaded = await db.TradeProfiles.AsNoTracking().SingleAsync();
                Assert.Equal(30, reloaded.CooldownMinutes);
                Assert.True(reloaded.SignalDeactivated);
                Assert.Equal("ABC123", reloaded.LastReportedFingerprint);
                Assert.NotNull(reloaded.LastReportedAt);
            }
        }
        finally
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }
}