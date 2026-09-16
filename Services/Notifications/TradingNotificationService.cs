using System.Globalization;
using WALLEve.Models.Database;

namespace WALLEve.Services.Notifications;

/// <summary>Konfiguration des In-App-Meldungsfeeds (Issue #70).</summary>
public sealed class TradingNotificationOptions
{
    /// <summary>Minuten, die eine Chance nach ihrer Meldung für Wiederholungen gesperrt ist.</summary>
    public int CooldownMinutes { get; set; } = 30;

    /// <summary>Maximale Anzahl behaltener Meldungen pro Charakter (Ring, älteste werden verworfen).</summary>
    public int MaxFeedSize { get; set; } = 60;
}

/// <summary>
/// Singleton-Meldungsfeed pro Charakter (Issue #70). Thread-sicher über einen
/// gemeinsamen Lock; Dedupe-Key ist der Inhalt der Chance (Art, Item, Orte,
/// gerundeter Score/Gewinn — Materialität wie beim Signal-Fingerprint aus #68),
/// nicht die DB-Id, damit Wiederholungsanalysen keine Duplikate erzeugen.
/// Owner-Isolation: Fremde Chancen werden nie veröffentlicht.
/// </summary>
public sealed class TradingNotificationService : ITradingNotificationService
{
    private readonly TimeSpan _cooldown;
    private readonly int _maxFeedSize;
    private readonly object _gate = new();
    private readonly Dictionary<int, CharacterFeed> _feeds = new();

    public TradingNotificationService(TradingNotificationOptions? options = null)
    {
        var o = options ?? new TradingNotificationOptions();
        _cooldown = TimeSpan.FromMinutes(Math.Max(0, o.CooldownMinutes));
        _maxFeedSize = Math.Max(1, o.MaxFeedSize);
    }

    public int Publish(int characterId, IReadOnlyList<TradingOpportunity> opportunities, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(opportunities);
        if (opportunities.Count == 0)
            return 0;

        lock (_gate)
        {
            var feed = GetOrCreateFeed(characterId, _cooldown);
            var published = 0;
            foreach (var opp in opportunities)
            {
                // Owner-Isolation: Nur Chancen des angefragten Charakters.
                if (opp.CharacterId != characterId)
                    continue;

                var key = BuildDedupeKey(opp);
                if (feed.IsSuppressed(key, utcNow))
                    continue; // Dedupe oder Cooldown aktiv

                feed.Add(key, opp, utcNow);
                published++;
            }

            return published;
        }
    }

    public IReadOnlyList<TradingNotification> GetAll(int characterId)
    {
        lock (_gate)
            return GetOrCreateFeed(characterId, _cooldown).Snapshot();
    }

    public IReadOnlyList<TradingNotification> GetUnseen(int characterId)
    {
        lock (_gate)
            return GetOrCreateFeed(characterId, _cooldown).SnapshotUnseen();
    }

    public void MarkAllSeen(int characterId)
    {
        lock (_gate)
            GetOrCreateFeed(characterId, _cooldown).MarkAllSeen();
    }

    // ------------------------------------------------------------------

    private CharacterFeed GetOrCreateFeed(int characterId, TimeSpan cooldown)
    {
        if (!_feeds.TryGetValue(characterId, out var feed))
        {
            feed = new CharacterFeed(_maxFeedSize, cooldown);
            _feeds[characterId] = feed;
        }

        return feed;
    }

    /// <summary>
    /// Inhaltlicher Dedupe-Key: Art + Item + Orte + gerundeter Score/Gewinn.
    /// Bewusst ohne DB-Id und ohne Zeitstempel — eine erneute Analyse derselben
    /// Chance erzeugt denselben Key und wird vom Cooldown unterdrückt.
    /// </summary>
    internal static string BuildDedupeKey(TradingOpportunity opp)
        => FormattableString.Invariant(
            $"{opp.OpportunityType}|{opp.TypeId}|{opp.BuyLocationId}|{opp.SellLocationId}|{Math.Round(opp.Score)}|{Math.Round(opp.EstimatedProfit)}");

    private sealed class CharacterFeed
    {
        private readonly int _maxSize;
        private readonly TimeSpan _cooldown;
        private readonly List<TradingNotification> _items = new();
        private readonly Dictionary<string, DateTime> _lastEmittedUtc = new();
        private long _nextId = 1;

        public CharacterFeed(int maxSize, TimeSpan cooldown)
        {
            _maxSize = maxSize;
            _cooldown = cooldown;
        }

        public bool IsSuppressed(string key, DateTime utcNow)
            => _lastEmittedUtc.TryGetValue(key, out var last) && last + _cooldown > utcNow;

        public void Add(string key, TradingOpportunity opp, DateTime utcNow)
        {
            _items.Insert(0, new TradingNotification
            {
                Id = _nextId++,
                CharacterId = opp.CharacterId,
                OpportunityId = opp.Id,
                OpportunityType = opp.OpportunityType,
                Title = BuildTitle(opp),
                Message = BuildMessage(opp),
                LinkUrl = $"/trading#opp-{opp.Id}",
                DetectedAtUtc = opp.DetectedAt.Kind == DateTimeKind.Utc
                    ? opp.DetectedAt
                    : DateTime.SpecifyKind(opp.DetectedAt, DateTimeKind.Utc),
            });

            while (_items.Count > _maxSize)
                _items.RemoveAt(_items.Count - 1);

            _lastEmittedUtc[key] = utcNow;
        }

        public IReadOnlyList<TradingNotification> Snapshot() => _items.ToArray();
        public IReadOnlyList<TradingNotification> SnapshotUnseen() => _items.Where(n => !n.IsSeen).ToArray();

        public void MarkAllSeen()
        {
            foreach (var item in _items)
                item.IsSeen = true;
        }

        private static string BuildTitle(TradingOpportunity opp) => opp.OpportunityType switch
        {
            "inventory_sell" => "Verkaufschance erkannt",
            "station_trading" => "Handelschance erkannt",
            "arbitrage" => "Arbitrage-Chance erkannt",
            "trend" => "Trend erkannt",
            _ => "Trading-Chance erkannt",
        };

        private static string BuildMessage(TradingOpportunity opp)
        {
            var profit = opp.EstimatedProfit.ToString("N0", CultureInfo.GetCultureInfo("de-DE"));
            return $"Geschätzter Gewinn: {profit} ISK (Score {opp.Score:N0}).";
        }
    }
}