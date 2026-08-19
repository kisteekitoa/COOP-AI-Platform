using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace COOPAI.API.Migrations
{
    /// <inheritdoc />
    public partial class AddPortfolioSnapshotV11 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PortfolioSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AsOfDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    DefinitionVersion = table.Column<int>(type: "int", nullable: false),
                    CurrencyCode = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    SourceType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SourceFileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    SourceFileHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SourceFileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    SourceRetrievedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SourceDataThroughDate = table.Column<DateOnly>(type: "date", nullable: true),
                    SnapshotContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TotalSourceRows = table.Column<int>(type: "int", nullable: false),
                    TotalContractCount = table.Column<int>(type: "int", nullable: false),
                    PlaceholderRowCount = table.Column<int>(type: "int", nullable: false),
                    MatchedCanonicalCount = table.Column<int>(type: "int", nullable: false),
                    MissingCanonicalCount = table.Column<int>(type: "int", nullable: false),
                    UnresolvedMemberContractCount = table.Column<int>(type: "int", nullable: false),
                    WarningRecordCount = table.Column<int>(type: "int", nullable: false),
                    BlockingErrorCount = table.Column<int>(type: "int", nullable: false),
                    ShadowExcludedCount = table.Column<int>(type: "int", nullable: false),
                    InTermContractCount = table.Column<int>(type: "int", nullable: false),
                    ExpiredContractCount = table.Column<int>(type: "int", nullable: false),
                    OutstandingContractCount = table.Column<int>(type: "int", nullable: false),
                    PaidOffContractCount = table.Column<int>(type: "int", nullable: false),
                    InTermOutstandingContractCount = table.Column<int>(type: "int", nullable: false),
                    InTermPaidOffContractCount = table.Column<int>(type: "int", nullable: false),
                    ExpiredOutstandingContractCount = table.Column<int>(type: "int", nullable: false),
                    ExpiredPaidOffContractCount = table.Column<int>(type: "int", nullable: false),
                    ExpiredOutstandingTotal = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    PrincipalOpening = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    ProfitOpening = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    TotalOpening = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    PrincipalRepayment = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    ProfitRepayment = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    TotalRepayment = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    PrincipalOutstanding = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    ProfitOutstanding = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    TotalOutstanding = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    PrincipalDifference = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    ProfitDifference = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    TotalDifference = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    ComponentDifference = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ValidatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortfolioSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PortfolioSnapshotExclusions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PortfolioSnapshotId = table.Column<int>(type: "int", nullable: false),
                    LoanContractId = table.Column<int>(type: "int", nullable: false),
                    ReasonCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortfolioSnapshotExclusions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PortfolioSnapshotExclusions_LoanContracts_LoanContractId",
                        column: x => x.LoanContractId,
                        principalTable: "LoanContracts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PortfolioSnapshotExclusions_PortfolioSnapshots_PortfolioSnapshotId",
                        column: x => x.PortfolioSnapshotId,
                        principalTable: "PortfolioSnapshots",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PortfolioSnapshotRecords",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PortfolioSnapshotId = table.Column<int>(type: "int", nullable: false),
                    SourceRecordKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    NormalizedContractNo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ContractDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ExpireDate = table.Column<DateOnly>(type: "date", nullable: true),
                    LoanContractId = table.Column<int>(type: "int", nullable: true),
                    MemberId = table.Column<int>(type: "int", nullable: true),
                    SourceRowKind = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    OpeningSide = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    TermStatus = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    BalanceStatus = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CanonicalMatchStatus = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    MemberMatchStatus = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    LoanTypePrefix = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    InclusionStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    WarningCodesJson = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    PrincipalOpening = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    ProfitOpening = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    TotalOpening = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    PrincipalRepayment = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    ProfitRepayment = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    TotalRepayment = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    PrincipalOutstanding = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    ProfitOutstanding = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    TotalOutstanding = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortfolioSnapshotRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PortfolioSnapshotRecords_LoanContracts_LoanContractId",
                        column: x => x.LoanContractId,
                        principalTable: "LoanContracts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PortfolioSnapshotRecords_Members_MemberId",
                        column: x => x.MemberId,
                        principalTable: "Members",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PortfolioSnapshotRecords_PortfolioSnapshots_PortfolioSnapshotId",
                        column: x => x.PortfolioSnapshotId,
                        principalTable: "PortfolioSnapshots",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioSnapshotExclusions_LoanContractId",
                table: "PortfolioSnapshotExclusions",
                column: "LoanContractId");

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioSnapshotExclusions_PortfolioSnapshotId_LoanContractId_ReasonCode",
                table: "PortfolioSnapshotExclusions",
                columns: new[] { "PortfolioSnapshotId", "LoanContractId", "ReasonCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioSnapshotRecords_LoanContractId_PortfolioSnapshotId",
                table: "PortfolioSnapshotRecords",
                columns: new[] { "LoanContractId", "PortfolioSnapshotId" });

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioSnapshotRecords_MemberId",
                table: "PortfolioSnapshotRecords",
                column: "MemberId");

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioSnapshotRecords_PortfolioSnapshotId_LoanTypePrefix",
                table: "PortfolioSnapshotRecords",
                columns: new[] { "PortfolioSnapshotId", "LoanTypePrefix" });

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioSnapshotRecords_PortfolioSnapshotId_MemberId",
                table: "PortfolioSnapshotRecords",
                columns: new[] { "PortfolioSnapshotId", "MemberId" });

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioSnapshotRecords_PortfolioSnapshotId_NormalizedContractNo",
                table: "PortfolioSnapshotRecords",
                columns: new[] { "PortfolioSnapshotId", "NormalizedContractNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioSnapshotRecords_PortfolioSnapshotId_TermStatus_BalanceStatus",
                table: "PortfolioSnapshotRecords",
                columns: new[] { "PortfolioSnapshotId", "TermStatus", "BalanceStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioSnapshots_AsOfDate_Revision",
                table: "PortfolioSnapshots",
                columns: new[] { "AsOfDate", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioSnapshots_SourceType_SourceFileHash",
                table: "PortfolioSnapshots",
                columns: new[] { "SourceType", "SourceFileHash" });

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioSnapshots_Status_AsOfDate",
                table: "PortfolioSnapshots",
                columns: new[] { "Status", "AsOfDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PortfolioSnapshotExclusions");

            migrationBuilder.DropTable(
                name: "PortfolioSnapshotRecords");

            migrationBuilder.DropTable(
                name: "PortfolioSnapshots");
        }
    }
}
