using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WALLEve.Migrations
{
    /// <inheritdoc />
    public partial class AddIndustryJobEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IndustryJobEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CharacterId = table.Column<int>(type: "INTEGER", nullable: false),
                    JobId = table.Column<int>(type: "INTEGER", nullable: false),
                    ActivityId = table.Column<int>(type: "INTEGER", nullable: false),
                    BlueprintId = table.Column<long>(type: "INTEGER", nullable: false),
                    BlueprintTypeId = table.Column<int>(type: "INTEGER", nullable: false),
                    BlueprintLocationId = table.Column<long>(type: "INTEGER", nullable: false),
                    OutputLocationId = table.Column<long>(type: "INTEGER", nullable: false),
                    FacilityId = table.Column<long>(type: "INTEGER", nullable: false),
                    StationId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductTypeId = table.Column<int>(type: "INTEGER", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    StartDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PauseDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Runs = table.Column<int>(type: "INTEGER", nullable: false),
                    LicensedRuns = table.Column<int>(type: "INTEGER", nullable: true),
                    SuccessfulRuns = table.Column<int>(type: "INTEGER", nullable: true),
                    Cost = table.Column<double>(type: "REAL", nullable: true),
                    Duration = table.Column<int>(type: "INTEGER", nullable: false),
                    InstallerId = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IndustryJobEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IndustryJobEntries_CharacterId_EndDate",
                table: "IndustryJobEntries",
                columns: new[] { "CharacterId", "EndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_IndustryJobEntries_CharacterId_JobId",
                table: "IndustryJobEntries",
                columns: new[] { "CharacterId", "JobId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IndustryJobEntries_CharacterId_Status",
                table: "IndustryJobEntries",
                columns: new[] { "CharacterId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IndustryJobEntries");
        }
    }
}
