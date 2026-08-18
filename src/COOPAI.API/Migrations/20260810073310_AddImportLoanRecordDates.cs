using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace COOPAI.API.Migrations
{
    /// <inheritdoc />
    public partial class AddImportLoanRecordDates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ContractDate",
                table: "ImportLoanRecords",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpireDate",
                table: "ImportLoanRecords",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContractDate",
                table: "ImportLoanRecords");

            migrationBuilder.DropColumn(
                name: "ExpireDate",
                table: "ImportLoanRecords");
        }
    }
}
