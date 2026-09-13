using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WALLEve.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketHubProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MarketHubProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    RegionId = table.Column<int>(type: "INTEGER", nullable: false),
                    SystemId = table.Column<int>(type: "INTEGER", nullable: false),
                    LocationId = table.Column<long>(type: "INTEGER", nullable: true),
                    IsActiveHub = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsComparisonMarket = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketHubProfiles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MarketHubProfiles_IsActiveHub",
                table: "MarketHubProfiles",
                column: "IsActiveHub");

            migrationBuilder.CreateIndex(
                name: "IX_MarketHubProfiles_IsComparisonMarket",
                table: "MarketHubProfiles",
                column: "IsComparisonMarket");

            migrationBuilder.CreateIndex(
                name: "IX_MarketHubProfiles_SystemId",
                table: "MarketHubProfiles",
                column: "SystemId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MarketHubProfiles");
        }
    }
}
