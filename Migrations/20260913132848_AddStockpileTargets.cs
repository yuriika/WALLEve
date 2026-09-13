using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WALLEve.Migrations
{
    /// <inheritdoc />
    public partial class AddStockpileTargets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StockpileTargets",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OwnerType = table.Column<int>(type: "INTEGER", nullable: false),
                    OwnerId = table.Column<int>(type: "INTEGER", nullable: false),
                    TypeId = table.Column<int>(type: "INTEGER", nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    LocationId = table.Column<long>(type: "INTEGER", nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    IsArchived = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockpileTargets", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockpileTargets_LocationId",
                table: "StockpileTargets",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_StockpileTargets_OwnerType_OwnerId_IsArchived",
                table: "StockpileTargets",
                columns: new[] { "OwnerType", "OwnerId", "IsArchived" });

            migrationBuilder.CreateIndex(
                name: "IX_StockpileTargets_OwnerType_OwnerId_TypeId",
                table: "StockpileTargets",
                columns: new[] { "OwnerType", "OwnerId", "TypeId" },
                unique: true,
                filter: "[IsArchived] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_StockpileTargets_TypeId",
                table: "StockpileTargets",
                column: "TypeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StockpileTargets");
        }
    }
}
