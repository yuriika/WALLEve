using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WALLEve.Migrations
{
    /// <inheritdoc />
    public partial class AddFeeOriginToTradingOpportunity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BrokerFeeOrigin",
                table: "TradingOpportunities",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "BrokerFeeRate",
                table: "TradingOpportunities",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FeeEvaluatedAtUtc",
                table: "TradingOpportunities",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SalesTaxOrigin",
                table: "TradingOpportunities",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "SalesTaxRate",
                table: "TradingOpportunities",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StandingsOrigin",
                table: "TradingOpportunities",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BrokerFeeOrigin",
                table: "TradingOpportunities");

            migrationBuilder.DropColumn(
                name: "BrokerFeeRate",
                table: "TradingOpportunities");

            migrationBuilder.DropColumn(
                name: "FeeEvaluatedAtUtc",
                table: "TradingOpportunities");

            migrationBuilder.DropColumn(
                name: "SalesTaxOrigin",
                table: "TradingOpportunities");

            migrationBuilder.DropColumn(
                name: "SalesTaxRate",
                table: "TradingOpportunities");

            migrationBuilder.DropColumn(
                name: "StandingsOrigin",
                table: "TradingOpportunities");
        }
    }
}
