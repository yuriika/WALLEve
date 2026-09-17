using Microsoft.EntityFrameworkCore;
using WALLEve.Models.Database;
using WALLEve.Models.Holdings;
using WALLEve.Models.Portfolio;
using WALLEve.Models.Stockpiles;

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
    public DbSet<CostBasisLedgerEntry> CostBasisLedgerEntries { get; set; } = null!;
    public DbSet<BackgroundJob> BackgroundJobs { get; set; } = null!;

    // Key-Value App-Einstellungen (überleben Neustarts)
    public DbSet<AppSetting> AppSettings { get; set; } = null!;

    // Hub-/Vergleichsmarkt-Profile (Bewertung, M1 #58)
    public DbSet<MarketHubProfile> MarketHubProfiles { get; set; } = null!;

    // Holdings tables (Rohdaten-Schema, M1): Owner/SyncRun/Snapshot/HoldingItem
    public DbSet<HoldingSyncRun> HoldingSyncRuns { get; set; } = null!;
    public DbSet<HoldingSnapshot> HoldingSnapshots { get; set; } = null!;
    public DbSet<HoldingItem> HoldingItems { get; set; } = null!;

    // Portfolio tables (M1): historische Punkte zu vollständigen Holdings-Snapshots
    public DbSet<PortfolioSnapshot> PortfolioSnapshots { get; set; } = null!;

    // Portfolio history points (M4 #38): eingefrorene Wert-Auswertungen kompletter
    // Snapshots — Assets/Escrow/Basis/Unknown/Cashflow/Realisiert getrennt.
    public DbSet<PortfolioHistoryPoint> PortfolioHistoryPoints { get; set; } = null!;

    // Stockpile tables (M2 #36): persistierte Ziele ohne Bestandsberechnung
    public DbSet<StockpileTarget> StockpileTargets { get; set; } = null!;

    // Trading contract table (M3 #37): unveränderliche Trade-Verträge mit
    // vollständig reproduzierbaren Eingaben; Kind ist Diskriminator.
    public DbSet<Models.Trading.TradeContract> TradeContracts { get; set; } = null!;

    // Trading profile table (M3 #44): validierte Handelsprofile mit harten
    // Grenzen (Kapital, Cargo, Sprünge, Security, Mindestvolumen/-gewinn,
    // Qualität) — angewendet VOR jeder Bewertung/Ranking.
    public DbSet<Models.Trading.TradeProfile> TradeProfiles { get; set; } = null!;

    // Trading status history table (M3 #45): unveränderliche Historie der
    // Statuswechsel von Trading-Empfehlungen mit Zeit und Quelle (Nutzer/System).
    public DbSet<Models.Trading.TradeStatusChange> TradeStatusChanges { get; set; } = null!;

    // Trading attribution tables (M3 #60): evidenzbasierte Zuordnung von
    // Wallet-Transaktionen zu Empfehlungen, explizite Transaktions-Links und die
    // unveränderliche Historie manueller Netto-Korrekturen.
    public DbSet<Models.Trading.RecommendationAttribution> RecommendationAttributions { get; set; } = null!;
    public DbSet<Models.Trading.AttributionTransactionLink> AttributionTransactionLinks { get; set; } = null!;
    public DbSet<Models.Trading.ActualNetCorrection> ActualNetCorrections { get; set; } = null!;

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

        // CostBasisLedgerEntry Configuration
        modelBuilder.Entity<CostBasisLedgerEntry>(entity =>
        {
            entity.HasKey(e => e.Id);

            // Idempotenz: dieselbe Quell-Transaktion pro Owner/Type wird nie
            // zweimal importiert (Duplicate-Import zum Schutz des Replays).
            entity.HasIndex(e => new { e.CharacterId, e.TypeId, e.SourceTransactionId })
                .IsUnique();

            // Chronologisches Replay pro Owner/Type.
            entity.HasIndex(e => new { e.CharacterId, e.TypeId, e.Date });
            entity.HasIndex(e => e.Date);
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

        // MarketHubProfile Configuration (#58): ein Profil je System, Rollen
        // als Flags. Die automatische Hub-Wahl liest nur IsActiveHub.
        modelBuilder.Entity<MarketHubProfile>(entity =>
        {
            entity.HasKey(e => e.Id);

            // Ein Hub-Profil pro System verhindert doppelte Distanz-Bezugspunkte.
            entity.HasIndex(e => e.SystemId)
                .IsUnique();

            // Schnelle Selektion aktivierter Hubs.
            entity.HasIndex(e => e.IsActiveHub);
            entity.HasIndex(e => e.IsComparisonMarket);
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

        // PortfolioSnapshot Configuration
        modelBuilder.Entity<PortfolioSnapshot>(entity =>
        {
            entity.HasKey(e => e.Id);

            // Ein historischer Punkt pro Quell-Snapshot: erneute Verarbeitung
            // derselben Quell-Snapshot-ID dupliziert keine Historie (#51).
            entity.HasIndex(e => e.HoldingSnapshotId)
                .IsUnique();

            // Der Portfolio-Punkt lebt mit seinem Quell-Snapshot.
            entity.HasOne(e => e.SourceSnapshot)
                .WithMany()
                .HasForeignKey(e => e.HoldingSnapshotId)
                .OnDelete(DeleteBehavior.Cascade);

            // Historische Punkte je Owner in zeitlicher Ordnung abfragbar.
            entity.HasIndex(e => new { e.OwnerType, e.OwnerId, e.CapturedAt });
        });

        // PortfolioHistoryPoint Configuration (#38): eingefrorene Wert-Auswertung
        // eines vollständigen Snapshots. Ein Punkt je Quell-Snapshot; spätere
        // Preise, Cost-Basis-Buchungen oder ein Marktwechsel überschreiben die
        // historische Provenienz nie.
        modelBuilder.Entity<PortfolioHistoryPoint>(entity =>
        {
            entity.HasKey(e => e.Id);

            // Ein Historien-Punkt pro Quell-Snapshot: erneute Verarbeitung
            // derselben Quell-Snapshot-ID erzeugt keinen zweiten Punkt.
            entity.HasIndex(e => e.HoldingSnapshotId)
                .IsUnique();

            // Der Punkt lebt mit seinem Quell-Snapshot.
            entity.HasOne(e => e.SourceSnapshot)
                .WithMany()
                .HasForeignKey(e => e.HoldingSnapshotId)
                .OnDelete(DeleteBehavior.Cascade);

            // Historische Punkte je Owner in zeitlicher Ordnung abfragbar.
            entity.HasIndex(e => new { e.OwnerType, e.OwnerId, e.CapturedAt });
        });

        // StockpileTarget Configuration (#36): owner-scoped Ziele, optional
        // orts-/container-begrenzt, mit Notiz und Archivstatus.
        modelBuilder.Entity<StockpileTarget>(entity =>
        {
            entity.HasKey(e => e.Id);

            // Identische Type-Ziele werden nicht versehentlich dupliziert:
            // ein aktives Ziel je Owner/Type. Archivierte Ziele geben ihren
            // Slot frei (partieller Unique-Index).
            entity.HasIndex(e => new { e.OwnerType, e.OwnerId, e.TypeId })
                .IsUnique()
                .HasFilter("[IsArchived] = 0");

            // Owner-Isolation: Ziele je Owner lesbar, Archivstatus filterbar.
            entity.HasIndex(e => new { e.OwnerType, e.OwnerId, e.IsArchived });

            entity.HasIndex(e => e.TypeId);
            entity.HasIndex(e => e.LocationId);
        });

        // Trading Contracts (M3 #37): unveränderliche Reproduktions-Archive einer
        // Trade-Analyse — eine Tabelle, Kind als Diskriminator (TPC ist auf SQLite
        // wegen fehlender Sequence-Schlüsselgenerierung nicht abbildbar). Bewusst
        // KEINE FK-Relation zu TradingOpportunities: Verträge sind Archive; das
        // Löschen abgelaufener Opportunities (Analyse-Hygiene) darf sie nicht mitreißen.
        modelBuilder.Entity<Models.Trading.TradeContract>(entity =>
        {
            entity.HasKey(e => e.Id);

            // Verträge je Owner/Art/Zeit und je Quell-Opportunity abfragbar.
            entity.HasIndex(e => new { e.CharacterId, e.Kind, e.CreatedAt });
            entity.HasIndex(e => new { e.TradingOpportunityId, e.Kind });

            // Quote-Quellen (Snapshots) direkt nachschlagbar.
            entity.HasIndex(e => e.MarketSnapshotId);
            entity.HasIndex(e => e.BuyMarketSnapshotId);
            entity.HasIndex(e => e.SellMarketSnapshotId);
        });

        // Trading Profile Configuration (#44): genau EIN validiertes Profil je
        // Charakter — Owner-Isolation über den Unique-Index auf CharacterId.
        // Ein Profil eines Owners kann die Grenzen eines anderen Owners nie
        // beeinflussen; keine FK-Relation (Profile leben unabhängig von der
        // Opportunity-Hygiene).
        modelBuilder.Entity<Models.Trading.TradeProfile>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => e.CharacterId)
                .IsUnique();

            // Profile je Charakter auflisten und nach Aktualität sortieren.
            entity.HasIndex(e => new { e.CharacterId, e.UpdatedAt });
        });

        // Trading Status History Configuration (#45): append-only Protokoll der
        // Statuswechsel einer Empfehlung. Bewusst KEINE FK-Relation zu
        // TradingOpportunities: Ablauf/Invalidierung löscht die Empfehlung nie,
        // aber die Historie überlebt auch die Opportunity-Hygiene — sie ist ein
        // unveränderliches Archiv (wie TradeContracts, #37). Owner-Isolation
        // über den denormalisierten CharacterId.
        modelBuilder.Entity<Models.Trading.TradeStatusChange>(entity =>
        {
            entity.HasKey(e => e.Id);

            // Historie je Empfehlung und je Owner (Owner-Isolation) abfragbar.
            entity.HasIndex(e => new { e.TradingOpportunityId, e.ChangedAt });
            entity.HasIndex(e => new { e.CharacterId, e.ChangedAt });

            // Quellen-/Statusfilter für Auswertungen.
            entity.HasIndex(e => new { e.ToStatus, e.Source });
        });

        // Trading-Attribution (#60): eine Zuordnung je Empfehlung und Owner.
        // Wie bei #45 bewusst KEINE FK-Relation zur Opportunity (Owner-Isolation
        // über den denormalisierten CharacterId), damit die Zuordnung erhalten bleibt,
        // wenn die Opportunity-Hygiene greift.
        modelBuilder.Entity<Models.Trading.RecommendationAttribution>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => new { e.TradingOpportunityId, e.CharacterId })
                .IsUnique();

            entity.HasIndex(e => new { e.CharacterId, e.MatchState });
            entity.HasIndex(e => new { e.TypeId, e.Side, e.WindowStartUtc });
        });

        // Explizite Transaktions-Links: ein Link je Zuordnung und Transaktion.
        // Der Index auf (TransactionId, CharacterId) ist die Grundlage der
        // Verbrauchsberechnung — eine Transaktion kann so nicht zwei Empfehlungen
        // voll zugerechnet werden.
        modelBuilder.Entity<Models.Trading.AttributionTransactionLink>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => new { e.AttributionId, e.TransactionId })
                .IsUnique();

            entity.HasIndex(e => new { e.CharacterId, e.TransactionId });
            entity.HasIndex(e => new { e.TradingOpportunityId, e.TransactionDate });
        });

        // Unveränderliche Historie der manuellen Netto-Korrekturen.
        modelBuilder.Entity<Models.Trading.ActualNetCorrection>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => new { e.AttributionId, e.CorrectedAt });
            entity.HasIndex(e => new { e.CharacterId, e.CorrectedAt });
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
