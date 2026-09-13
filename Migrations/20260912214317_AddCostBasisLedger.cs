using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WALLEve.Migrations
{
    /// <inheritdoc />
    public partial class AddCostBasisLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CostBasisLedgerEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CharacterId = table.Column<int>(type: "INTEGER", nullable: false),
                    TypeId = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceTransactionId = table.Column<long>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsBuy = table.Column<bool>(type: "INTEGER", nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    UnitPrice = table.Column<double>(type: "REAL", nullable: false),
                    ImportedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CostBasisLedgerEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CostBasisLedgerEntries_CharacterId_TypeId_Date",
                table: "CostBasisLedgerEntries",
                columns: new[] { "CharacterId", "TypeId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_CostBasisLedgerEntries_CharacterId_TypeId_SourceTransactionId",
                table: "CostBasisLedgerEntries",
                columns: new[] { "CharacterId", "TypeId", "SourceTransactionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CostBasisLedgerEntries_Date",
                table: "CostBasisLedgerEntries",
                column: "Date");

            // Backfill: bereits gespiegelte Wallet-Historie idempotent ins Ledger
            // übernehmen, damit bestehende Installationen nicht ihre vollständige
            // Ereignisfolge verlieren (Replay liest ausschließlich das Ledger).
            // Der Unique-Index verhindert dabei jede Duplikatzeile.
            migrationBuilder.Sql(
                """
                INSERT INTO "CostBasisLedgerEntries"
                    ("CharacterId", "TypeId", "SourceTransactionId", "Date",
                     "IsBuy", "Quantity", "UnitPrice", "ImportedAt")
                SELECT "CharacterId", "TypeId", "TransactionId", "Date",
                       "IsBuy", "Quantity", "UnitPrice", strftime('%Y-%m-%dT%H:%M:%fZ', 'now')
                FROM "WalletTransactionRecords";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CostBasisLedgerEntries");
        }
    }
}
