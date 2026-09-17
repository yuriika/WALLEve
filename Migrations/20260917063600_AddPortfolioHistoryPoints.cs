using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WALLEve.Migrations
{
    /// <inheritdoc />
    public partial class AddPortfolioHistoryPoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PortfolioHistoryPoints",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HoldingSnapshotId = table.Column<long>(type: "INTEGER", nullable: false),
                    OwnerType = table.Column<int>(type: "INTEGER", nullable: false),
                    OwnerId = table.Column<int>(type: "INTEGER", nullable: false),
                    CapturedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", nullable: false),
                    ValuationRegionId = table.Column<int>(type: "INTEGER", nullable: true),
                    ValuationHubName = table.Column<string>(type: "TEXT", nullable: true),
                    ValuatedTypeCount = table.Column<int>(type: "INTEGER", nullable: false),
                    UnknownValuationTypeCount = table.Column<int>(type: "INTEGER", nullable: false),
                    AssetsItemCount = table.Column<int>(type: "INTEGER", nullable: false),
                    AssetsQuantity = table.Column<long>(type: "INTEGER", nullable: false),
                    AssetsValue = table.Column<double>(type: "REAL", nullable: true),
                    UnknownValuationItemCount = table.Column<int>(type: "INTEGER", nullable: false),
                    UnknownValuationQuantity = table.Column<long>(type: "INTEGER", nullable: false),
                    EscrowItemCount = table.Column<int>(type: "INTEGER", nullable: false),
                    EscrowQuantity = table.Column<long>(type: "INTEGER", nullable: false),
                    EscrowValue = table.Column<double>(type: "REAL", nullable: true),
                    CostBasisKnownItemCount = table.Column<int>(type: "INTEGER", nullable: false),
                    UnknownCostBasisItemCount = table.Column<int>(type: "INTEGER", nullable: false),
                    KnownBasisValue = table.Column<double>(type: "REAL", nullable: true),
                    UnknownBasisQuantity = table.Column<long>(type: "INTEGER", nullable: false),
                    UnknownBasisMarketValue = table.Column<double>(type: "REAL", nullable: true),
                    WalletTransactionCount = table.Column<int>(type: "INTEGER", nullable: false),
                    WalletCashInflow = table.Column<double>(type: "REAL", nullable: true),
                    WalletCashOutflow = table.Column<double>(type: "REAL", nullable: true),
                    RealizedProfit = table.Column<double>(type: "REAL", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortfolioHistoryPoints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PortfolioHistoryPoints_HoldingSnapshots_HoldingSnapshotId",
                        column: x => x.HoldingSnapshotId,
                        principalTable: "HoldingSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioHistoryPoints_HoldingSnapshotId",
                table: "PortfolioHistoryPoints",
                column: "HoldingSnapshotId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioHistoryPoints_OwnerType_OwnerId_CapturedAt",
                table: "PortfolioHistoryPoints",
                columns: new[] { "OwnerType", "OwnerId", "CapturedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PortfolioHistoryPoints");
        }
    }
}
