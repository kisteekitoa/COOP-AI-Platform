using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace COOPAI.API.Migrations
{
    /// <inheritdoc />
    public partial class AddPortfolioSnapshotGate2B2Publish : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ConcurrencyVersion",
                table: "PortfolioSnapshots",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTime>(
                name: "PublishedAt",
                table: "PortfolioSnapshots",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PublishedByUserId",
                table: "PortfolioSnapshots",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SupersededAt",
                table: "PortfolioSnapshots",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SupersededBySnapshotId",
                table: "PortfolioSnapshots",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioSnapshots_PublishedByUserId",
                table: "PortfolioSnapshots",
                column: "PublishedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioSnapshots_SupersededBySnapshotId",
                table: "PortfolioSnapshots",
                column: "SupersededBySnapshotId");

            migrationBuilder.CreateIndex(
                name: "UX_PortfolioSnapshots_OneCurrentPublished",
                table: "PortfolioSnapshots",
                column: "Status",
                unique: true,
                filter: "[Status] = 'Published'");

            migrationBuilder.AddForeignKey(
                name: "FK_PortfolioSnapshots_AspNetUsers_PublishedByUserId",
                table: "PortfolioSnapshots",
                column: "PublishedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_PortfolioSnapshots_PortfolioSnapshots_SupersededBySnapshotId",
                table: "PortfolioSnapshots",
                column: "SupersededBySnapshotId",
                principalTable: "PortfolioSnapshots",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PortfolioSnapshots_AspNetUsers_PublishedByUserId",
                table: "PortfolioSnapshots");

            migrationBuilder.DropForeignKey(
                name: "FK_PortfolioSnapshots_PortfolioSnapshots_SupersededBySnapshotId",
                table: "PortfolioSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_PortfolioSnapshots_PublishedByUserId",
                table: "PortfolioSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_PortfolioSnapshots_SupersededBySnapshotId",
                table: "PortfolioSnapshots");

            migrationBuilder.DropIndex(
                name: "UX_PortfolioSnapshots_OneCurrentPublished",
                table: "PortfolioSnapshots");

            migrationBuilder.DropColumn(
                name: "ConcurrencyVersion",
                table: "PortfolioSnapshots");

            migrationBuilder.DropColumn(
                name: "PublishedAt",
                table: "PortfolioSnapshots");

            migrationBuilder.DropColumn(
                name: "PublishedByUserId",
                table: "PortfolioSnapshots");

            migrationBuilder.DropColumn(
                name: "SupersededAt",
                table: "PortfolioSnapshots");

            migrationBuilder.DropColumn(
                name: "SupersededBySnapshotId",
                table: "PortfolioSnapshots");
        }
    }
}
