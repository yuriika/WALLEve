using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WALLEve.Migrations
{
    /// <inheritdoc />
    public partial class AddRecommendationAttributions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActualNetCorrections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AttributionId = table.Column<int>(type: "INTEGER", nullable: false),
                    TradingOpportunityId = table.Column<int>(type: "INTEGER", nullable: false),
                    CharacterId = table.Column<int>(type: "INTEGER", nullable: false),
                    PreviousActualNet = table.Column<decimal>(type: "TEXT", nullable: true),
                    NewActualNet = table.Column<decimal>(type: "TEXT", nullable: true),
                    Reason = table.Column<string>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", nullable: false),
                    CorrectedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActualNetCorrections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AttributionTransactionLinks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AttributionId = table.Column<int>(type: "INTEGER", nullable: false),
                    TradingOpportunityId = table.Column<int>(type: "INTEGER", nullable: false),
                    CharacterId = table.Column<int>(type: "INTEGER", nullable: false),
                    TransactionId = table.Column<long>(type: "INTEGER", nullable: false),
                    TypeId = table.Column<int>(type: "INTEGER", nullable: false),
                    TransactionDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Side = table.Column<string>(type: "TEXT", nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    UnitPrice = table.Column<double>(type: "REAL", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttributionTransactionLinks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RecommendationAttributions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TradingOpportunityId = table.Column<int>(type: "INTEGER", nullable: false),
                    CharacterId = table.Column<int>(type: "INTEGER", nullable: false),
                    TypeId = table.Column<int>(type: "INTEGER", nullable: false),
                    Side = table.Column<string>(type: "TEXT", nullable: false),
                    MatchState = table.Column<string>(type: "TEXT", nullable: false),
                    ExpectedQuantity = table.Column<int>(type: "INTEGER", nullable: false),
                    AttributedQuantity = table.Column<int>(type: "INTEGER", nullable: false),
                    ExpectedNetMin = table.Column<decimal>(type: "TEXT", nullable: false),
                    ExpectedNetMax = table.Column<decimal>(type: "TEXT", nullable: false),
                    ActualNet = table.Column<decimal>(type: "TEXT", nullable: true),
                    FeeKnowledge = table.Column<string>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", nullable: false),
                    WindowStartUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    WindowEndUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecommendationAttributions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActualNetCorrections_AttributionId_CorrectedAt",
                table: "ActualNetCorrections",
                columns: new[] { "AttributionId", "CorrectedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ActualNetCorrections_CharacterId_CorrectedAt",
                table: "ActualNetCorrections",
                columns: new[] { "CharacterId", "CorrectedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AttributionTransactionLinks_AttributionId_TransactionId",
                table: "AttributionTransactionLinks",
                columns: new[] { "AttributionId", "TransactionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttributionTransactionLinks_CharacterId_TransactionId",
                table: "AttributionTransactionLinks",
                columns: new[] { "CharacterId", "TransactionId" });

            migrationBuilder.CreateIndex(
                name: "IX_AttributionTransactionLinks_TradingOpportunityId_TransactionDate",
                table: "AttributionTransactionLinks",
                columns: new[] { "TradingOpportunityId", "TransactionDate" });

            migrationBuilder.CreateIndex(
                name: "IX_RecommendationAttributions_CharacterId_MatchState",
                table: "RecommendationAttributions",
                columns: new[] { "CharacterId", "MatchState" });

            migrationBuilder.CreateIndex(
                name: "IX_RecommendationAttributions_TradingOpportunityId_CharacterId",
                table: "RecommendationAttributions",
                columns: new[] { "TradingOpportunityId", "CharacterId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecommendationAttributions_TypeId_Side_WindowStartUtc",
                table: "RecommendationAttributions",
                columns: new[] { "TypeId", "Side", "WindowStartUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActualNetCorrections");

            migrationBuilder.DropTable(
                name: "AttributionTransactionLinks");

            migrationBuilder.DropTable(
                name: "RecommendationAttributions");
        }
    }
}
