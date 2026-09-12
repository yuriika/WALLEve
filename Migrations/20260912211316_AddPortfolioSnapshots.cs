using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WALLEve.Migrations
{
    /// <inheritdoc />
    public partial class AddPortfolioSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PortfolioSnapshots",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HoldingSnapshotId = table.Column<long>(type: "INTEGER", nullable: false),
                    OwnerType = table.Column<int>(type: "INTEGER", nullable: false),
                    OwnerId = table.Column<int>(type: "INTEGER", nullable: false),
                    CapturedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TotalItems = table.Column<int>(type: "INTEGER", nullable: false),
                    ValuedItemCount = table.Column<int>(type: "INTEGER", nullable: false),
                    UnknownValuationItemCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CostBasisKnownItemCount = table.Column<int>(type: "INTEGER", nullable: false),
                    UnknownCostBasisItemCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortfolioSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PortfolioSnapshots_HoldingSnapshots_HoldingSnapshotId",
                        column: x => x.HoldingSnapshotId,
                        principalTable: "HoldingSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioSnapshots_HoldingSnapshotId",
                table: "PortfolioSnapshots",
                column: "HoldingSnapshotId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioSnapshots_OwnerType_OwnerId_CapturedAt",
                table: "PortfolioSnapshots",
                columns: new[] { "OwnerType", "OwnerId", "CapturedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PortfolioSnapshots");
        }
    }
}
