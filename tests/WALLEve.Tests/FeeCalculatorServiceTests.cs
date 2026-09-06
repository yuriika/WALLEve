using WALLEve.Models.Esi.Character;
using WALLEve.Services.Market;

namespace WALLEve.Tests;

/// <summary>
/// Tests für die Gebühren- und Gewinn-Berechnung (Broker Fee, Sales Tax).
/// Kernlogik: Basis 1% Broker / 8% Tax, je Skill-Level -0.05% / -0.4%,
/// Mindestwerte 0.75% bzw. 6% (entspricht Level 5 + 4/Accounting).
/// </summary>
public class FeeCalculatorServiceTests
{
    private static CharacterSkills SkillsWith(int brokerRelationsLevel, int accountingLevel) => new()
    {
        Skills = new List<CharacterSkill>
        {
            new() { SkillId = 3444, TrainedSkillLevel = brokerRelationsLevel },
            new() { SkillId = 16622, TrainedSkillLevel = accountingLevel }
        }
    };

    [Fact]
    public void BrokerFee_WithoutSkills_IsOnePercent()
    {
        var service = new FeeCalculatorService();
        Assert.Equal(0.01, service.GetBrokerFeeRate(null), 6);
    }

    [Fact]
    public void BrokerFee_Level5_FloorsAt075Percent()
    {
        var service = new FeeCalculatorService();
        // 1% - 5*0.05% = 0.75% → exakt am Floor
        Assert.Equal(0.0075, service.GetBrokerFeeRate(SkillsWith(5, 0)), 6);
    }

    [Fact]
    public void SalesTax_WithoutSkills_IsEightPercent()
    {
        var service = new FeeCalculatorService();
        Assert.Equal(0.08, service.GetSalesTaxRate(null), 6);
    }

    [Fact]
    public void SalesTax_Level5_FloorsAtSixPercent()
    {
        var service = new FeeCalculatorService();
        // 8% - 5*0.4% = 6% → exakt am Floor
        Assert.Equal(0.06, service.GetSalesTaxRate(SkillsWith(0, 5)), 6);
    }

    [Fact]
    public void SellProceeds_SubtractsFeesFromGross()
    {
        var service = new FeeCalculatorService();
        var result = service.CalculateSellProceeds(1000, 10, null); // 10.000 ISK gross

        Assert.Equal(10_000, result.GrossAmount, 6);
        Assert.Equal(100, result.BrokerFee, 6);      // 1%
        Assert.Equal(800, result.SalesTax, 6);       // 8%
        Assert.Equal(9_100, result.NetAmount, 6);    // netto nach Fees
        Assert.Equal(9.0, result.EffectiveFeeRatePercent, 6);
    }

    [Fact]
    public void TradeProfit_WithSkills_IsPositive()
    {
        var service = new FeeCalculatorService();
        var skills = SkillsWith(5, 5); // minimale Fees

        var profit = service.CalculateTradeProfit(90, 100, 1000, skills);

        // Kauf: 90.000 + 0.75% = 90.675; Verkauf: 100.000 - (0.75%+6%) = 93.250
        Assert.Equal(2_575, profit.NetProfit, 1);
        Assert.True(profit.RoiPercent > 2.8 && profit.RoiPercent < 2.9);
        // Break-even: 90 * 1.0075 / 0.9325 ≈ 97.24
        Assert.Equal(97.24, profit.BreakEvenSellPrice, 2);
    }

    [Fact]
    public void BreakEvenSellPrice_ScalesWithFees()
    {
        var service = new FeeCalculatorService();

        var noSkills = service.CalculateBreakEvenSellPrice(100, 1, null); // 100*1.01/0.91
        var maxSkills = service.CalculateBreakEvenSellPrice(100, 1, SkillsWith(5, 5)); // 100*1.0075/0.9325

        Assert.Equal(110.99, noSkills, 2);
        Assert.Equal(108.04, maxSkills, 2);
        Assert.True(maxSkills < noSkills); // bessere Skills → niedrigerer Break-even
    }
}