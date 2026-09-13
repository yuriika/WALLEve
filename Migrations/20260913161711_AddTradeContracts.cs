using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WALLEve.Migrations
{
    /// <inheritdoc />
    public partial class AddTradeContracts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TradeContracts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TradingOpportunityId = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    CharacterId = table.Column<int>(type: "INTEGER", nullable: false),
                    TypeId = table.Column<int>(type: "INTEGER", nullable: false),
                    AlgorithmVersion = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsActionable = table.Column<bool>(type: "INTEGER", nullable: false),
                    NotActionableReason = table.Column<string>(type: "TEXT", nullable: true),
                    IsLegacy = table.Column<bool>(type: "INTEGER", nullable: false),
                    Evidence = table.Column<string>(type: "TEXT", nullable: false),
                    EstimatedProfit = table.Column<decimal>(type: "TEXT", nullable: false),
                    RequiredCapital = table.Column<decimal>(type: "TEXT", nullable: false),
                    NetRoiPercent = table.Column<decimal>(type: "TEXT", nullable: false),
                    UnitCostBasis = table.Column<decimal>(type: "TEXT", nullable: true),
                    SellPricePerUnit = table.Column<decimal>(type: "TEXT", nullable: true),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: true),
                    SellLocationId = table.Column<long>(type: "INTEGER", nullable: true),
                    SellLocationLabel = table.Column<string>(type: "TEXT", nullable: true),
                    BrokerFee = table.Column<decimal>(type: "TEXT", nullable: true),
                    SalesTax = table.Column<decimal>(type: "TEXT", nullable: true),
                    MarketSnapshotId = table.Column<int>(type: "INTEGER", nullable: true),
                    CostBasisSource = table.Column<string>(type: "TEXT", nullable: true),
                    EstimatedNetProceeds = table.Column<decimal>(type: "TEXT", nullable: true),
                    BreakEvenPrice = table.Column<decimal>(type: "TEXT", nullable: true),
                    OrderId = table.Column<long>(type: "INTEGER", nullable: true),
                    LocationId = table.Column<long>(type: "INTEGER", nullable: true),
                    CurrentPricePerUnit = table.Column<decimal>(type: "TEXT", nullable: true),
                    SuggestedPricePerUnit = table.Column<decimal>(type: "TEXT", nullable: true),
                    BrokerRatePercent = table.Column<decimal>(type: "TEXT", nullable: true),
                    RelistDiscountPercent = table.Column<decimal>(type: "TEXT", nullable: true),
                    ModifyFee = table.Column<decimal>(type: "TEXT", nullable: true),
                    EstimatedExtraNetProceeds = table.Column<decimal>(type: "TEXT", nullable: true),
                    BuyLocationId = table.Column<long>(type: "INTEGER", nullable: true),
                    BuyRegionId = table.Column<int>(type: "INTEGER", nullable: true),
                    SellRegionId = table.Column<int>(type: "INTEGER", nullable: true),
                    BuyPricePerUnit = table.Column<decimal>(type: "TEXT", nullable: true),
                    BuyMarketSnapshotId = table.Column<int>(type: "INTEGER", nullable: true),
                    SellMarketSnapshotId = table.Column<int>(type: "INTEGER", nullable: true),
                    SalesTaxPercent = table.Column<decimal>(type: "TEXT", nullable: true),
                    JumpDistance = table.Column<int>(type: "INTEGER", nullable: true),
                    StartSystemId = table.Column<int>(type: "INTEGER", nullable: true),
                    EndSystemId = table.Column<int>(type: "INTEGER", nullable: true),
                    JumpCount = table.Column<int>(type: "INTEGER", nullable: true),
                    RouteJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeContracts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TradeContracts_BuyMarketSnapshotId",
                table: "TradeContracts",
                column: "BuyMarketSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_TradeContracts_CharacterId_Kind_CreatedAt",
                table: "TradeContracts",
                columns: new[] { "CharacterId", "Kind", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TradeContracts_MarketSnapshotId",
                table: "TradeContracts",
                column: "MarketSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_TradeContracts_SellMarketSnapshotId",
                table: "TradeContracts",
                column: "SellMarketSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_TradeContracts_TradingOpportunityId_Kind",
                table: "TradeContracts",
                columns: new[] { "TradingOpportunityId", "Kind" });

            // Issue #37, Akzeptanzkriterium 3: bestehende Opportunities werden als
            // Legacy-Verträge übernommen — IsLegacy = 1, IsActionable = 0 mit
            // Begründung, Evidenztext 1:1 aus der Opportunity, Beträge 1:1 (BuyPrice →
            // UnitCostBasis, SellPrice → SellPricePerUnit), KEINE erfundene neue
            // Evidenz und keine erfundenen Eingaben (fehlende Werte bleiben NULL).
            migrationBuilder.Sql("""
                INSERT INTO "TradeContracts" (
                    "TradingOpportunityId", "Kind", "CharacterId", "TypeId",
                    "AlgorithmVersion", "CreatedAt", "IsActionable", "NotActionableReason",
                    "IsLegacy", "Evidence", "EstimatedProfit", "RequiredCapital", "NetRoiPercent",
                    "UnitCostBasis", "SellPricePerUnit", "Quantity", "SellLocationId",
                    "SellLocationLabel", "BrokerFee", "SalesTax", "MarketSnapshotId",
                    "CostBasisSource", "EstimatedNetProceeds", "BreakEvenPrice")
                SELECT
                    "Id", 1, "CharacterId", "TypeId",
                    COALESCE("AlgorithmVersion", ''), "DetectedAt", 0,
                    'Legacy-Datensatz (vor Issue #37): Eingaben unvollständig, nicht reproduzierbar — keine neue Evidenz.',
                    1, "Evidence", "EstimatedProfit", "RequiredCapital", 0,
                    "BuyPrice", "SellPrice", NULL, NULL,
                    NULL, NULL, NULL, NULL,
                    NULL, NULL, NULL
                FROM "TradingOpportunities"
                WHERE "OpportunityType" = 'inventory_sell';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TradeContracts");
        }
    }
}
