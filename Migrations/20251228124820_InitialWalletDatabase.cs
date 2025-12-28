using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WALLEve.Migrations
{
    /// <inheritdoc />
    public partial class InitialWalletDatabase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Characters",
                columns: table => new
                {
                    CharacterId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CharacterName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    LastSyncedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Characters", x => x.CharacterId);
                });

            migrationBuilder.CreateTable(
                name: "Corporations",
                columns: table => new
                {
                    CorporationId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CorporationName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    LastSyncedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Corporations", x => x.CorporationId);
                });

            migrationBuilder.CreateTable(
                name: "Links",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceEntryId = table.Column<long>(type: "INTEGER", nullable: false),
                    TargetEntryId = table.Column<long>(type: "INTEGER", nullable: false),
                    CharacterId = table.Column<int>(type: "INTEGER", nullable: true),
                    CorporationId = table.Column<int>(type: "INTEGER", nullable: true),
                    Division = table.Column<int>(type: "INTEGER", nullable: true),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Confidence = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsManuallyVerified = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsManuallyRejected = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    UserNotes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Links", x => x.Id);
                    table.CheckConstraint("CK_WalletEntryLink_CharacterOrCorp", "(CharacterId IS NOT NULL AND CorporationId IS NULL) OR (CharacterId IS NULL AND CorporationId IS NOT NULL)");
                    table.CheckConstraint("CK_WalletEntryLink_DivisionForCorpOnly", "(CorporationId IS NOT NULL AND Division BETWEEN 1 AND 7) OR (CorporationId IS NULL AND Division IS NULL)");
                    table.ForeignKey(
                        name: "FK_Links_Characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "Characters",
                        principalColumn: "CharacterId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Links_Corporations_CorporationId",
                        column: x => x.CorporationId,
                        principalTable: "Corporations",
                        principalColumn: "CorporationId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Characters_CharacterName",
                table: "Characters",
                column: "CharacterName");

            migrationBuilder.CreateIndex(
                name: "IX_Characters_LastSyncedAt",
                table: "Characters",
                column: "LastSyncedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Corporations_CorporationName",
                table: "Corporations",
                column: "CorporationName");

            migrationBuilder.CreateIndex(
                name: "IX_Corporations_LastSyncedAt",
                table: "Corporations",
                column: "LastSyncedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Links_CharacterId_SourceEntryId",
                table: "Links",
                columns: new[] { "CharacterId", "SourceEntryId" });

            migrationBuilder.CreateIndex(
                name: "IX_Links_CharacterId_TargetEntryId",
                table: "Links",
                columns: new[] { "CharacterId", "TargetEntryId" });

            migrationBuilder.CreateIndex(
                name: "IX_Links_Confidence",
                table: "Links",
                column: "Confidence");

            migrationBuilder.CreateIndex(
                name: "IX_Links_CorporationId_Division_SourceEntryId",
                table: "Links",
                columns: new[] { "CorporationId", "Division", "SourceEntryId" });

            migrationBuilder.CreateIndex(
                name: "IX_Links_CorporationId_Division_TargetEntryId",
                table: "Links",
                columns: new[] { "CorporationId", "Division", "TargetEntryId" });

            migrationBuilder.CreateIndex(
                name: "IX_Links_CreatedAt",
                table: "Links",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Links_IsManuallyRejected",
                table: "Links",
                column: "IsManuallyRejected");

            migrationBuilder.CreateIndex(
                name: "IX_Links_IsManuallyVerified",
                table: "Links",
                column: "IsManuallyVerified");

            migrationBuilder.CreateIndex(
                name: "IX_Links_SourceEntryId_TargetEntryId",
                table: "Links",
                columns: new[] { "SourceEntryId", "TargetEntryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Links_Type",
                table: "Links",
                column: "Type");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Links");

            migrationBuilder.DropTable(
                name: "Characters");

            migrationBuilder.DropTable(
                name: "Corporations");
        }
    }
}
