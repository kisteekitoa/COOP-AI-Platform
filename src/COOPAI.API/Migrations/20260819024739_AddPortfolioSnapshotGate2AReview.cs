using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace COOPAI.API.Migrations
{
    /// <inheritdoc />
    public partial class AddPortfolioSnapshotGate2AReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RejectedAt",
                table: "PortfolioSnapshots",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "PortfolioSnapshots",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceRowNumber",
                table: "PortfolioSnapshotRecords",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioSnapshots_SourceFileHash_AsOfDate_DefinitionVersion_SnapshotContentHash",
                table: "PortfolioSnapshots",
                columns: new[] { "SourceFileHash", "AsOfDate", "DefinitionVersion", "SnapshotContentHash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PortfolioSnapshots_SourceFileHash_AsOfDate_DefinitionVersion_SnapshotContentHash",
                table: "PortfolioSnapshots");

            migrationBuilder.DropColumn(
                name: "RejectedAt",
                table: "PortfolioSnapshots");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "PortfolioSnapshots");

            migrationBuilder.DropColumn(
                name: "SourceRowNumber",
                table: "PortfolioSnapshotRecords");
        }
    }
}
