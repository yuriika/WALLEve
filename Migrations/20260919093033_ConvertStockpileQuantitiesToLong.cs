using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WALLEve.Migrations
{
    /// <inheritdoc />
    public partial class ConvertStockpileQuantitiesToLong : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SQLite note: SQLite's INTEGER type is already signed 64-bit, so no
            // ALTER TABLE is needed — the physical column type does not change.
            // The model change (int → long) is captured entirely in the EF Core
            // snapshot (.Designer.cs + WalletDbContextModelSnapshot.cs), which
            // EF uses to detect schema drift on future migrations.
            // DO NOT add an invalid ALTER TABLE statement here.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
