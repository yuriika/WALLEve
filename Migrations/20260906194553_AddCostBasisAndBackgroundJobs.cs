using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WALLEve.Migrations
{
    /// <inheritdoc />
    public partial class AddCostBasisAndBackgroundJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BackgroundJobs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    JobType = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: false),
                    CharacterId = table.Column<int>(type: "INTEGER", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Current = table.Column<int>(type: "INTEGER", nullable: false),
                    Total = table.Column<int>(type: "INTEGER", nullable: false),
                    ParametersJson = table.Column<string>(type: "TEXT", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackgroundJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CostBasisEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CharacterId = table.Column<int>(type: "INTEGER", nullable: false),
                    TypeId = table.Column<int>(type: "INTEGER", nullable: false),
                    Value = table.Column<double>(type: "REAL", nullable: true),
                    Source = table.Column<int>(type: "INTEGER", nullable: false),
                    PurchaseDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    EstimateRegionId = table.Column<int>(type: "INTEGER", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CostBasisEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WalletTransactionRecords",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CharacterId = table.Column<int>(type: "INTEGER", nullable: false),
                    TransactionId = table.Column<long>(type: "INTEGER", nullable: false),
                    TypeId = table.Column<int>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsBuy = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsPersonal = table.Column<bool>(type: "INTEGER", nullable: false),
                    JournalRefId = table.Column<long>(type: "INTEGER", nullable: false),
                    LocationId = table.Column<long>(type: "INTEGER", nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    UnitPrice = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalletTransactionRecords", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BackgroundJobs_JobType_Status",
                table: "BackgroundJobs",
                columns: new[] { "JobType", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_BackgroundJobs_UpdatedAt",
                table: "BackgroundJobs",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_CostBasisEntries_CharacterId_TypeId",
                table: "CostBasisEntries",
                columns: new[] { "CharacterId", "TypeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CostBasisEntries_Source",
                table: "CostBasisEntries",
                column: "Source");

            migrationBuilder.CreateIndex(
                name: "IX_CostBasisEntries_UpdatedAt",
                table: "CostBasisEntries",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_WalletTransactionRecords_CharacterId_TransactionId",
                table: "WalletTransactionRecords",
                columns: new[] { "CharacterId", "TransactionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WalletTransactionRecords_CharacterId_TypeId_IsBuy",
                table: "WalletTransactionRecords",
                columns: new[] { "CharacterId", "TypeId", "IsBuy" });

            migrationBuilder.CreateIndex(
                name: "IX_WalletTransactionRecords_Date",
                table: "WalletTransactionRecords",
                column: "Date");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BackgroundJobs");

            migrationBuilder.DropTable(
                name: "CostBasisEntries");

            migrationBuilder.DropTable(
                name: "WalletTransactionRecords");
        }
    }
}
