using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WALLEve.Migrations
{
    /// <inheritdoc />
    public partial class AddLocationToMarketSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "BestBuyLocationId",
                table: "MarketSnapshots",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BestBuySystemId",
                table: "MarketSnapshots",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "BestSellLocationId",
                table: "MarketSnapshots",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BestSellSystemId",
                table: "MarketSnapshots",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BestBuyLocationId",
                table: "MarketSnapshots");

            migrationBuilder.DropColumn(
                name: "BestBuySystemId",
                table: "MarketSnapshots");

            migrationBuilder.DropColumn(
                name: "BestSellLocationId",
                table: "MarketSnapshots");

            migrationBuilder.DropColumn(
                name: "BestSellSystemId",
                table: "MarketSnapshots");
        }
    }
}
