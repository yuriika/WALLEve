using WALLEve.Data;
using WALLEve.Models.Trading;
using WALLEve.Services.Trading.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace WALLEve.Services.Trading;

/// <summary>
/// Implementierung der validierten Handelsprofil-Speicherung (Issue #44):
/// Bereichsvalidierung (Bereiche, Pflichtfelder, Security-Auswahl), Upsert je
/// Charakter und Owner-Isolation über den Unique-Index auf CharacterId.
/// </summary>
public sealed class TradeProfileService : ITradeProfileService
{
    private readonly WalletDbContext _db;

    public TradeProfileService(WalletDbContext db)
    {
        _db = db;
    }

    public TradeProfile? GetForCharacter(int characterId)
        => _db.TradeProfiles.AsNoTracking().SingleOrDefault(p => p.CharacterId == characterId);

    public SaveTradeProfileResult Save(TradeProfileCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = Validate(command);
        if (errors.Count > 0)
            return SaveTradeProfileResult.Failed(errors);

        var profile = _db.TradeProfiles.SingleOrDefault(p => p.CharacterId == command.CharacterId);
        if (profile is null)
        {
            profile = new TradeProfile { CharacterId = command.CharacterId };
            _db.TradeProfiles.Add(profile);
        }

        Apply(profile, command);
        profile.UpdatedAt = DateTime.UtcNow;
        _db.SaveChanges();

        return SaveTradeProfileResult.Ok(profile);
    }

    private static void Apply(TradeProfile profile, TradeProfileCommand command)
    {
        profile.Name = command.Name.Trim();
        profile.MaxCapital = command.MaxCapital;
        profile.MaxCargoVolume = command.MaxCargoVolume;
        profile.MaxJumps = command.MaxJumps;
        profile.AllowHighSec = command.AllowHighSec;
        profile.AllowLowSec = command.AllowLowSec;
        profile.AllowNullSec = command.AllowNullSec;
        profile.MinVolumeM3 = command.MinVolumeM3;
        profile.MinProfit = command.MinProfit;
        profile.MinQualityScore = command.MinQualityScore;
    }

    private static List<string> Validate(TradeProfileCommand command)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(command.Name))
            errors.Add("Profilname ist erforderlich.");

        if (command.MaxCapital is < 0)
            errors.Add("MaxCapital darf nicht negativ sein.");
        if (command.MaxCargoVolume is < 0)
            errors.Add("MaxCargoVolume darf nicht negativ sein.");
        if (command.MaxJumps is < 0)
            errors.Add("MaxJumps darf nicht negativ sein.");
        if (command.MinVolumeM3 is < 0)
            errors.Add("MinVolumeM3 darf nicht negativ sein.");
        if (command.MinProfit is < 0)
            errors.Add("MinProfit darf nicht negativ sein.");
        if (command.MinQualityScore is < 0 or > 100)
            errors.Add("MinQualityScore muss zwischen 0 und 100 liegen.");

        if (!command.AllowHighSec && !command.AllowLowSec && !command.AllowNullSec)
            errors.Add("Mindestens eine Security-Zone muss erlaubt sein.");

        return errors;
    }
}