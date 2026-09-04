using WALLEve.Models.Esi.Character;

namespace WALLEve.Services.Market.Interfaces;

/// <summary>
/// Berechnet EVE-Markt-Gebühren (Broker Fee, Sales Tax) basierend auf Charakter-Skills.
/// </summary>
public interface IFeeCalculatorService
{
    double GetBrokerFeeRate(CharacterSkills? skills);
    double GetSalesTaxRate(CharacterSkills? skills);
    FeeCalculationResult CalculateBuyCost(double pricePerUnit, int quantity, CharacterSkills? skills);
    FeeCalculationResult CalculateSellProceeds(double pricePerUnit, int quantity, CharacterSkills? skills);
    FeeCalculationResult CalculateOrderChangeCost(double pricePerUnit, int quantity, CharacterSkills? skills);
    double CalculateBreakEvenSellPrice(double buyPricePerUnit, int quantity, CharacterSkills? skills);
    TradeProfitResult CalculateTradeProfit(double buyPrice, double sellPrice, int quantity, CharacterSkills? skills);
}

public class FeeCalculationResult
{
    public double GrossAmount { get; set; }
    public double BrokerFee { get; set; }
    public double SalesTax { get; set; }
    public double NetAmount { get; set; }
    public double EffectiveFeeRatePercent { get; set; }
}

public class TradeProfitResult
{
    public FeeCalculationResult BuyCost { get; set; } = new();
    public FeeCalculationResult SellProceeds { get; set; } = new();
    public double NetProfit { get; set; }
    public double RoiPercent { get; set; }
    public double BreakEvenSellPrice { get; set; }
}