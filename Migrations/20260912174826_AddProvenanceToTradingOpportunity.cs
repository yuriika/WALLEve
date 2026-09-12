using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WALLEve.Migrations
{
    /// <inheritdoc />
    public partial class AddProvenanceToTradingOpportunity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Ehrliche Provenienz statt AI-Confidence (#33): Die Spalten werden so
            // umbenannt, dass die WERTE an der richtigen Stelle landen:
            // Confidence -> Score (Wert bleibt erhalten, aber als Heuristik-Score
            //   gekennzeichnet, NICHT als AI-Confidence),
            // AIModel -> Provenance (der frühere Modell-Name wird durch 'legacy'
            //   ersetzt, siehe UPDATE unten),
            // Reasoning -> Evidence (die Berechnungstext-Evidenz).
            migrationBuilder.RenameColumn(
                name: "Confidence",
                table: "TradingOpportunities",
                newName: "Score");

            migrationBuilder.RenameColumn(
                name: "AIModel",
                table: "TradingOpportunities",
                newName: "Provenance");

            migrationBuilder.RenameColumn(
                name: "Reasoning",
                table: "TradingOpportunities",
                newName: "Evidence");

            migrationBuilder.RenameIndex(
                name: "IX_TradingOpportunities_Status_Confidence",
                table: "TradingOpportunities",
                newName: "IX_TradingOpportunities_Status_Score");

            migrationBuilder.AddColumn<string>(
                name: "AlgorithmVersion",
                table: "TradingOpportunities",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DataQuality",
                table: "TradingOpportunities",
                type: "TEXT",
                nullable: true);

            // Bestehende Datensätze stammen aus der Zeit der AI-Confidence-Angaben:
            // ihre Confidence-/Score-Werte sind KEINE aktuelle Evidenz. Sie werden
            // explizit als Legacy gekennzeichnet; neue Datensätze schreiben
            // Provenance='heuristic' plus Algorithmusversion (im Service).
            migrationBuilder.Sql(
                "UPDATE TradingOpportunities SET Provenance = 'legacy'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE TradingOpportunities SET Provenance = '' WHERE Provenance = 'legacy'");

            migrationBuilder.DropColumn(
                name: "AlgorithmVersion",
                table: "TradingOpportunities");

            migrationBuilder.DropColumn(
                name: "DataQuality",
                table: "TradingOpportunities");

            migrationBuilder.RenameColumn(
                name: "Score",
                table: "TradingOpportunities",
                newName: "Confidence");

            migrationBuilder.RenameColumn(
                name: "Provenance",
                table: "TradingOpportunities",
                newName: "AIModel");

            migrationBuilder.RenameColumn(
                name: "Evidence",
                table: "TradingOpportunities",
                newName: "Reasoning");

            migrationBuilder.RenameIndex(
                name: "IX_TradingOpportunities_Status_Score",
                table: "TradingOpportunities",
                newName: "IX_TradingOpportunities_Status_Confidence");
        }
    }
}