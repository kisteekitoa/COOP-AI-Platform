using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace COOPAI.API.Migrations
{
    /// <inheritdoc />
    public partial class AddLoanContractFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "FullName",
                table: "Members",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(250)",
                oldMaxLength: 250);

            migrationBuilder.AddColumn<int>(
                name: "AssignedOfficerId",
                table: "LoanContracts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InstallmentCount",
                table: "LoanContracts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastFollowUpDate",
                table: "LoanContracts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastFollowUpRemark",
                table: "LoanContracts",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastPaymentDate",
                table: "LoanContracts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PaidInstallmentCount",
                table: "LoanContracts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ImportLoanRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowNo = table.Column<int>(type: "int", nullable: false),
                    MemberNo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    MemberName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ContractNo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    LoanAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PrincipalBalance = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ProfitBalance = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalBalance = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    OverdueDays = table.Column<int>(type: "int", nullable: false),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ImportedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportLoanRecords", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Members_MemberNo",
                table: "Members",
                column: "MemberNo");

            migrationBuilder.CreateIndex(
                name: "IX_LoanContracts_ContractNo",
                table: "LoanContracts",
                column: "ContractNo");

            migrationBuilder.CreateIndex(
                name: "IX_ImportLoanRecords_BatchId",
                table: "ImportLoanRecords",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportLoanRecords_ContractNo",
                table: "ImportLoanRecords",
                column: "ContractNo");

            migrationBuilder.CreateIndex(
                name: "IX_ImportLoanRecords_MemberNo",
                table: "ImportLoanRecords",
                column: "MemberNo");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImportLoanRecords");

            migrationBuilder.DropIndex(
                name: "IX_Members_MemberNo",
                table: "Members");

            migrationBuilder.DropIndex(
                name: "IX_LoanContracts_ContractNo",
                table: "LoanContracts");

            migrationBuilder.DropColumn(
                name: "AssignedOfficerId",
                table: "LoanContracts");

            migrationBuilder.DropColumn(
                name: "InstallmentCount",
                table: "LoanContracts");

            migrationBuilder.DropColumn(
                name: "LastFollowUpDate",
                table: "LoanContracts");

            migrationBuilder.DropColumn(
                name: "LastFollowUpRemark",
                table: "LoanContracts");

            migrationBuilder.DropColumn(
                name: "LastPaymentDate",
                table: "LoanContracts");

            migrationBuilder.DropColumn(
                name: "PaidInstallmentCount",
                table: "LoanContracts");

            migrationBuilder.AlterColumn<string>(
                name: "FullName",
                table: "Members",
                type: "nvarchar(250)",
                maxLength: 250,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200);
        }
    }
}
