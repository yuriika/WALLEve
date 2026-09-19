using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WALLEve.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Issue #205: Erweitert StationId des IndustryJobEntry von int auf long.
    /// SQLite's INTEGER ist bereits signed 64-bit, daher kein ALTER TABLE nötig —
    /// der physische Spaltentyp ändert sich nicht. Die Modelländerung (int → long)
    /// wird vollständig im EF-Core-Snapshot (.Designer.cs + WalletDbContextModelSnapshot.cs)
    /// erfasst, den EF zur Schema-Drift-Erkennung bei künftigen Migrationen nutzt.
    /// Analog zu ConvertStockpileQuantitiesToLong.
    /// </summary>
    public partial class ConvertIndustryJobStationIdToLong : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
