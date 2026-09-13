using WALLEve.Models.Trading;

namespace WALLEve.Services.Trading.Interfaces;

/// <summary>Speicherbefehl für ein Handelsprofil (upsert je Charakter, Issue #44).</summary>
public sealed record TradeProfileCommand(
    int CharacterId,
    string Name,
    decimal? MaxCapital,
    decimal? MaxCargoVolume,
    int? MaxJumps,
    bool AllowHighSec,
    bool AllowLowSec,
    bool AllowNullSec,
    decimal? MinVolumeM3,
    decimal? MinProfit,
    int? MinQualityScore);

/// <summary>Ergebnis eines Speicherversuchs; bei Fehlern keine Persistenz.</summary>
public sealed record SaveTradeProfileResult(bool Success, TradeProfile? Profile, IReadOnlyList<string> ValidationErrors)
{
    public static SaveTradeProfileResult Ok(TradeProfile profile) => new(true, profile, Array.Empty<string>());

    public static SaveTradeProfileResult Failed(IEnumerable<string> errors) => new(false, null, errors.ToList());
}

/// <summary>
/// Persistiert validierte Handelsprofile (Issue #44): genau ein Profil je
/// Charakter (Owner-Isolation — Werte anderer Charaktere werden nie gelesen
/// oder überschrieben) mit Bereichsvalidierung vor jedem Speichern.
/// </summary>
public interface ITradeProfileService
{
    /// <summary>Profil des Charakters; null, wenn keines gespeichert ist.</summary>
    TradeProfile? GetForCharacter(int characterId);

    /// <summary>Validiert und speichert das Profil (upsert je CharacterId).</summary>
    SaveTradeProfileResult Save(TradeProfileCommand command);
}