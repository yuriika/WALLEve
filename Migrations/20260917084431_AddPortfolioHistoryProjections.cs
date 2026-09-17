using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WALLEve.Migrations
{
    /// <inheritdoc />
    public partial class AddPortfolioHistoryProjections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PortfolioHistoryCategories",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PointId = table.Column<long>(type: "INTEGER", nullable: false),
                    Category = table.Column<string>(type: "TEXT", nullable: false),
                    ItemCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Quantity = table.Column<long>(type: "INTEGER", nullable: false),
                    Value = table.Column<double>(type: "REAL", nullable: true),
                    EscrowItemCount = table.Column<int>(type: "INTEGER", nullable: false),
                    EscrowQuantity = table.Column<long>(type: "INTEGER", nullable: false),
                    EscrowValue = table.Column<double>(type: "REAL", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortfolioHistoryCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PortfolioHistoryCategories_PortfolioHistoryPoints_PointId",
                        column: x => x.PointId,
                        principalTable: "PortfolioHistoryPoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PortfolioHistoryLocations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PointId = table.Column<long>(type: "INTEGER", nullable: false),
                    LocationId = table.Column<long>(type: "INTEGER", nullable: false),
                    LocationFlag = table.Column<string>(type: "TEXT", nullable: false),
                    ItemCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Quantity = table.Column<long>(type: "INTEGER", nullable: false),
                    Value = table.Column<double>(type: "REAL", nullable: true),
                    EscrowItemCount = table.Column<int>(type: "INTEGER", nullable: false),
                    EscrowQuantity = table.Column<long>(type: "INTEGER", nullable: false),
                    EscrowValue = table.Column<double>(type: "REAL", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortfolioHistoryLocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PortfolioHistoryLocations_PortfolioHistoryPoints_PointId",
                        column: x => x.PointId,
                        principalTable: "PortfolioHistoryPoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioHistoryCategories_PointId",
                table: "PortfolioHistoryCategories",
                column: "PointId");

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioHistoryLocations_PointId",
                table: "PortfolioHistoryLocations",
                column: "PointId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PortfolioHistoryCategories");

            migrationBuilder.DropTable(
                name: "PortfolioHistoryLocations");
        }
    }
}
