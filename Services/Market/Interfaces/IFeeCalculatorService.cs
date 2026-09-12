using WALLEve.Models.Esi.Character;

namespace WALLEve.Services.Market.Interfaces;

public interface IFeeCalculatorService
{
    double GetBrokerFeeRate(CharacterSkills? skills);
    double GetSalesTaxRate(CharacterSkills? skills);

    /// <summary>Relist-Discount (RD) aus Advanced Broker Relations — für Modify-Fee.</summary>
    double GetRelistDiscountRate(CharacterSkills? skills);

    /// <summary>
    /// Fee für eine Preisänderung: max(0, BR×(Wert2−Wert1)) + (1−RD)×BR×Wert2, min 100 ISK.
    /// </summary>
    double CalculateOrderModifyFee(double oldPrice, double newPrice, int quantity, CharacterSkills? skills);

    FeeCalculationResult CalculateBuyCost(double pricePerUnit, int quantity, CharacterSkills? skills);
    FeeCalculationResult CalculateSellProceeds(double pricePerUnit, int quantity, CharacterSkills? skills);
    FeeCalculationResult CalculateOrderChangeCost(double pricePerUnit, int quantity, CharacterSkills? skills);
    double CalculateBreakEvenSellPrice(double buyPricePerUnit, int quantity, CharacterSkills? skills);

    /// <summary>
    /// Break-even-Verkaufspreis für BESTANDS-Items mit GESPEICHERTER Cost Basis.
    /// Die gespeicherte Basis enthält die verknüpften Erwerbskosten bereits genau
    /// einmal — eine erneute Buy-Brokergebühr darf deshalb NICHT aufgeschlagen werden:
    /// Ergebnis = Basis / (1 − BrokerRate − SalesTax).
    /// Für echte Kauf→Verkauf-Trades weiterhin CalculateBreakEvenSellPrice (mit Buy-Faktor).
    /// </summary>
    double CalculateBreakEvenSellPriceForStoredBasis(double costBasisPerUnit, CharacterSkills? skills);
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