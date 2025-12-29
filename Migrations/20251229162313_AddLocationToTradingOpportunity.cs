using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WALLEve.Migrations
{
    /// <inheritdoc />
    public partial class AddLocationToTradingOpportunity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "BuyLocationId",
                table: "TradingOpportunities",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SellLocationId",
                table: "TradingOpportunities",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BuyLocationId",
                table: "TradingOpportunities");

            migrationBuilder.DropColumn(
                name: "SellLocationId",
                table: "TradingOpportunities");
        }
    }
}
