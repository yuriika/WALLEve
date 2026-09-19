using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WALLEve.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Issue #180: Normalisiert BrokerFeeRate/SalesTaxRate bestehender
    /// station_trading-Opportunities von Prozent (1.5 = 1,5 %) auf
    /// dimensionslose Rate (0.015 = 1,5 %). Nur station_trading-Zeilen
    /// werden angefasst — inventory_sell-Zeilen haben bereits die korrekte
    /// Rate, route_trade wird noch nicht produziert.
    /// </summary>
    public partial class NormalizeStationTradeFeeUnits : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "TradingOpportunities"
                SET "BrokerFeeRate" = "BrokerFeeRate" / 100.0,
                    "SalesTaxRate" = "SalesTaxRate" / 100.0
                WHERE "OpportunityType" = 'station_trading'
                  AND "BrokerFeeRate" IS NOT NULL
                  AND "SalesTaxRate" IS NOT NULL
                  AND "BrokerFeeRate" > 1.0;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "TradingOpportunities"
                SET "BrokerFeeRate" = "BrokerFeeRate" * 100.0,
                    "SalesTaxRate" = "SalesTaxRate" * 100.0
                WHERE "OpportunityType" = 'station_trading'
                  AND "BrokerFeeRate" IS NOT NULL
                  AND "SalesTaxRate" IS NOT NULL
                  AND "BrokerFeeRate" < 1.0;
                """);
        }
    }
}