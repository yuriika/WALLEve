using Microsoft.EntityFrameworkCore;
using WALLEve.Models.Database;

namespace WALLEve.Data;

/// <summary>
/// DbContext für die Wallet-Datenbank (wallet.db)
/// Separate DB für App-Daten, NICHT die SDE-DB!
/// </summary>
public class WalletDbContext : DbContext
{
    public DbSet<WalletCharacter> Characters { get; set; } = null!;
    public DbSet<WalletCorporation> Corporations { get; set; } = null!;
    public DbSet<WalletEntryLink> Links { get; set; } = null!;

    public WalletDbContext(DbContextOptions<WalletDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // WalletCharacter Configuration
        modelBuilder.Entity<WalletCharacter>(entity =>
        {
            entity.HasKey(e => e.CharacterId);
            entity.HasIndex(e => e.CharacterName);
            entity.HasIndex(e => e.LastSyncedAt);

            entity.HasMany(e => e.Links)
                .WithOne(l => l.Character)
                .HasForeignKey(l => l.CharacterId)
                .OnDelete(DeleteBehavior.Cascade); // Delete links wenn Character gelöscht wird
        });

        // WalletCorporation Configuration
        modelBuilder.Entity<WalletCorporation>(entity =>
        {
            entity.HasKey(e => e.CorporationId);
            entity.HasIndex(e => e.CorporationName);
            entity.HasIndex(e => e.LastSyncedAt);

            entity.HasMany(e => e.Links)
                .WithOne(l => l.Corporation)
                .HasForeignKey(l => l.CorporationId)
                .OnDelete(DeleteBehavior.Cascade); // Delete links wenn Corp gelöscht wird
        });

        // WalletEntryLink Configuration
        modelBuilder.Entity<WalletEntryLink>(entity =>
        {
            entity.HasKey(e => e.Id);

            // Tabellenkonfiguration mit Check Constraints
            entity.ToTable("Links", t =>
            {
                // Check Constraint: Entweder Character ODER Corporation, nicht beides
                t.HasCheckConstraint(
                    "CK_WalletEntryLink_CharacterOrCorp",
                    "(CharacterId IS NOT NULL AND CorporationId IS NULL) OR (CharacterId IS NULL AND CorporationId IS NOT NULL)"
                );

                // Check Constraint: Division nur für Corp Wallets
                t.HasCheckConstraint(
                    "CK_WalletEntryLink_DivisionForCorpOnly",
                    "(CorporationId IS NOT NULL AND Division BETWEEN 1 AND 7) OR (CorporationId IS NULL AND Division IS NULL)"
                );
            });

            // Composite Index für schnelle Bidirectional-Lookups
            entity.HasIndex(e => new { e.SourceEntryId, e.TargetEntryId })
                .IsUnique(); // Ein Link zwischen zwei Entries nur einmal

            // Index für Character-basierte Queries
            entity.HasIndex(e => new { e.CharacterId, e.SourceEntryId });
            entity.HasIndex(e => new { e.CharacterId, e.TargetEntryId });

            // Index für Corp-basierte Queries
            entity.HasIndex(e => new { e.CorporationId, e.Division, e.SourceEntryId });
            entity.HasIndex(e => new { e.CorporationId, e.Division, e.TargetEntryId });

            // Index für Type/Confidence Queries
            entity.HasIndex(e => e.Type);
            entity.HasIndex(e => e.Confidence);
            entity.HasIndex(e => e.CreatedAt);

            // Index für manuelle Überprüfung
            entity.HasIndex(e => e.IsManuallyVerified);
            entity.HasIndex(e => e.IsManuallyRejected);
        });
    }

    /// <summary>
    /// Initialisiert die Datenbank (erstellt wenn nicht vorhanden)
    /// </summary>
    public async Task InitializeDatabaseAsync()
    {
        try
        {
            // Erstellt DB wenn nicht vorhanden, führt Migrations aus
            await Database.MigrateAsync();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to initialize Wallet database", ex);
        }
    }
}
