using WALLEve.Models.Esi.Character;
using WALLEve.Services.Market.Interfaces;

namespace WALLEve.Services.Market;
/// <summary>
/// Broker Fee: Basis 1% - 0.05% pro Broker Relations Level (Skill 3444, max 0.75%)
/// Sales Tax: Basis 8% - 0.4% pro Accounting Level (Skill 16622, max 6%)
/// </summary>
public class FeeCalculatorService : IFeeCalculatorService
{
    private const int BrokerRelationsSkillId = 3444;
    private const int AccountingSkillId = 16622;
    private const double BrokerFeeBase = 0.01;
    private const double BrokerFeePerLevel = 0.0005;
    private const double SalesTaxBase = 0.08;
    private const double SalesTaxPerLevel = 0.004;

    public double GetBrokerFeeRate(CharacterSkills? skills)
    {
        var level = GetSkillLevel(skills, BrokerRelationsSkillId);
        return Math.Max(0.0075, BrokerFeeBase - (level * BrokerFeePerLevel));
    }

    public double GetSalesTaxRate(CharacterSkills? skills)
    {
        var level = GetSkillLevel(skills, AccountingSkillId);
        return Math.Max(0.06, SalesTaxBase - (level * SalesTaxPerLevel));
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