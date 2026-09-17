using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WALLEve.Migrations
{
    /// <inheritdoc />
    public partial class AddMiningLedgerEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MiningLedgerEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CharacterId = table.Column<int>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TypeId = table.Column<int>(type: "INTEGER", nullable: false),
                    SolarSystemId = table.Column<int>(type: "INTEGER", nullable: false),
                    Quantity = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiningLedgerEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MiningLedgerEntries_CharacterId_Date",
                table: "MiningLedgerEntries",
                columns: new[] { "CharacterId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_MiningLedgerEntries_CharacterId_Date_TypeId_SolarSystemId",
                table: "MiningLedgerEntries",
                columns: new[] { "CharacterId", "Date", "TypeId", "SolarSystemId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MiningLedgerEntries");
        }
    }
}
