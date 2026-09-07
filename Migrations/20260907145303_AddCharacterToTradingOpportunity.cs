using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WALLEve.Migrations
{
    /// <inheritdoc />
    public partial class AddCharacterToTradingOpportunity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CharacterId",
                table: "TradingOpportunities",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_TradingOpportunities_CharacterId_Status_OpportunityType",
                table: "TradingOpportunities",
                columns: new[] { "CharacterId", "Status", "OpportunityType" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TradingOpportunities_CharacterId_Status_OpportunityType",
                table: "TradingOpportunities");

            migrationBuilder.DropColumn(
                name: "CharacterId",
                table: "TradingOpportunities");
        }
    }
}
