using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WALLEve.Migrations
{
    /// <inheritdoc />
    public partial class AddTradeProfileSignalState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CooldownMinutes",
                table: "TradeProfiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastReportedAt",
                table: "TradeProfiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastReportedFingerprint",
                table: "TradeProfiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SignalDeactivated",
                table: "TradeProfiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CooldownMinutes",
                table: "TradeProfiles");

            migrationBuilder.DropColumn(
                name: "LastReportedAt",
                table: "TradeProfiles");

            migrationBuilder.DropColumn(
                name: "LastReportedFingerprint",
                table: "TradeProfiles");

            migrationBuilder.DropColumn(
                name: "SignalDeactivated",
                table: "TradeProfiles");
        }
    }
}
