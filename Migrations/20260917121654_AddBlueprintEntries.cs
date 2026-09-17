using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WALLEve.Migrations
{
    /// <inheritdoc />
    public partial class AddBlueprintEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BlueprintEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CharacterId = table.Column<int>(type: "INTEGER", nullable: false),
                    ItemId = table.Column<long>(type: "INTEGER", nullable: false),
                    TypeId = table.Column<int>(type: "INTEGER", nullable: false),
                    LocationId = table.Column<long>(type: "INTEGER", nullable: false),
                    LocationFlag = table.Column<string>(type: "TEXT", nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    MaterialEfficiency = table.Column<int>(type: "INTEGER", nullable: false),
                    TimeEfficiency = table.Column<int>(type: "INTEGER", nullable: false),
                    Runs = table.Column<int>(type: "INTEGER", nullable: false),
                    IsBlueprintCopy = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlueprintEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BlueprintEntries_CharacterId_ItemId",
                table: "BlueprintEntries",
                columns: new[] { "CharacterId", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BlueprintEntries_CharacterId_TypeId",
                table: "BlueprintEntries",
                columns: new[] { "CharacterId", "TypeId" });

            migrationBuilder.CreateIndex(
                name: "IX_BlueprintEntries_TypeId",
                table: "BlueprintEntries",
                column: "TypeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BlueprintEntries");
        }
    }
}
