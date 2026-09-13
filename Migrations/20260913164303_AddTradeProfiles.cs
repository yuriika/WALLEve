using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WALLEve.Migrations
{
    /// <inheritdoc />
    public partial class AddTradeProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TradeProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CharacterId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    MaxCapital = table.Column<decimal>(type: "TEXT", nullable: true),
                    MaxCargoVolume = table.Column<decimal>(type: "TEXT", nullable: true),
                    MaxJumps = table.Column<int>(type: "INTEGER", nullable: true),
                    AllowHighSec = table.Column<bool>(type: "INTEGER", nullable: false),
                    AllowLowSec = table.Column<bool>(type: "INTEGER", nullable: false),
                    AllowNullSec = table.Column<bool>(type: "INTEGER", nullable: false),
                    MinVolumeM3 = table.Column<decimal>(type: "TEXT", nullable: true),
                    MinProfit = table.Column<decimal>(type: "TEXT", nullable: true),
                    MinQualityScore = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeProfiles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TradeProfiles_CharacterId",
                table: "TradeProfiles",
                column: "CharacterId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TradeProfiles_CharacterId_UpdatedAt",
                table: "TradeProfiles",
                columns: new[] { "CharacterId", "UpdatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TradeProfiles");
        }
    }
}
