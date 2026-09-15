using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WALLEve.Migrations
{
    /// <inheritdoc />
    public partial class AddTradeStatusChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TradeStatusChanges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TradingOpportunityId = table.Column<int>(type: "INTEGER", nullable: false),
                    CharacterId = table.Column<int>(type: "INTEGER", nullable: false),
                    FromStatus = table.Column<string>(type: "TEXT", nullable: false),
                    ToStatus = table.Column<string>(type: "TEXT", nullable: false),
                    Source = table.Column<int>(type: "INTEGER", nullable: false),
                    ChangedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeStatusChanges", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TradeStatusChanges_CharacterId_ChangedAt",
                table: "TradeStatusChanges",
                columns: new[] { "CharacterId", "ChangedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TradeStatusChanges_ToStatus_Source",
                table: "TradeStatusChanges",
                columns: new[] { "ToStatus", "Source" });

            migrationBuilder.CreateIndex(
                name: "IX_TradeStatusChanges_TradingOpportunityId_ChangedAt",
                table: "TradeStatusChanges",
                columns: new[] { "TradingOpportunityId", "ChangedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TradeStatusChanges");
        }
    }
}
