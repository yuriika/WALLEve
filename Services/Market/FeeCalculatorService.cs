using WALLEve.Models.Esi.Character;
using WALLEve.Services.Market.Interfaces;

namespace WALLEve.Services.Market;
/// <summary>
/// Gebührenberechnung nach offiziellen EVE-Formeln (support.eveonline.com,
/// Artikel "Broker Fee and Sales Tax" + "Buy and Sell Orders"):
///
/// BROKER FEE (Order-Erstellung, NPC-Station):
///   3% − 0.3%×Broker-Relations-Level − 0.03%×Faction-Standing − 0.02%×Corp-Standing,
///   Minimum 1%. Upwell-Strukturen: abweichend (0.5% NPC-Sink + Owner-Anteil,
///   Broker Relations greift nicht) — hier nicht modelliert, NPC-Station als Default.
///
/// SALES TAX (nach Verkauf):
///   7,5% Basis (seit 2025-03), −11% relativ pro Accounting-Level, Minimum 3,37%.
///
/// MODIFY-FEE (Preisänderung P1→P2, offiziell):
///   Fee = max(0, BR×(P2−P1)) + (1−RD)×BR×P2
///   BR = effektive Broker-Rate, RD = Relist-Discount (Advanced Broker Relations,
///   Wiki: 50% Basis + 6% je Level), Minimum 100 ISK.
///   → Preissenkung ist deutlich günstiger als Preiserhöhung (keine Differenz-Fee).
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
    private const double SalesTaxBase = 0.075;          // seit 2025-03-12 (vorher 4%)
    private const double SalesTaxReductionPerLevel = 0.11; // relativ pro Accounting-Level
    private const double SalesTaxMin = 0.0337;

    // Relist-Discount: 50% Basis + 6% je Advanced-Broker-Relations-Level
    private const double RelistDiscountBase = 0.50;
    private const double RelistDiscountPerLevel = 0.06;
    private const double ModifyFeeMinimum = 100.0;

    public double GetBrokerFeeRate(CharacterSkills? skills)
    {
        var level = GetSkillLevel(skills, BrokerRelationsSkillId);
        // Standings (Faction/Corp) liegen über ESI nicht zuverlässig vor → 0 angenommen;
        // das ergibt die konservativste (höchste) Schätzung.
        return Math.Max(BrokerFeeMin, BrokerFeeBase - (level * BrokerFeePerLevel));
    }

    public double GetSalesTaxRate(CharacterSkills? skills)
    {
        var level = GetSkillLevel(skills, AccountingSkillId);
        return Math.Max(SalesTaxMin, SalesTaxBase * (1.0 - (level * SalesTaxReductionPerLevel)));
    }

    /// <summary>Relist-Discount (RD) aus Advanced Broker Relations.</summary>
    public double GetRelistDiscountRate(CharacterSkills? skills)
    {
        var level = GetSkillLevel(skills, AdvancedBrokerRelationsSkillId);
        return Math.Min(1.0, RelistDiscountBase + (level * RelistDiscountPerLevel));
    }

    /// <summary>
    /// Fee für eine Preisänderung P1→P2 (Preise pro Einheit, Menge multipliziert):
    /// Fee = max(0, BR×(Wert2−Wert1)) + (1−RD)×BR×Wert2, Minimum 100 ISK.
    /// </summary>
    public double CalculateOrderModifyFee(double oldPrice, double newPrice, int quantity, CharacterSkills? skills)
    {
        var brokerRate = GetBrokerFeeRate(skills);
        var relistDiscount = GetRelistDiscountRate(skills);
        var oldValue = oldPrice * quantity;
        var newValue = newPrice * quantity;

        var fee = Math.Max(0, brokerRate * (newValue - oldValue))
                + (1.0 - relistDiscount) * brokerRate * newValue;
        return Math.Max(ModifyFeeMinimum, fee);
    }

    public FeeCalculationResult CalculateBuyCost(double pricePerUnit, int quantity, CharacterSkills? skills)
    {
        var gross = pricePerUnit * quantity;
        var brokerFee = gross * GetBrokerFeeRate(skills);
        return new FeeCalculationResult
        {
            GrossAmount = gross,
            BrokerFee = brokerFee,
            SalesTax = 0,
            NetAmount = gross + brokerFee,
            EffectiveFeeRatePercent = GetBrokerFeeRate(skills) * 100
        };
    }

    public FeeCalculationResult CalculateSellProceeds(double pricePerUnit, int quantity, CharacterSkills? skills)
    {
        var gross = pricePerUnit * quantity;
        var brokerRate = GetBrokerFeeRate(skills);
        var taxRate = GetSalesTaxRate(skills);
        var brokerFee = gross * brokerRate;
        var salesTax = gross * taxRate;
        return new FeeCalculationResult
        {
            GrossAmount = gross,
            BrokerFee = brokerFee,
            SalesTax = salesTax,
            NetAmount = gross - brokerFee - salesTax,
            EffectiveFeeRatePercent = (brokerRate + taxRate) * 100
        };
    }

    public FeeCalculationResult CalculateOrderChangeCost(double pricePerUnit, int quantity, CharacterSkills? skills)
    {
        // Momentane Kosten einer Order-Änderung NUR auf Basis der Broker-Fee
        // (die Modify-Fee mit alter/neuer Preis ist CalculateOrderModifyFee)
        var gross = pricePerUnit * quantity;
        var brokerFee = gross * GetBrokerFeeRate(skills);
        return new FeeCalculationResult
        {
            GrossAmount = gross,
            BrokerFee = brokerFee,
            SalesTax = 0,
            NetAmount = gross + brokerFee,
            EffectiveFeeRatePercent = GetBrokerFeeRate(skills) * 100
        };
    }

    public double CalculateBreakEvenSellPrice(double buyPricePerUnit, int quantity, CharacterSkills? skills)
    {
        var brokerRate = GetBrokerFeeRate(skills);
        var taxRate = GetSalesTaxRate(skills);
        var buyCostFactor = 1.0 + brokerRate;
        var sellNetFactor = 1.0 - brokerRate - taxRate;
        if (sellNetFactor <= 0) return double.PositiveInfinity;
        return buyPricePerUnit * buyCostFactor / sellNetFactor;
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