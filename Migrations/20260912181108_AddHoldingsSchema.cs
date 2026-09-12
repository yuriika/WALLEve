using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WALLEve.Migrations
{
    /// <inheritdoc />
    public partial class AddHoldingsSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HoldingSyncRuns",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OwnerType = table.Column<int>(type: "INTEGER", nullable: false),
                    OwnerId = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Error = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HoldingSyncRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HoldingSnapshots",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SyncRunId = table.Column<long>(type: "INTEGER", nullable: false),
                    OwnerType = table.Column<int>(type: "INTEGER", nullable: false),
                    OwnerId = table.Column<int>(type: "INTEGER", nullable: false),
                    SyncedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HoldingSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HoldingSnapshots_HoldingSyncRuns_SyncRunId",
                        column: x => x.SyncRunId,
                        principalTable: "HoldingSyncRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HoldingItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SnapshotId = table.Column<long>(type: "INTEGER", nullable: false),
                    ItemId = table.Column<long>(type: "INTEGER", nullable: false),
                    TypeId = table.Column<int>(type: "INTEGER", nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    IsSingleton = table.Column<bool>(type: "INTEGER", nullable: false),
                    LocationId = table.Column<long>(type: "INTEGER", nullable: false),
                    LocationFlag = table.Column<string>(type: "TEXT", nullable: false),
                    ParentItemId = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HoldingItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HoldingItems_HoldingSnapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "HoldingSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HoldingItems_SnapshotId_TypeId",
                table: "HoldingItems",
                columns: new[] { "SnapshotId", "TypeId" });

            migrationBuilder.CreateIndex(
                name: "IX_HoldingItems_TypeId_IsSingleton",
                table: "HoldingItems",
                columns: new[] { "TypeId", "IsSingleton" });

            migrationBuilder.CreateIndex(
                name: "IX_HoldingSnapshots_OwnerType_OwnerId_SyncedAt",
                table: "HoldingSnapshots",
                columns: new[] { "OwnerType", "OwnerId", "SyncedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_HoldingSnapshots_SyncRunId",
                table: "HoldingSnapshots",
                column: "SyncRunId");

            migrationBuilder.CreateIndex(
                name: "IX_HoldingSyncRuns_OwnerType_OwnerId_StartedAt",
                table: "HoldingSyncRuns",
                columns: new[] { "OwnerType", "OwnerId", "StartedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HoldingItems");

            migrationBuilder.DropTable(
                name: "HoldingSnapshots");

            migrationBuilder.DropTable(
                name: "HoldingSyncRuns");
        }
    }
}
