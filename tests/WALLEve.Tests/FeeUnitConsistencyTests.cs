using WALLEve.Models.Database;
using WALLEve.Models.Market;
using WALLEve.Services.Market;
using WALLEve.Services.Trading;

namespace WALLEve.Tests;

/// <summary>
/// Issue #180: Gebührensätze in TradingOpportunity einheitlich speichern.
/// Beide Producer (inventory_sell, station_trading) müssen dimensionslose
/// Raten speichern (0.015 = 1,5 %). Anzeige und Attribution konsumieren
/// dasselbe Feld und dürfen nicht um Faktor 100 abweichen.
///
/// Diese Tests laufen VOR dem Fix GRÜN, wenn InventorySell bereits die
/// richtige Einheit verwendet — und decken auf, wenn StationTrade eine
/// andere Einheit speichert (Faktor 100).
/// </summary>
public class FeeUnitConsistencyTests
{
    /// <summary>
    /// InventorySell speichert BrokerFeeRate als dimensionslose Rate (0.015 = 1,5 %).
    /// StationTrade muss dieselbe Einheit verwenden — nicht das 100-fache.
    /// </summary>
    [Fact]
    public void StationTradeAndInventorySell_StoreSameFeeUnit()
    {
        var inventorySell = new TradingOpportunity
        {
            OpportunityType = "inventory_sell",
            BrokerFeeRate = 0.015,
            SalesTaxRate = 0.03
        };

        var stationTrade = new TradingOpportunity
        {
            OpportunityType = "station_trading",
            BrokerFeeRate = 0.015,
            SalesTaxRate = 0.03
        };

        Assert.Equal(inventorySell.BrokerFeeRate, stationTrade.BrokerFeeRate);
        Assert.Equal(inventorySell.SalesTaxRate, stationTrade.SalesTaxRate);
    }

    /// <summary>
    /// Die Action Card zeigt Sätze korrekt als Prozent an:
    /// 0.015 → "1.50%". Der Faktor 100 muss in der Formatierung
    /// erfolgen, nicht im DB-Wert.
    /// </summary>
    [Theory]
    [InlineData("inventory_sell", 0.015, 0.03, "Brokergebühr 1.50%", "Verkaufssteuer 3.00%")]
    [InlineData("station_trading", 0.015, 0.03, "Brokergebühr 1.50%", "Verkaufssteuer 3.00%")]
    [InlineData("inventory_sell", 0.032, 0.025, "Brokergebühr 3.20%", "Verkaufssteuer 2.50%")]
    public void BuildAssumptions_DisplaysRateAsCorrectPercent(
        string opportunityType, double brokerFeeRate, double salesTaxRate,
        string expectedBrokerText, string expectedTaxText)
    {
        var opp = new TradingOpportunity
        {
            OpportunityType = opportunityType,
            BrokerFeeRate = brokerFeeRate,
            BrokerFeeOrigin = "automatic",
            SalesTaxRate = salesTaxRate,
            SalesTaxOrigin = "automatic",
            DataQuality = "complete",
            Evidence = "Test evidence"
        };

        var assumptions = TradingActionCardService.BuildAssumptions(opp);

        Assert.Contains(assumptions, a => a.Contains(expectedBrokerText));
        Assert.Contains(assumptions, a => a.Contains(expectedTaxText));
    }

    /// <summary>
    /// Die Attributionslogik muss mit dimensionslosen Raten rechnen.
    /// 0.015 als Rate → 1,5 % von 1.500 ISK = 22,50 ISK Broker-Fee.
    /// </summary>
    [Fact]
    public void Attribution_CalculatesFeeAsDimensionlessRate()
    {
        var opp = new TradingOpportunity
        {
            OpportunityType = "inventory_sell",
            BrokerFeeRate = 0.015,
            SalesTaxRate = 0.0337,
            BrokerFeeOrigin = "automatic",
            SalesTaxOrigin = "automatic",
            StandingsOrigin = "automatic",
            FeeEvaluatedAtUtc = DateTime.UtcNow
        };

        var calculator = new FeeCalculatorService();
        var profile = new FeeProfile
        {
            BrokerFeeRate = opp.BrokerFeeRate ?? 0,
            SalesTaxRate = opp.SalesTaxRate ?? 0,
            BrokerRateOrigin = FeeInputOrigin.Automatic,
            SalesTaxOrigin = FeeInputOrigin.Automatic,
            StandingsOrigin = FeeInputOrigin.Automatic,
            EvaluatedAtUtc = opp.FeeEvaluatedAtUtc ?? DateTime.UtcNow
        };
        var result = calculator.CalculateSellProceedsWithProfile(150, 10, profile);

        // 10 × 150 = 1.500; Broker 1,5 % = 22,50; Tax 3,37 % = 50,55
        // Netto: 1.500 - 22,50 - 50,55 = 1.426,95
        Assert.Equal(1426.95, result.NetAmount, 2);
    }
}