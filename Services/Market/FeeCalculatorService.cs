using Microsoft.Extensions.Options;
using WALLEve.Models.Esi.Character;
using WALLEve.Models.Market;
using WALLEve.Services.Market.Interfaces;

namespace WALLEve.Services.Market;

/// <summary>
/// Gebührenberechnung nach offiziellen EVE-Formeln (support.eveonline.com,
/// Artikel "Broker Fee and Sales Tax" + "Buy and Sell Orders"):
///
/// BROKER FEE (Order-Erstellung, NPC-Station):
///   3% − 0.3%×Broker-Relations-Level − 0.03%×Faction-Standing − 0.02%×Corp-Standing,
///   Minimum 1%. Upwell-Strukturen: abweichend (0.5% NPC-Sink + Owner-Anteil,
///   Broker Relations greift nicht) — hier nicht modelliert; Strukturen werden
///   bereits in der Ortsauflösung (#28) als keine Handelsposten blockiert.
///
/// SALES TAX (nach Verkauf):
///   7,5% Basis (seit 2025-03), −11% relativ pro Accounting-Level, Minimum 3,37%.
///
/// MODIFY-FEE (Preisänderung P1→P2, offiziell):
///   Fee = max(0, BR×(P2−P1)) + (1−RD)×BR×P2
///   BR = effektive Broker-Rate, RD = Relist-Discount (Advanced Broker Relations,
///   Wiki: 50% Basis + 6% je Level), Minimum 100 ISK.
///   → Preissenkung ist deutlich günstiger als Preiserhöhung (keine Differenz-Fee).
///
/// Herkunftstreue (Issue #46): Jede Eingabe trägt eine FeeInputOrigin
/// (Automatic/ManualOverride/Estimated/Unknown). Fehlende Skills oder Standings
/// ergeben eine KONSERVATIVE Schätzung (maximaler Satz), die im Ergebnis und in
/// der Persistenz als "geschätzt" ausgewiesen wird — nie ein stiller
/// Null-Fallback. Manuelle Overrides (Sektion "FeeCalculator") sind explizit
/// als ManualOverride nachvollziehbar. Eine ungültige/unbekannte notwendige
/// Eingabe setzt HasUnknownInput und blockiert die präzise Berechnung.
/// </summary>
public class FeeCalculatorService : IFeeCalculatorService
{
    // SDE-verifizierte Skill-IDs (invTypes): 3444 wäre "Retail" — nicht Broker Relations!
    private const int BrokerRelationsSkillId = 3446;
    private const int AdvancedBrokerRelationsSkillId = 16597;
    private const int AccountingSkillId = 16622;

    private const double BrokerFeeBase = 0.03;          // NPC-Station
    private const double BrokerFeePerLevel = 0.003;
    private const double BrokerFeeMin = 0.01;           // bei max Standing+Skill
    // Maximaler Standing-Rabatt aus der offiziellen Formel (0,03 % je
    // Faction-Punkt + 0,02 % je Corp-Punkt, Standings 0..10) = 0,5 %.
    // Standings sind über ESI nicht belegbar — der geschätzte Anteil ist
    // deshalb eine begrenzte Spanne [Satz − MaxRabatt, Satz].
    private const double MaxStandingsDiscount = 0.005;
    private const double SalesTaxBase = 0.075;          // seit 2025-03-12 (vorher 4%)
    private const double SalesTaxReductionPerLevel = 0.11; // relativ pro Accounting-Level
    private const double SalesTaxMin = 0.0337;

    // Relist-Discount: 50% Basis + 6% je Advanced-Broker-Relations-Level
    private const double RelistDiscountBase = 0.50;
    private const double RelistDiscountPerLevel = 0.06;
    private const double ModifyFeeMinimum = 100.0;

    private readonly FeeOverrideSettings _overrides;

    public FeeCalculatorService()
        : this(Options.Create(new FeeOverrideSettings()))
    {
    }

    public FeeCalculatorService(IOptions<FeeOverrideSettings> options)
    {
        _overrides = options?.Value ?? new FeeOverrideSettings();
    }

    /// <summary>
    /// Ermittelt die effektiven Gebühren-Eingaben MIT Herkunft je Eingabe.
    /// Das ist die einzige Quelle der Wahrheit; alle Berechnungsmethoden
    /// leiten ihre Sätze daraus ab.
    /// </summary>
    public FeeProfile BuildFeeProfile(CharacterSkills? skills)
    {
        var profile = new FeeProfile { EvaluatedAtUtc = DateTime.UtcNow };

        ResolveBrokerRate(skills, profile);
        ResolveSalesTaxRate(skills, profile);
        ResolveRelistDiscount(skills, profile);
        return profile;
    }

    private void ResolveBrokerRate(CharacterSkills? skills, FeeProfile profile)
    {
        if (_overrides.BrokerFeeRate is { } brokerOverride)
        {
            if (IsValidRate(brokerOverride))
            {
                // Override ersetzt Skill- UND Standing-Anteil vollständig.
                profile.BrokerFeeRate = brokerOverride;
                profile.BrokerRateOrigin = FeeInputOrigin.ManualOverride;
                profile.StandingsOrigin = FeeInputOrigin.ManualOverride;
                profile.BrokerFeeRateBestCase = brokerOverride; // belegt, keine Spanne
                return;
            }

            profile.BrokerFeeRate = GetConservativeBrokerRate(skills);
            profile.BrokerRateOrigin = FeeInputOrigin.Unknown;
            profile.StandingsOrigin = FeeInputOrigin.Unknown;
            profile.BrokerFeeRateBestCase = profile.BrokerFeeRate;
            return;
        }

        var level = GetSkillLevel(skills, BrokerRelationsSkillId);
        var skillBased = Math.Max(BrokerFeeMin, BrokerFeeBase - (level * BrokerFeePerLevel));

        // Standings (Faction/Corp) liegen über ESI nicht belegbar vor (Scope
        // angefragt, Endpoint nicht implementiert). Ohne manuellen Override ist
        // der Anteil 0 — eine KONSERVATIVE Schätzung (maximaler Satz), explizit
        // als Estimated ausgewiesen, kein stiller garantiert korrekter Wert.
        if (_overrides.StandingsDiscount is { } standingDiscount)
        {
            if (IsValidRate(standingDiscount))
            {
                profile.BrokerFeeRate = Math.Max(BrokerFeeMin, skillBased - standingDiscount);
                profile.BrokerRateOrigin = skills?.Skills != null
                    ? FeeInputOrigin.Automatic
                    : FeeInputOrigin.Estimated;
                profile.StandingsOrigin = FeeInputOrigin.ManualOverride;
                profile.BrokerFeeRateBestCase = profile.BrokerFeeRate; // belegt, keine Spanne
                return;
            }

            profile.BrokerFeeRate = skillBased;
            profile.BrokerRateOrigin = skills?.Skills != null
                ? FeeInputOrigin.Automatic
                : FeeInputOrigin.Estimated;
            profile.StandingsOrigin = FeeInputOrigin.Unknown;
            profile.BrokerFeeRateBestCase = profile.BrokerFeeRate;
            return;
        }

        profile.BrokerFeeRate = skillBased;
        profile.BrokerRateOrigin = skills?.Skills != null
            ? FeeInputOrigin.Automatic
            : FeeInputOrigin.Estimated;
        profile.StandingsOrigin = FeeInputOrigin.Estimated;
        // Geschätzter Standing-Anteil → begrenzte Spanne statt falsch exaktem
        // Wert: Best-Case = konservativer Satz minus maximalem Standing-Rabatt.
        profile.BrokerFeeRateBestCase = Math.Max(BrokerFeeMin, skillBased - MaxStandingsDiscount);
    }

    private void ResolveSalesTaxRate(CharacterSkills? skills, FeeProfile profile)
    {
        if (_overrides.SalesTaxRate is { } taxOverride)
        {
            if (IsValidRate(taxOverride))
            {
                profile.SalesTaxRate = taxOverride;
                profile.SalesTaxOrigin = FeeInputOrigin.ManualOverride;
                return;
            }

            profile.SalesTaxRate = GetConservativeSalesTaxRate(skills);
            profile.SalesTaxOrigin = FeeInputOrigin.Unknown;
            return;
        }

        var level = GetSkillLevel(skills, AccountingSkillId);
        profile.SalesTaxRate = Math.Max(SalesTaxMin, SalesTaxBase * (1.0 - (level * SalesTaxReductionPerLevel)));
        profile.SalesTaxOrigin = skills?.Skills != null
            ? FeeInputOrigin.Automatic
            : FeeInputOrigin.Estimated;
    }

    private void ResolveRelistDiscount(CharacterSkills? skills, FeeProfile profile)
    {
        // Relist-Discount betrifft nur die Modify-Fee; ohne Override-Feld.
        var level = GetSkillLevel(skills, AdvancedBrokerRelationsSkillId);
        profile.RelistDiscountRate = Math.Min(1.0, RelistDiscountBase + (level * RelistDiscountPerLevel));
    }

    /// <summary>Konservativer (maximaler) Broker-Satz, wenn ein Override ungültig ist.</summary>
    private double GetConservativeBrokerRate(CharacterSkills? skills)
    {
        var level = GetSkillLevel(skills, BrokerRelationsSkillId);
        return Math.Max(BrokerFeeMin, BrokerFeeBase - (level * BrokerFeePerLevel));
    }

    /// <summary>Konservativer (maximaler) Sales-Tax-Satz, wenn ein Override ungültig ist.</summary>
    private double GetConservativeSalesTaxRate(CharacterSkills? skills)
    {
        var level = GetSkillLevel(skills, AccountingSkillId);
        return Math.Max(SalesTaxMin, SalesTaxBase * (1.0 - (level * SalesTaxReductionPerLevel)));
    }

    private static bool IsValidRate(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0.0 && value < 1.0;

    public double GetBrokerFeeRate(CharacterSkills? skills)
        => BuildFeeProfile(skills).BrokerFeeRate;

    public double GetSalesTaxRate(CharacterSkills? skills)
        => BuildFeeProfile(skills).SalesTaxRate;

    /// <summary>Relist-Discount (RD) aus Advanced Broker Relations.</summary>
    public double GetRelistDiscountRate(CharacterSkills? skills)
        => BuildFeeProfile(skills).RelistDiscountRate;

    /// <summary>
    /// Fee für eine Preisänderung P1→P2 (Preise pro Einheit, Menge multipliziert):
    /// Fee = max(0, BR×(Wert2−Wert1)) + (1−RD)×BR×Wert2, Minimum 100 ISK.
    /// </summary>
    public double CalculateOrderModifyFee(double oldPrice, double newPrice, int quantity, CharacterSkills? skills)
    {
        var profile = BuildFeeProfile(skills);
        var brokerRate = profile.BrokerFeeRate;
        var relistDiscount = profile.RelistDiscountRate;
        var oldValue = oldPrice * quantity;
        var newValue = newPrice * quantity;

        var fee = Math.Max(0, brokerRate * (newValue - oldValue))
                + (1.0 - relistDiscount) * brokerRate * newValue;
        return Math.Max(ModifyFeeMinimum, fee);
    }

    public FeeCalculationResult CalculateBuyCost(double pricePerUnit, int quantity, CharacterSkills? skills)
    {
        var profile = BuildFeeProfile(skills);
        var gross = pricePerUnit * quantity;
        var brokerFee = gross * profile.BrokerFeeRate;
        return new FeeCalculationResult
        {
            GrossAmount = gross,
            BrokerFee = brokerFee,
            SalesTax = 0,
            NetAmount = gross + brokerFee,
            EffectiveFeeRatePercent = profile.BrokerFeeRate * 100,
            Profile = profile
        };
    }

    public FeeCalculationResult CalculateSellProceeds(double pricePerUnit, int quantity, CharacterSkills? skills)
    {
        return CalculateSellProceedsWithProfile(pricePerUnit, quantity, BuildFeeProfile(skills));
    }

    public FeeCalculationResult CalculateSellProceedsWithProfile(double pricePerUnit, int quantity, FeeProfile profile)
    {
        var gross = pricePerUnit * quantity;
        var brokerFee = gross * profile.BrokerFeeRate;
        var salesTax = gross * profile.SalesTaxRate;
        return new FeeCalculationResult
        {
            GrossAmount = gross,
            BrokerFee = brokerFee,
            SalesTax = salesTax,
            NetAmount = gross - brokerFee - salesTax,
            EffectiveFeeRatePercent = (profile.BrokerFeeRate + profile.SalesTaxRate) * 100,
            Profile = profile
        };
    }

    public FeeCalculationResult CalculateOrderChangeCost(double pricePerUnit, int quantity, CharacterSkills? skills)
    {
        // Momentane Kosten einer Order-Änderung NUR auf Basis der Broker-Fee
        // (die Modify-Fee mit alter/neuer Preis ist CalculateOrderModifyFee)
        var profile = BuildFeeProfile(skills);
        var gross = pricePerUnit * quantity;
        var brokerFee = gross * profile.BrokerFeeRate;
        return new FeeCalculationResult
        {
            GrossAmount = gross,
            BrokerFee = brokerFee,
            SalesTax = 0,
            NetAmount = gross + brokerFee,
            EffectiveFeeRatePercent = profile.BrokerFeeRate * 100,
            Profile = profile
        };
    }

    public double CalculateBreakEvenSellPrice(double buyPricePerUnit, int quantity, CharacterSkills? skills)
    {
        var profile = BuildFeeProfile(skills);
        var brokerRate = profile.BrokerFeeRate;
        var taxRate = profile.SalesTaxRate;
        var buyCostFactor = 1.0 + brokerRate;
        var sellNetFactor = 1.0 - brokerRate - taxRate;
        if (sellNetFactor <= 0) return double.PositiveInfinity;
        return buyPricePerUnit * buyCostFactor / sellNetFactor;
    }

    /// <summary>
    /// Break-even-Verkaufspreis für BESTANDS-Items mit gespeicherter Cost Basis:
    /// KEIN Buy-Faktor, weil die gespeicherte Basis die Erwerbskosten bereits
    /// genau einmal enthält (sonst würde die Kauf-Nebenkosten doppelt belastet).
    /// </summary>
    public double CalculateBreakEvenSellPriceForStoredBasis(double costBasisPerUnit, CharacterSkills? skills)
    {
        return CalculateBreakEvenSellPriceForStoredBasisWithProfile(costBasisPerUnit, BuildFeeProfile(skills));
    }

    public double CalculateBreakEvenSellPriceForStoredBasisWithProfile(double costBasisPerUnit, FeeProfile profile)
    {
        var brokerRate = profile.BrokerFeeRate;
        var taxRate = profile.SalesTaxRate;
        var sellNetFactor = 1.0 - brokerRate - taxRate;
        if (sellNetFactor <= 0) return double.PositiveInfinity;
        return costBasisPerUnit / sellNetFactor;
    }

    public TradeProfitResult CalculateTradeProfit(double buyPrice, double sellPrice, int quantity, CharacterSkills? skills)
    {
        var buyCost = CalculateBuyCost(buyPrice, quantity, skills);
        var sellProceeds = CalculateSellProceeds(sellPrice, quantity, skills);
        var netProfit = sellProceeds.NetAmount - buyCost.NetAmount;
        return new TradeProfitResult
        {
            BuyCost = buyCost,
            SellProceeds = sellProceeds,
            NetProfit = netProfit,
            RoiPercent = buyCost.NetAmount > 0 ? (netProfit / buyCost.NetAmount) * 100 : 0,
            BreakEvenSellPrice = CalculateBreakEvenSellPrice(buyPrice, quantity, skills)
        };
    }

    private static int GetSkillLevel(CharacterSkills? skills, int skillId)
    {
        if (skills?.Skills == null) return 0;
        return skills.Skills.FirstOrDefault(s => s.SkillId == skillId)?.TrainedSkillLevel ?? 0;
    }
}