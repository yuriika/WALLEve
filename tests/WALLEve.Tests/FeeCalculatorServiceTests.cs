using Microsoft.Extensions.Options;
using WALLEve.Models.Esi.Character;
using WALLEve.Models.Market;
using WALLEve.Services.Market;

namespace WALLEve.Tests;

/// <summary>
/// Tests für die Gebühren- und Gewinn-Berechnung nach OFFIZIELLEN EVE-Formeln
/// (support.eveonline.com "Broker Fee and Sales Tax" + "Buy and Sell Orders"):
/// Broker 3% Basis (−0.3%/Level, min 1%), Sales Tax 7,5% Basis (min 3,37%),
/// Modify-Fee = max(0, BR×(P2−P1)) + (1−RD)×BR×P2 (min 100 ISK).
/// Skill-IDs SDE-verifiziert: Broker Relations 3446, Advanced Broker Relations 16597, Accounting 16622.
/// </summary>
public class FeeCalculatorServiceTests
{
    private static CharacterSkills SkillsWith(int brokerRelationsLevel, int accountingLevel, int advancedBrokerLevel = 0) => new()
    {
        Skills = new List<CharacterSkill>
        {
            new() { SkillId = 3446, TrainedSkillLevel = brokerRelationsLevel },
            new() { SkillId = 16597, TrainedSkillLevel = advancedBrokerLevel },
            new() { SkillId = 16622, TrainedSkillLevel = accountingLevel }
        }
    };

    // ------------------------------------------------------------------
    // Broker Fee (3% Basis, −0.3%/Level, min 1%)
    // ------------------------------------------------------------------

    [Fact]
    public void BrokerFee_WithoutSkills_IsThreePercent()
    {
        var service = new FeeCalculatorService();
        Assert.Equal(0.03, service.GetBrokerFeeRate(null), 6);
    }

    [Fact]
    public void BrokerFee_Level5_IsOnePointFivePercent()
    {
        var service = new FeeCalculatorService();
        // 3% − 5×0.3% = 1.5% (Floor 1% wird bei Level 5 ohne Standings nicht erreicht)
        Assert.Equal(0.015, service.GetBrokerFeeRate(SkillsWith(5, 0)), 6);
    }

    [Fact]
    public void BrokerFee_DoesNotUseRetailSkill_WrongId3444()
    {
        var service = new FeeCalculatorService();
        // Sicherstellen: Skill 3444 ("Retail") beeinflusst die Broker-Fee NICHT —
        // das war ein früherer Bug (falsche Skill-ID im Calculator).
        var skills = new CharacterSkills
        {
            Skills = new List<CharacterSkill>
            {
                new() { SkillId = 3444, TrainedSkillLevel = 5 } // Retail, irrelevant
            }
        };
        Assert.Equal(0.03, service.GetBrokerFeeRate(skills), 6);
    }

    // ------------------------------------------------------------------
    // Sales Tax (7,5% Basis, −11% relativ/Level, min 3,37%)
    // ------------------------------------------------------------------

    [Fact]
    public void SalesTax_WithoutSkills_IsSevenPointFivePercent()
    {
        var service = new FeeCalculatorService();
        Assert.Equal(0.075, service.GetSalesTaxRate(null), 6);
    }

    [Fact]
    public void SalesTax_Level5_FloorsAtThreePoint37Percent()
    {
        var service = new FeeCalculatorService();
        // 7.5% × (1 − 5×0.11) = 3.375% → Floor 3.37%
        Assert.Equal(0.0337, service.GetSalesTaxRate(SkillsWith(0, 5)), 4);
    }

    [Fact]
    public void SellProceeds_SubtractsFeesFromGross()
    {
        var service = new FeeCalculatorService();
        var result = service.CalculateSellProceeds(1000, 10, null); // 10.000 ISK gross

        Assert.Equal(10_000, result.GrossAmount, 6);
        Assert.Equal(300, result.BrokerFee, 6);   // 3%
        Assert.Equal(750, result.SalesTax, 6);    // 7,5%
        Assert.Equal(8_950, result.NetAmount, 6); // netto nach Fees
        Assert.Equal(10.5, result.EffectiveFeeRatePercent, 6);
    }

    // ------------------------------------------------------------------
    // Modify-Fee (Preisänderung) — offizielle Formel
    // ------------------------------------------------------------------

    [Fact]
    public void ModifyFee_PriceCut_PaysOnlyRelistShare()
    {
        var service = new FeeCalculatorService();
        // 6→5 ISK × 10.000: max(0, BR×(50k−60k))=0; (1−0.5)×0.03×50k = 750
        var fee = service.CalculateOrderModifyFee(6.0, 5.0, 10_000, null);

        Assert.Equal(750.0, fee, 2);
    }

    [Fact]
    public void ModifyFee_PriceIncrease_PaysRelistPlusDifference()
    {
        var service = new FeeCalculatorService();
        // 6→7 ISK × 10.000: max(0, 0.03×10k)=300; 0.5×0.03×70k=1050 → 1350
        var fee = service.CalculateOrderModifyFee(6.0, 7.0, 10_000, null);

        Assert.Equal(1_350.0, fee, 2);
    }

    [Fact]
    public void ModifyFee_AdvancedBrokerRelations_ReducesRelistShare()
    {
        var service = new FeeCalculatorService();
        var skills = SkillsWith(0, 0, advancedBrokerLevel: 5); // RD = 50% + 5×6% = 80%

        // Preissenkung 6→5 × 10.000: (1−0.8)×0.03×50k = 300
        var cut = service.CalculateOrderModifyFee(6.0, 5.0, 10_000, skills);
        // Preiserhöhung 6→7 × 10.000: 300 (Differenz) + (1−0.8)×0.03×70k = 420 → 720
        var raise = service.CalculateOrderModifyFee(6.0, 7.0, 10_000, skills);

        Assert.Equal(300.0, cut, 2);
        Assert.Equal(720.0, raise, 2);
    }

    [Fact]
    public void ModifyFee_NeverBelow100Isk()
    {
        var service = new FeeCalculatorService();
        // Kleine Order: 6→5 × 10 Einheiten → 0 + 0.5×0.03×50 = 0.75 → Minimum 100 ISK
        var fee = service.CalculateOrderModifyFee(6.0, 5.0, 10, null);

        Assert.Equal(100.0, fee, 2);
    }

    [Fact]
    public void RelistDiscount_ScalesWithAdvancedBrokerRelations()
    {
        var service = new FeeCalculatorService();
        Assert.Equal(0.50, service.GetRelistDiscountRate(null), 6);
        Assert.Equal(0.80, service.GetRelistDiscountRate(SkillsWith(5, 5, advancedBrokerLevel: 5)), 6);
    }

    // ------------------------------------------------------------------
    // Trade Profit & Break-even
    // ------------------------------------------------------------------

    [Fact]
    public void TradeProfit_WithSkills_IsPositive()
    {
        var service = new FeeCalculatorService();
        var skills = SkillsWith(5, 5); // Broker 1.5%, Tax 3.375%

        var profit = service.CalculateTradeProfit(90, 100, 1000, skills);

        // Kauf: 90.000 × 1.015 = 91.350; Verkauf: 100.000 × (1−0.015−0.03375) = 95.125
        Assert.Equal(3_775, profit.NetProfit, 1);
        Assert.True(profit.RoiPercent > 4.1 && profit.RoiPercent < 4.2);
        // Break-even: 90 × 1.015 / 0.95125 ≈ 96.03
        Assert.Equal(96.03, profit.BreakEvenSellPrice, 2);
    }

    [Fact]
    public void BreakEvenSellPrice_ScalesWithFees()
    {
        var service = new FeeCalculatorService();

        var noSkills = service.CalculateBreakEvenSellPrice(100, 1, null); // 100×1.03/0.895
        var maxSkills = service.CalculateBreakEvenSellPrice(100, 1, SkillsWith(5, 5)); // 100×1.015/0.95125

        Assert.Equal(115.08, noSkills, 2);
        Assert.Equal(106.70, maxSkills, 2);
        Assert.True(maxSkills < noSkills); // bessere Skills → niedrigerer Break-even
    }

    // ------------------------------------------------------------------
    // Invariante (#4): Break-even der GESPEICHERTEN Basis ohne Buy-Faktor
    // ------------------------------------------------------------------

    [Fact]
    public void BreakEvenForStoredBasis_DoesNotAddBuyFee()
    {
        var service = new FeeCalculatorService();

        // Gespeicherte Basis: Erwerbskosten sind darin bereits genau einmal enthalten —
        // kein Buy-Faktor (×1.03 bzw. ×1.015) wie beim echten Kauf→Verkauf-Trade.
        var noSkills = service.CalculateBreakEvenSellPriceForStoredBasis(100, null); // 100/0.895
        var maxSkills = service.CalculateBreakEvenSellPriceForStoredBasis(100, SkillsWith(5, 5)); // 100/0.95125

        Assert.Equal(111.73, noSkills, 2);
        Assert.Equal(105.12, maxSkills, 2);
        Assert.True(noSkills < service.CalculateBreakEvenSellPrice(100, 1, null));              // 111.73 < 115.08
        Assert.True(maxSkills < service.CalculateBreakEvenSellPrice(100, 1, SkillsWith(5, 5))); // 105.12 < 106.70
    }

    [Fact]
    public void BreakEvenForStoredBasis_Rounding_IsPrecisePerUnit()
    {
        var service = new FeeCalculatorService();

        // Basis 90, keine Skills: 90 / 0.895 = 100,558659… (Rundung auf 4 Nachkommastellen)
        var value = service.CalculateBreakEvenSellPriceForStoredBasis(90, null);
        Assert.Equal(100.5587, value, 4);
        // Abgrenzung: klassischer Trade schlägt den Buy-Faktor auf → 103,5754
        Assert.Equal(103.5754, service.CalculateBreakEvenSellPrice(90, 1, null), 4);
    }

    // ------------------------------------------------------------------
    // Herkunftstreue (Issue #46): Automatic / ManualOverride / Estimated /
    // Unknown sind unterscheidbar — kein stiller Nullgebühr-Fallback.
    // ------------------------------------------------------------------

    [Fact]
    public void Profile_WithoutSkills_MarksFeeInputsAsEstimated()
    {
        // Keine übergebenen Skills und keine Overrides: die konservativen Basissätze
        // (3 % Broker, 7,5 % Steuer, 0 % Standings) sind SCHÄTZUNGEN, kein stiller
        // Null-Fallback und keine belegten Werte.
        var profile = new FeeCalculatorService().BuildFeeProfile(null);

        Assert.Equal(0.03, profile.BrokerFeeRate, 6);
        Assert.Equal(0.075, profile.SalesTaxRate, 6);
        Assert.Equal(FeeInputOrigin.Estimated, profile.BrokerRateOrigin);
        Assert.Equal(FeeInputOrigin.Estimated, profile.SalesTaxOrigin);
        Assert.Equal(FeeInputOrigin.Estimated, profile.StandingsOrigin);
        Assert.False(profile.IsPrecise);
        Assert.False(profile.HasUnknownInput); // Estimated ist eine begrenzte Spanne, kein Blocker
        Assert.True(profile.ProvidesBoundedRange);
        // Review #129: die Spanne deckt ALLE Estimated-Eingaben ab — fehlende
        // Skills (Level-Spanne 0..5) UND Standings (max. 0,5 % Rabatt).
        Assert.Equal(0.01, profile.BrokerFeeRateBestCase, 6);     // 3 % − 5×0,3 % − 0,5 % = 1 % (Floor)
        Assert.Equal(0.03375, profile.SalesTaxRateBestCase, 6);   // 7,5 % × (1 − 5×11 %) = 3,375 %
    }

    [Fact]
    public void Profile_EstimatedStandings_ExposesBoundedBrokerRange()
    {
        // Normalfall (Review #129 / AC3): Standings sind über ESI nicht belegbar —
        // der Standing-Anteil ist eine begrenzte Spanne [Satz − 0,5 %, Satz],
        // kein falsch exakter Einzelwert.
        var noSkills = new FeeCalculatorService().BuildFeeProfile(null);
        Assert.Equal(0.03, noSkills.BrokerFeeRate, 6);
        // Ohne Skill-Antwort ist auch der Broker-Relations-/Accounting-Level
        // 0..5 unklar → die Spanne enthält die komplette Level-Spanne der Formel.
        Assert.Equal(0.01, noSkills.BrokerFeeRateBestCase, 6);   // 3 % − 5×0,3 % − 0,5 % = 1 % (Floor)
        Assert.Equal(0.03375, noSkills.SalesTaxRateBestCase, 6); // 7,5 % × 0,45 = 3,375 %

        var maxSkills = new FeeCalculatorService().BuildFeeProfile(SkillsWith(5, 5));
        Assert.Equal(0.015, maxSkills.BrokerFeeRate, 6);
        Assert.Equal(0.01, maxSkills.BrokerFeeRateBestCase, 6); // Floor: Broker-Fee-Minimum 1 %

        // Best-Case-Kopie trägt die optimistische Grenze BEIDER Sätze
        // (Origins unverändert).
        var best = maxSkills.BestCaseCopy();
        Assert.Equal(0.01, best.BrokerFeeRate, 6);
        Assert.Equal(0.03375, best.SalesTaxRate, 6);
        Assert.Equal(FeeInputOrigin.Automatic, best.BrokerRateOrigin);
        Assert.Equal(FeeInputOrigin.Estimated, best.StandingsOrigin);
    }

    [Fact]
    public void Profile_MissingSkills_RangeCoversAllAllowedSkillLevelsAndStandings()
    {
        // Regression Review #129: Die Spanne bei fehlender Skill-Antwort ist nur
        // eine echte Grenze, wenn sie ALLE zulässigen Skill-Level (0..5) und
        // Standing-Rabatte (0..0,5 %) der offiziellen Formel enthält.
        var service = new FeeCalculatorService();
        var profile = service.BuildFeeProfile(null);
        Assert.True(profile.ProvidesBoundedRange);

        // Jede zulässige Skill-Kombination liegt innerhalb der ausgewiesenen Spanne.
        for (var brokerLevel = 0; brokerLevel <= 5; brokerLevel++)
        {
            for (var accountingLevel = 0; accountingLevel <= 5; accountingLevel++)
            {
                var outcome = service.BuildFeeProfile(SkillsWith(brokerLevel, accountingLevel));
                Assert.InRange(outcome.BrokerFeeRate, profile.BrokerFeeRateBestCase, profile.BrokerFeeRate);
                Assert.InRange(outcome.SalesTaxRate, profile.SalesTaxRateBestCase, profile.SalesTaxRate);
            }
        }

        // Auch der maximal mögliche Standing-Rabatt (0..0,5 %) reißt die untere
        // Grenze nicht: max(1 %, 3 % − 0,3 %×Level − Rabatt) ∈ [1 %, 3 %].
        for (var tenths = 0; tenths <= 5; tenths++)
        {
            var discount = tenths * 0.001;
            for (var brokerLevel = 0; brokerLevel <= 5; brokerLevel++)
            {
                var broker = Math.Max(0.01, 0.03 - (brokerLevel * 0.003) - discount);
                Assert.InRange(broker, profile.BrokerFeeRateBestCase, profile.BrokerFeeRate);
            }
        }
    }

    [Fact]
    public void Profile_PreciseInputs_DoNotExposeARange()
    {
        // Manuelle Overrides sind belegt → keine Spanne: alle Eingaben exakt.
        var service = new FeeCalculatorService(Options.Create(new FeeOverrideSettings
        {
            BrokerFeeRate = 0.02
        }));

        var profile = service.BuildFeeProfile(SkillsWith(5, 5));

        Assert.True(profile.IsPrecise);
        Assert.False(profile.ProvidesBoundedRange);
        Assert.Equal(profile.BrokerFeeRate, profile.BrokerFeeRateBestCase, 6);
    }

    [Fact]
    public void Profile_WithSkills_MarksFeeInputsAsAutomatic()
    {
        var profile = new FeeCalculatorService().BuildFeeProfile(SkillsWith(5, 5));

        Assert.Equal(0.015, profile.BrokerFeeRate, 6);
        Assert.Equal(FeeInputOrigin.Automatic, profile.BrokerRateOrigin);
        Assert.Equal(FeeInputOrigin.Automatic, profile.SalesTaxOrigin);
        // Standings sind über ESI nicht belegbar → bleiben eine Schätzung, auch mit Skills.
        Assert.Equal(FeeInputOrigin.Estimated, profile.StandingsOrigin);
        // Review #129: geschätzte Standings sind NICHT präzise — die Berechnung
        // ist über die dokumentierte Spanne abbildbar, aber kein exakter Einzelwert.
        Assert.False(profile.IsPrecise);
        Assert.True(profile.ProvidesBoundedRange);
        // Spanne aus der offiziellen Formel: Skill-Satz 1,5 % − max. 0,5 % Standing-Rabatt → 1,0 % (Minimum).
        Assert.Equal(0.015, profile.BrokerFeeRate, 6);
        Assert.Equal(0.01, profile.BrokerFeeRateBestCase, 6);
    }

    [Fact]
    public void Profile_BrokerOverride_IsManualAndReplacesSkillCalculation()
    {
        var service = new FeeCalculatorService(Options.Create(new FeeOverrideSettings
        {
            BrokerFeeRate = 0.02
        }));

        var profile = service.BuildFeeProfile(SkillsWith(5, 5));

        Assert.Equal(0.02, profile.BrokerFeeRate, 6);
        Assert.Equal(FeeInputOrigin.ManualOverride, profile.BrokerRateOrigin);
        Assert.Equal(FeeInputOrigin.ManualOverride, profile.StandingsOrigin);
        Assert.Equal(FeeInputOrigin.Automatic, profile.SalesTaxOrigin);
        Assert.True(profile.IsPrecise);
    }

    [Fact]
    public void Profile_SalesTaxOverride_IsManual()
    {
        var service = new FeeCalculatorService(Options.Create(new FeeOverrideSettings
        {
            SalesTaxRate = 0.05
        }));

        var profile = service.BuildFeeProfile(SkillsWith(5, 5));

        Assert.Equal(0.05, profile.SalesTaxRate, 6);
        Assert.Equal(FeeInputOrigin.ManualOverride, profile.SalesTaxOrigin);
        Assert.Equal(FeeInputOrigin.Automatic, profile.BrokerRateOrigin);
    }

    [Fact]
    public void Profile_StandingsDiscountOverride_IsManualAndReducesBrokerRate()
    {
        // 0.0003 je Standing-Punkt: simuliert z. B. 3,5 Faction-Standing → 0.00105 Rabatt
        var service = new FeeCalculatorService(Options.Create(new FeeOverrideSettings
        {
            StandingsDiscount = 0.00105
        }));

        var profile = service.BuildFeeProfile(SkillsWith(5, 0));

        // 3 % − 5×0,3 % − 0,105 % = 1,395 %
        Assert.Equal(0.01395, profile.BrokerFeeRate, 6);
        Assert.Equal(FeeInputOrigin.ManualOverride, profile.StandingsOrigin);
        Assert.Equal(FeeInputOrigin.Automatic, profile.BrokerRateOrigin);
    }

    [Fact]
    public void Profile_InvalidOverride_MarksUnknown_AndBlocksPreciseCalculation()
    {
        // Ungültiger Satz (negativ): keine stille Annahme, sondern Unknown —
        // die präzise Berechnung ist blockiert (Akzeptanzkriterium 3).
        var service = new FeeCalculatorService(Options.Create(new FeeOverrideSettings
        {
            BrokerFeeRate = -0.5
        }));

        var profile = service.BuildFeeProfile(null);

        Assert.Equal(FeeInputOrigin.Unknown, profile.BrokerRateOrigin);
        Assert.True(profile.HasUnknownInput);
        Assert.False(profile.IsPrecise);
    }

    // ------------------------------------------------------------------
    // Gebührenfixtures (Akzeptanzkriterium 2): Skills UND Overrides prüfen
    // sowie Erwerbsgebühren genau einmal.
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(0, 0, null, null, 0.03, 0.075)]
    [InlineData(5, 5, null, null, 0.015, 0.03375)]
    [InlineData(0, 0, 0.01, 0.04, 0.01, 0.04)]
    [InlineData(5, 5, 0.02, 0.05, 0.02, 0.05)]
    public void FeeFixtures_CombineSkillsAndOverrides(
        int brokerLevel, int taxLevel,
        double? brokerOverride, double? taxOverride,
        double expectedBrokerRate, double expectedTaxRate)
    {
        var service = new FeeCalculatorService(Options.Create(new FeeOverrideSettings
        {
            BrokerFeeRate = brokerOverride,
            SalesTaxRate = taxOverride
        }));

        var profile = service.BuildFeeProfile(SkillsWith(brokerLevel, taxLevel));

        Assert.Equal(expectedBrokerRate, profile.BrokerFeeRate, 6);
        Assert.Equal(expectedTaxRate, profile.SalesTaxRate, 6);

        // Origin nach Konfiguration: Override schlägt Skills; ohne Override
        // sind die Skill-Werte automatisch ermittelt.
        Assert.Equal(brokerOverride.HasValue
            ? FeeInputOrigin.ManualOverride
            : FeeInputOrigin.Automatic, profile.BrokerRateOrigin);
        Assert.Equal(taxOverride.HasValue
            ? FeeInputOrigin.ManualOverride
            : FeeInputOrigin.Automatic, profile.SalesTaxOrigin);
    }

    [Fact]
    public void BreakEvenForStoredBasis_WithManualOverride_StillAppliesAcquisitionCostExactlyOnce()
    {
        // Override-Sätze ändern die Formel nicht: die gespeicherte Basis enthält
        // die Erwerbskosten genau einmal — kein Buy-Faktor, auch mit Override.
        var service = new FeeCalculatorService(Options.Create(new FeeOverrideSettings
        {
            BrokerFeeRate = 0.01,
            SalesTaxRate = 0.04
        }));

        var storedBasis = service.CalculateBreakEvenSellPriceForStoredBasis(100, null); // 100/0.95
        var trade = service.CalculateBreakEvenSellPrice(100, 1, null);                  // (100×1.01)/0.95

        Assert.Equal(105.2632, storedBasis, 4);
        Assert.Equal(106.3158, trade, 4);
        Assert.True(storedBasis < trade); // Kauf-Aufschlag erscheint NUR im echten Trade
    }
}