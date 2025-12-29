using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WALLEve.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketAnalysisTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MarketHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RegionId = table.Column<int>(type: "INTEGER", nullable: false),
                    TypeId = table.Column<int>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Average = table.Column<double>(type: "REAL", nullable: false),
                    Highest = table.Column<double>(type: "REAL", nullable: false),
                    Lowest = table.Column<double>(type: "REAL", nullable: false),
                    Volume = table.Column<long>(type: "INTEGER", nullable: false),
                    OrderCount = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketHistory", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MarketSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RegionId = table.Column<int>(type: "INTEGER", nullable: false),
                    TypeId = table.Column<int>(type: "INTEGER", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    BestBuyPrice = table.Column<double>(type: "REAL", nullable: true),
                    BestSellPrice = table.Column<double>(type: "REAL", nullable: true),
                    BuyVolume = table.Column<int>(type: "INTEGER", nullable: false),
                    SellVolume = table.Column<int>(type: "INTEGER", nullable: false),
                    Spread = table.Column<double>(type: "REAL", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MarketTrends",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TypeId = table.Column<int>(type: "INTEGER", nullable: false),
                    RegionId = table.Column<int>(type: "INTEGER", nullable: false),
                    TrendType = table.Column<string>(type: "TEXT", nullable: false),
                    Strength = table.Column<double>(type: "REAL", nullable: false),
                    PredictedChange = table.Column<double>(type: "REAL", nullable: true),
                    TimeWindow = table.Column<string>(type: "TEXT", nullable: false),
                    AnalyzedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AIModel = table.Column<string>(type: "TEXT", nullable: false),
                    Features = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketTrends", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TradingOpportunities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TypeId = table.Column<int>(type: "INTEGER", nullable: false),
                    OpportunityType = table.Column<string>(type: "TEXT", nullable: false),
                    BuyRegionId = table.Column<int>(type: "INTEGER", nullable: true),
                    SellRegionId = table.Column<int>(type: "INTEGER", nullable: true),
                    BuySystemId = table.Column<int>(type: "INTEGER", nullable: true),
                    SellSystemId = table.Column<int>(type: "INTEGER", nullable: true),
                    JumpDistance = table.Column<int>(type: "INTEGER", nullable: true),
                    RouteSecurityAnalysis = table.Column<string>(type: "TEXT", nullable: true),
                    BuyPrice = table.Column<double>(type: "REAL", nullable: true),
                    SellPrice = table.Column<double>(type: "REAL", nullable: true),
                    EstimatedProfit = table.Column<double>(type: "REAL", nullable: false),
                    RequiredCapital = table.Column<double>(type: "REAL", nullable: false),
                    Confidence = table.Column<double>(type: "REAL", nullable: false),
                    AIModel = table.Column<string>(type: "TEXT", nullable: false),
                    Reasoning = table.Column<string>(type: "TEXT", nullable: false),
                    DetectedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    ExecutedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ActualProfit = table.Column<double>(type: "REAL", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradingOpportunities", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MarketHistory_RegionId_TypeId_Date",
                table: "MarketHistory",
                columns: new[] { "RegionId", "TypeId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MarketHistory_TypeId_Date",
                table: "MarketHistory",
                columns: new[] { "TypeId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketSnapshots_RegionId_TypeId_Timestamp",
                table: "MarketSnapshots",
                columns: new[] { "RegionId", "TypeId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketSnapshots_Timestamp",
                table: "MarketSnapshots",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_MarketTrends_RegionId_TypeId",
                table: "MarketTrends",
                columns: new[] { "RegionId", "TypeId" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketTrends_TrendType_Strength",
                table: "MarketTrends",
                columns: new[] { "TrendType", "Strength" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketTrends_TypeId_AnalyzedAt",
                table: "MarketTrends",
                columns: new[] { "TypeId", "AnalyzedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TradingOpportunities_BuyRegionId",
                table: "TradingOpportunities",
                column: "BuyRegionId");

            migrationBuilder.CreateIndex(
                name: "IX_TradingOpportunities_DetectedAt",
                table: "TradingOpportunities",
                column: "DetectedAt");

            migrationBuilder.CreateIndex(
                name: "IX_TradingOpportunities_SellRegionId",
                table: "TradingOpportunities",
                column: "SellRegionId");

            migrationBuilder.CreateIndex(
                name: "IX_TradingOpportunities_Status_Confidence",
                table: "TradingOpportunities",
                columns: new[] { "Status", "Confidence" });

            migrationBuilder.CreateIndex(
                name: "IX_TradingOpportunities_Status_ExpiresAt",
                table: "TradingOpportunities",
                columns: new[] { "Status", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TradingOpportunities_TypeId_Status",
                table: "TradingOpportunities",
                columns: new[] { "TypeId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MarketHistory");

            migrationBuilder.DropTable(
                name: "MarketSnapshots");

            migrationBuilder.DropTable(
                name: "MarketTrends");

            migrationBuilder.DropTable(
                name: "TradingOpportunities");
        }
    }
}
