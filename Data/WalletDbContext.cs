using Microsoft.EntityFrameworkCore;
using WALLEve.Models.Database;
using WALLEve.Models.Holdings;

namespace WALLEve.Data;

/// <summary>
/// DbContext für die Wallet-Datenbank (wallet.db)
/// Separate DB für App-Daten, NICHT die SDE-DB!
/// </summary>
public class WalletDbContext : DbContext
{
    // Wallet-related tables
    public DbSet<WalletCharacter> Characters { get; set; } = null!;
    public DbSet<WalletCorporation> Corporations { get; set; } = null!;
    public DbSet<WalletEntryLink> Links { get; set; } = null!;

    // Market Analysis tables
    public DbSet<MarketSnapshot> MarketSnapshots { get; set; } = null!;
    public DbSet<MarketHistory> MarketHistory { get; set; } = null!;
    public DbSet<TradingOpportunity> TradingOpportunities { get; set; } = null!;
    public DbSet<MarketTrend> MarketTrends { get; set; } = null!;

    public DbSet<MarketFavorit> MarketFavorits { get; set; } = null!;

    // Cost-Basis + Background-Task tables
    public DbSet<WalletTransactionRecord> WalletTransactionRecords { get; set; } = null!;
    public DbSet<CostBasisEntry> CostBasisEntries { get; set; } = null!;
    public DbSet<BackgroundJob> BackgroundJobs { get; set; } = null!;

    // Key-Value App-Einstellungen (überleben Neustarts)
    public DbSet<AppSetting> AppSettings { get; set; } = null!;

    // Holdings tables (Rohdaten-Schema, M1): Owner/SyncRun/Snapshot/HoldingItem
    public DbSet<HoldingSyncRun> HoldingSyncRuns { get; set; } = null!;
    public DbSet<HoldingSnapshot> HoldingSnapshots { get; set; } = null!;
    public DbSet<HoldingItem> HoldingItems { get; set; } = null!;

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

        // MarketSnapshot Configuration
        modelBuilder.Entity<MarketSnapshot>(entity =>
        {
            entity.HasKey(e => e.Id);

            // Composite Index für schnelle Lookups: Region + Type + Time
            entity.HasIndex(e => new { e.RegionId, e.TypeId, e.Timestamp });

            // Index für Time-based Queries (Cleanup alter Daten)
            entity.HasIndex(e => e.Timestamp);
        });

        // MarketHistory Configuration
        modelBuilder.Entity<MarketHistory>(entity =>
        {
            entity.HasKey(e => e.Id);

            // Composite Index für Region + Type + Date Queries
            entity.HasIndex(e => new { e.RegionId, e.TypeId, e.Date });

            // Index für Type-based Queries (Trend Analysis)
            entity.HasIndex(e => new { e.TypeId, e.Date });

            // Unique Constraint: Ein Eintrag pro Region/Type/Date
            entity.HasIndex(e => new { e.RegionId, e.TypeId, e.Date })
                .IsUnique();
        });

        // TradingOpportunity Configuration
        modelBuilder.Entity<TradingOpportunity>(entity =>
        {
            entity.HasKey(e => e.Id);

            // Index für Character-basierte Bestands-Opportunities
            entity.HasIndex(e => new { e.CharacterId, e.Status, e.OpportunityType });

            // Index für Status + Expires Queries (aktive Opportunities)
            entity.HasIndex(e => new { e.Status, e.ExpiresAt });

            // Index für Type-based Queries
            entity.HasIndex(e => new { e.TypeId, e.Status });

            // Index für Time-based Queries
            entity.HasIndex(e => e.DetectedAt);

            // Index für Performance Tracking
            entity.HasIndex(e => new { e.Status, e.Score });

            // Index für Region-based Queries
            entity.HasIndex(e => e.BuyRegionId);
            entity.HasIndex(e => e.SellRegionId);
        });

        // MarketTrend Configuration
        modelBuilder.Entity<MarketTrend>(entity =>
        {
            entity.HasKey(e => e.Id);

            // Index für Type + Analyzed Time Queries
            entity.HasIndex(e => new { e.TypeId, e.AnalyzedAt });

            // Index für Trend Type + Strength Queries
            entity.HasIndex(e => new { e.TrendType, e.Strength });

            // Index für Region-based Queries
            entity.HasIndex(e => new { e.RegionId, e.TypeId });
        });

        // MarketFavorit Configuration
        modelBuilder.Entity<MarketFavorit>(entity =>
        {
            entity.HasKey(e => new { e.CharacterId , e.TypeId});
            entity.HasOne<WalletCharacter>()
                .WithMany()
                .HasForeignKey(e => e.CharacterId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.CharacterId);
        });

        // WalletTransactionRecord Configuration
        modelBuilder.Entity<WalletTransactionRecord>(entity =>
        {
            entity.HasKey(e => e.Id);

            // Eindeutige Transaktions-ID pro Charakter (Dedup beim Spiegeln)
            entity.HasIndex(e => new { e.CharacterId, e.TransactionId })
                .IsUnique();

            entity.HasIndex(e => new { e.CharacterId, e.TypeId, e.IsBuy });
            entity.HasIndex(e => e.Date);
        });

        // CostBasisEntry Configuration
        modelBuilder.Entity<CostBasisEntry>(entity =>
        {
            entity.HasKey(e => e.Id);

            // Ein Eintrag pro Charakter/Item
            entity.HasIndex(e => new { e.CharacterId, e.TypeId })
                .IsUnique();

            entity.HasIndex(e => e.Source);
            entity.HasIndex(e => e.UpdatedAt);
        });

        // BackgroundJob Configuration
        modelBuilder.Entity<BackgroundJob>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.JobType, e.Status });
            entity.HasIndex(e => e.UpdatedAt);
        });

        // AppSetting Configuration
        modelBuilder.Entity<AppSetting>(entity =>
        {
            entity.HasKey(e => e.Key);
        });

        // HoldingSyncRun Configuration
        modelBuilder.Entity<HoldingSyncRun>(entity =>
        {
            entity.HasKey(e => e.Id);

            // Zusammengesetzter Owner-Schlüssel: gleiche Item-IDs verschiedener
            // Owner kollidieren nie. Index für Lauf-Historie je Owner.
            entity.HasIndex(e => new { e.OwnerType, e.OwnerId, e.StartedAt });
        });

        // HoldingSnapshot Configuration
        modelBuilder.Entity<HoldingSnapshot>(entity =>
        {
            entity.HasKey(e => e.Id);

            // Ein Lauf kann mehrere Snapshots erzeugen; löschen des Laufs
            // entfernt auch seine Abbilder (Rohdaten ohne Eigenleben).
            entity.HasOne(e => e.SyncRun)
                .WithMany(s => s.Snapshots)
                .HasForeignKey(e => e.SyncRunId)
                .OnDelete(DeleteBehavior.Cascade);

            // Denormalisierter Owner-Schlüssel für direkte Abfragen je Owner.
            entity.HasIndex(e => new { e.OwnerType, e.OwnerId, e.SyncedAt });
        });

        // HoldingItem Configuration
        modelBuilder.Entity<HoldingItem>(entity =>
        {
            entity.HasKey(e => e.Id);

            // Rohzeilen gehören zu genau einem Snapshot; Snapshot-Löschung
            // entfernt die Rohdimensionen (additiv, keine Nutzerdaten betroffen).
            entity.HasOne(e => e.Snapshot)
                .WithMany(s => s.Items)
                .HasForeignKey(e => e.SnapshotId)
                .OnDelete(DeleteBehavior.Cascade);

            // Kein Unique-Index auf ItemId: dieselbe ItemId kann in
            // verschiedenen Snapshot-/Owner-Kontexten existieren.
            entity.HasIndex(e => new { e.SnapshotId, e.TypeId });
            entity.HasIndex(e => new { e.TypeId, e.IsSingleton });
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
