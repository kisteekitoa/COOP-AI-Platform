using System.Data.Common;
using System.Text.Json;
using COOPAI.API.Controllers;
using COOPAI.API.Data;
using COOPAI.API.Models;
using COOPAI.API.Models.Portfolio;
using COOPAI.API.Services.PortfolioSnapshots;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace COOPAI.API.Tests;

public sealed class PortfolioSnapshotWorkflowServiceTests
{
    [Theory]
    [InlineData(PortfolioSnapshotPersistenceStage.HeaderTracked)]
    [InlineData(PortfolioSnapshotPersistenceStage.RecordsTracked)]
    [InlineData(PortfolioSnapshotPersistenceStage.ExclusionsTracked)]
    [InlineData(PortfolioSnapshotPersistenceStage.BeforeSave)]
    public async Task CreateDraft_HookFailureAtEveryStage_RollsBackEntireDraft(
        PortfolioSnapshotPersistenceStage failureStage)
    {
        await using var database = await WorkflowDatabase.CreateAsync();
        await database.SeedProtectedDataAsync();
        await using var context = database.CreateContext();
        var source = new MutableSource();
        var service = new PortfolioSnapshotWorkflowService(
            context,
            source,
            [new ThrowingPersistenceHook(failureStage)]);
        var validation = await service.ValidateAsync("file-a.xlsx", "Loan.xlsx", SourceDate);
        var protectedBefore = await ProtectedCountsAsync(context);

        await Assert.ThrowsAsync<InjectedPersistenceException>(() => service.CreateDraftAsync(
            "file-a.xlsx",
            "Loan.xlsx",
            SourceDate,
            validation.SourceFileHash,
            validation.SnapshotContentHash));

        await AssertNoPartialSnapshotAsync(database, protectedBefore);
    }

    [Theory]
    [InlineData("PortfolioSnapshots")]
    [InlineData("PortfolioSnapshotRecords")]
    [InlineData("PortfolioSnapshotExclusions")]
    public async Task CreateDraft_DatabaseInsertFailure_RollsBackHeaderRecordsAndExclusions(string tableName)
    {
        var interceptor = new ThrowOnInsertInterceptor(tableName);
        await using var database = await WorkflowDatabase.CreateAsync(interceptor);
        await database.SeedProtectedDataAsync();
        await using var context = database.CreateContext();
        var source = new MutableSource();
        var service = new PortfolioSnapshotWorkflowService(context, source);
        var validation = await service.ValidateAsync("file-a.xlsx", "Loan.xlsx", SourceDate);
        var protectedBefore = await ProtectedCountsAsync(context);
        interceptor.Enabled = true;

        await Assert.ThrowsAnyAsync<Exception>(() => service.CreateDraftAsync(
            "file-a.xlsx",
            "Loan.xlsx",
            SourceDate,
            validation.SourceFileHash,
            validation.SnapshotContentHash));
        interceptor.Enabled = false;

        await AssertNoPartialSnapshotAsync(database, protectedBefore);
    }

    [Fact]
    public async Task ValidateAsync_IsReadOnlyAndReturnsRequiredReconciliations()
    {
        await using var database = await WorkflowDatabase.CreateAsync();
        await database.SeedProtectedDataAsync();
        await using var context = database.CreateContext();
        var service = new PortfolioSnapshotWorkflowService(context, new MutableSource());
        var protectedBefore = await ProtectedCountsAsync(context);

        var result = await service.ValidateAsync("file-a.xlsx", "Loan.xlsx", SourceDate);

        Assert.True(result.IsValid);
        Assert.True(result.Quality.Reconciled);
        Assert.Equal(2, result.Counts.SourceRows);
        Assert.Equal(1, result.Counts.TotalContracts);
        Assert.Equal(1, result.Counts.PlaceholderRows);
        Assert.Equal(1, result.Counts.InTermOutstandingContracts);
        Assert.Equal(100m, result.Financial.TotalOutstanding);
        Assert.Empty(context.ChangeTracker.Entries().Where(x => x.State != EntityState.Unchanged));
        await AssertNoPartialSnapshotAsync(database, protectedBefore);
    }

    [Fact]
    public async Task CreateDraft_IsIdempotentForDraftAndValidatedIdentity()
    {
        await using var database = await WorkflowDatabase.CreateAsync();
        await database.SeedProtectedDataAsync();
        await using var context = database.CreateContext();
        var service = new PortfolioSnapshotWorkflowService(context, new MutableSource());
        var validation = await service.ValidateAsync("file-a.xlsx", "Loan.xlsx", SourceDate);

        var first = await service.CreateDraftAsync(
            "file-a.xlsx", "Loan.xlsx", SourceDate,
            validation.SourceFileHash, validation.SnapshotContentHash);
        var second = await service.CreateDraftAsync(
            "file-a.xlsx", "Loan.xlsx", SourceDate,
            validation.SourceFileHash, validation.SnapshotContentHash);
        await service.ValidateDraftAsync(first.Id);
        var third = await service.CreateDraftAsync(
            "file-a.xlsx", "Loan.xlsx", SourceDate,
            validation.SourceFileHash, validation.SnapshotContentHash);

        Assert.False(first.WasExisting);
        Assert.True(second.WasExisting);
        Assert.True(third.WasExisting);
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.Id, third.Id);
        Assert.Equal("Validated", third.Status);
        Assert.Equal(1, await context.PortfolioSnapshots.CountAsync());
    }

    [Fact]
    public async Task CreateDraft_ExactRejectedIdentity_RequiresExplicitReprocess()
    {
        await using var database = await WorkflowDatabase.CreateAsync();
        await database.SeedProtectedDataAsync();
        await using var context = database.CreateContext();
        var service = new PortfolioSnapshotWorkflowService(context, new MutableSource());
        var validation = await service.ValidateAsync("file-a.xlsx", "Loan.xlsx", SourceDate);
        var draft = await service.CreateDraftAsync(
            "file-a.xlsx", "Loan.xlsx", SourceDate,
            validation.SourceFileHash, validation.SnapshotContentHash);
        await service.RejectAsync(draft.Id, "ยอดคงเหลือต้องตรวจสอบเพิ่มเติม");

        var exception = await Assert.ThrowsAsync<PortfolioSnapshotWorkflowException>(() =>
            service.CreateDraftAsync(
                "file-a.xlsx", "Loan.xlsx", SourceDate,
                validation.SourceFileHash, validation.SnapshotContentHash));

        Assert.Equal("RejectedSnapshotRequiresExplicitReprocess", exception.Code);
        Assert.Equal(1, await context.PortfolioSnapshots.CountAsync());
    }

    [Fact]
    public async Task CreateDraft_ChangedFileOrWrongHashesOrAsOfDate_PersistsNothing()
    {
        await using var database = await WorkflowDatabase.CreateAsync();
        await database.SeedProtectedDataAsync();
        await using var context = database.CreateContext();
        var source = new MutableSource();
        var service = new PortfolioSnapshotWorkflowService(context, source);
        var validation = await service.ValidateAsync("file-a.xlsx", "Loan.xlsx", SourceDate);

        source.SourceFileHash = new string('B', 64);
        var changedFile = await Assert.ThrowsAsync<PortfolioSnapshotWorkflowException>(() =>
            service.CreateDraftAsync(
                "file-b.xlsx", "Loan.xlsx", SourceDate,
                validation.SourceFileHash, validation.SnapshotContentHash));
        source.SourceFileHash = new string('A', 64);
        var wrongContent = await Assert.ThrowsAsync<PortfolioSnapshotWorkflowException>(() =>
            service.CreateDraftAsync(
                "file-a.xlsx", "Loan.xlsx", SourceDate,
                validation.SourceFileHash, new string('0', 64)));
        var wrongAsOf = await Assert.ThrowsAsync<PortfolioSnapshotWorkflowException>(() =>
            service.CreateDraftAsync(
                "file-a.xlsx", "Loan.xlsx", SourceDate.AddDays(1),
                validation.SourceFileHash, validation.SnapshotContentHash));

        Assert.Equal("SourceFileHashMismatch", changedFile.Code);
        Assert.Equal("SnapshotContentHashMismatch", wrongContent.Code);
        Assert.Equal("SnapshotContentHashMismatch", wrongAsOf.Code);
        Assert.Empty(await context.PortfolioSnapshots.ToListAsync());
    }

    [Fact]
    public async Task Lifecycle_AllowsApprovedTransitionsAndRejectsInvalidTransitions()
    {
        await using var database = await WorkflowDatabase.CreateAsync();
        await database.SeedProtectedDataAsync();
        await using var context = database.CreateContext();
        var service = new PortfolioSnapshotWorkflowService(context, new MutableSource());
        var firstValidation = await service.ValidateAsync("file-a.xlsx", "Loan.xlsx", SourceDate);
        var draft = await service.CreateDraftAsync(
            "file-a.xlsx", "Loan.xlsx", SourceDate,
            firstValidation.SourceFileHash, firstValidation.SnapshotContentHash);

        var validated = await service.ValidateDraftAsync(draft.Id);
        var rejected = await service.RejectAsync(validated.Id, "ผู้ตรวจทานขอให้ตรวจสอบเอกสารต้นทาง");
        var invalidValidate = await Assert.ThrowsAsync<PortfolioSnapshotWorkflowException>(() =>
            service.ValidateDraftAsync(rejected.Id));
        var invalidReject = await Assert.ThrowsAsync<PortfolioSnapshotWorkflowException>(() =>
            service.RejectAsync(rejected.Id, "ปฏิเสธซ้ำไม่ได้"));

        Assert.Equal("Validated", validated.Status);
        Assert.Equal("Rejected", rejected.Status);
        Assert.Equal("InvalidSnapshotTransition", invalidValidate.Code);
        Assert.Equal("InvalidSnapshotTransition", invalidReject.Code);
        var persisted = await context.PortfolioSnapshots
            .Include(x => x.Records)
            .SingleAsync(x => x.Id == draft.Id);
        Assert.NotEmpty(persisted.Records);
        Assert.NotNull(persisted.RejectedAt);
    }

    [Fact]
    public async Task Lifecycle_AllowsDraftToRejectedWithoutDeletingRecords()
    {
        await using var database = await WorkflowDatabase.CreateAsync();
        await database.SeedProtectedDataAsync();
        await using var context = database.CreateContext();
        var service = new PortfolioSnapshotWorkflowService(context, new MutableSource());
        var validation = await service.ValidateAsync("file-a.xlsx", "Loan.xlsx", SourceDate);
        var draft = await service.CreateDraftAsync(
            "file-a.xlsx", "Loan.xlsx", SourceDate,
            validation.SourceFileHash, validation.SnapshotContentHash);

        var rejected = await service.RejectAsync(draft.Id, "ข้อมูลต้องผ่านการทบทวนเพิ่มเติม");

        Assert.Equal("Rejected", rejected.Status);
        Assert.Equal(1, await context.PortfolioSnapshotRecords.CountAsync());
        Assert.Equal(1, await context.PortfolioSnapshotExclusions.CountAsync());
    }

    [Fact]
    public async Task ValidateDraft_DetectsPersistedHeaderOrContentTampering()
    {
        await using var database = await WorkflowDatabase.CreateAsync();
        await database.SeedProtectedDataAsync();
        await using var context = database.CreateContext();
        var service = new PortfolioSnapshotWorkflowService(context, new MutableSource());
        var validation = await service.ValidateAsync("file-a.xlsx", "Loan.xlsx", SourceDate);
        var draft = await service.CreateDraftAsync(
            "file-a.xlsx", "Loan.xlsx", SourceDate,
            validation.SourceFileHash, validation.SnapshotContentHash);
        var snapshot = await context.PortfolioSnapshots.SingleAsync(x => x.Id == draft.Id);
        snapshot.TotalOutstanding += 1m;
        await context.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<PortfolioSnapshotWorkflowException>(() =>
            service.ValidateDraftAsync(draft.Id));

        Assert.Equal("PersistedSnapshotValidationFailed", exception.Code);
        Assert.Equal(PortfolioSnapshotStatus.Draft, snapshot.Status);
    }

    [Fact]
    public async Task ReviewAndRecordFilters_ExposeWarningsWithoutPiiOrShadowText()
    {
        await using var database = await WorkflowDatabase.CreateAsync();
        await database.SeedProtectedDataAsync();
        await using var context = database.CreateContext();
        var source = new MutableSource { IncludeUnresolvedWarning = true };
        var service = new PortfolioSnapshotWorkflowService(context, source);
        var validation = await service.ValidateAsync("file-a.xlsx", "Loan.xlsx", SourceDate);
        var draft = await service.CreateDraftAsync(
            "file-a.xlsx", "Loan.xlsx", SourceDate,
            validation.SourceFileHash, validation.SnapshotContentHash);

        var review = await service.GetReviewAsync(draft.Id);
        var filtered = await service.GetRecordsAsync(
            draft.Id, 1, 10, null, "PaidOff", "IncludedWithWarning",
            PortfolioSnapshotCodes.Negative, "Missing", "Missing", null);
        var secondPage = await service.GetRecordsAsync(
            draft.Id, 2, 1, null, null, null, null, null, null, null);
        var serialized = JsonSerializer.Serialize(review);

        Assert.NotNull(review);
        Assert.False(review.PublishingEnabled);
        Assert.Equal(2, review.Counts.TotalContracts);
        Assert.Equal(100m, review.Financial.TotalOutstanding);
        Assert.Contains(review.WarningSummary, x => x.Code == PortfolioSnapshotCodes.Negative);
        Assert.Contains(review.WarningSummary, x => x.Code == PortfolioSnapshotCodes.TotalBalanceMismatch);
        Assert.Single(review.UnresolvedMembers);
        Assert.True(review.UnresolvedMembers[0].MemberIdAbsent);
        Assert.Single(review.Exclusions);
        Assert.Equal(PortfolioSnapshotCodes.MalformedShadowContractNo, review.Exclusions[0].ReasonCode);
        Assert.DoesNotContain("Protected Full Name", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("Malformed Person Shadow", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("MemberName", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(filtered);
        Assert.Single(filtered.Items);
        Assert.Equal("PaidOff", filtered.Items[0].BalanceStatus);
        Assert.Contains(PortfolioSnapshotCodes.Negative, filtered.Items[0].WarningCodes);
        Assert.NotNull(secondPage);
        Assert.Equal(2, secondPage.TotalCount);
        Assert.Single(secondPage.Items);
        Assert.Equal(2, secondPage.Page);
    }

    [Fact]
    public async Task List_ReturnsDraftValidatedRejectedOnly()
    {
        await using var database = await WorkflowDatabase.CreateAsync();
        await database.SeedProtectedDataAsync();
        await using var context = database.CreateContext();
        context.PortfolioSnapshots.AddRange(
            MinimalSnapshot(PortfolioSnapshotStatus.Draft, "A"),
            MinimalSnapshot(PortfolioSnapshotStatus.Validated, "B"),
            MinimalSnapshot(PortfolioSnapshotStatus.Rejected, "C"),
            MinimalSnapshot(PortfolioSnapshotStatus.Published, "D"));
        await context.SaveChangesAsync();
        var service = new PortfolioSnapshotWorkflowService(context, new MutableSource());

        var result = await service.ListAsync();

        Assert.Equal(3, result.Count);
        Assert.DoesNotContain(result, x => x.Status == "Published");
    }

    [Fact]
    public void PublishEndpoint_IsHardDisabledAndPerformsNoServiceCall()
    {
        var controller = new PortfolioSnapshotsController(null!);

        var result = controller.Publish(123);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(409, conflict.StatusCode);
        Assert.Contains("disabled", JsonSerializer.Serialize(conflict.Value), StringComparison.OrdinalIgnoreCase);
    }

    private static readonly DateOnly SourceDate = new(2026, 6, 30);

    private static PortfolioSnapshot MinimalSnapshot(PortfolioSnapshotStatus status, string seed) => new()
    {
        AsOfDate = SourceDate.AddDays(seed[0] - 'A'),
        Revision = 1,
        Status = status,
        DefinitionVersion = 2,
        SourceFileName = $"{seed}.xlsx",
        SourceFileHash = new string(seed[0], 64),
        SnapshotContentHash = new string(seed[0], 64)
    };

    private static async Task<ProtectedCounts> ProtectedCountsAsync(CoopDbContext context) => new(
        await context.LoanContracts.CountAsync(),
        await context.Members.CountAsync(),
        await context.ImportBatches.CountAsync(),
        await context.ImportLogs.CountAsync(),
        await context.ImportLoanRecords.CountAsync());

    private static async Task AssertNoPartialSnapshotAsync(
        WorkflowDatabase database,
        ProtectedCounts protectedBefore)
    {
        await using var verification = database.CreateContext();
        Assert.Empty(await verification.PortfolioSnapshots.ToListAsync());
        Assert.Empty(await verification.PortfolioSnapshotRecords.ToListAsync());
        Assert.Empty(await verification.PortfolioSnapshotExclusions.ToListAsync());
        Assert.Equal(protectedBefore, await ProtectedCountsAsync(verification));
    }

    private sealed record ProtectedCounts(
        int LoanContracts,
        int Members,
        int ImportBatches,
        int ImportLogs,
        int ImportLoanRecords);

    private sealed class MutableSource : IPortfolioSnapshotSource
    {
        public string SourceFileHash { get; set; } = new('A', 64);
        public bool IncludeUnresolvedWarning { get; set; }

        public Task<PortfolioSourceReadResult> ReadAsync(
            string controlledCopyPath,
            DateOnly asOfDate,
            CancellationToken cancellationToken = default)
        {
            var rows = new List<PortfolioSourceRow>
            {
                ContractRow(
                    7,
                    "M001",
                    "\u0E2A\u0E21-2569-000001",
                    asOfDate,
                    new PortfolioFinancialValues(100m, 20m, 120m),
                    new PortfolioFinancialValues(20m, 0m, 20m),
                    new PortfolioFinancialValues(80m, 20m, 100m),
                    IncludeUnresolvedWarning ? new PortfolioFinancialValues(100m, 20m, 121m) : null),
                new()
                {
                    SourceRowNumber = 9,
                    SourceRecordKey = "row:9",
                    ContractNo = "\u0E2A\u0E2B-2569-000002",
                    SourceRowKind = PortfolioSourceRowKind.TemplatePlaceholder
                }
            };
            if (IncludeUnresolvedWarning)
            {
                rows.Insert(1, ContractRow(
                    8,
                    "M002",
                    "\u0E2A\u0E08-2568-000002",
                    asOfDate.AddDays(-1),
                    new PortfolioFinancialValues(-10m, 0m, -10m),
                    new PortfolioFinancialValues(-10m, 0m, -10m),
                    new PortfolioFinancialValues(0m, 0m, 0m)));
            }

            var contracts = rows.Where(x => x.SourceRowKind == PortfolioSourceRowKind.Contract).ToArray();
            var summary = new PortfolioReportSummary(
                Sum(contracts, x => x.Opening),
                Sum(contracts, x => x.Repayment),
                Sum(contracts, x => x.Outstanding));
            return Task.FromResult(new PortfolioSourceReadResult
            {
                AsOfDate = asOfDate,
                SourceFileName = Path.GetFileName(controlledCopyPath),
                SourceFileHash = SourceFileHash,
                SourceFileSizeBytes = 123,
                SourceRetrievedAt = DateTime.UtcNow,
                SourceDataThroughDate = asOfDate,
                Rows = rows,
                ReportSummary = summary
            });
        }

        private static PortfolioSourceRow ContractRow(
            int rowNumber,
            string memberNo,
            string contractNo,
            DateOnly expireDate,
            PortfolioFinancialValues opening,
            PortfolioFinancialValues repayment,
            PortfolioFinancialValues outstanding,
            PortfolioFinancialValues? displayedOpening = null) => new()
        {
            SourceRowNumber = rowNumber,
            SourceRecordKey = $"row:{rowNumber}",
            MemberNo = memberNo,
            ContractNo = contractNo,
            ContractDate = new DateOnly(2025, 1, 1),
            ExpireDate = expireDate,
            SourceRowKind = PortfolioSourceRowKind.Contract,
            OpeningSide = PortfolioOpeningSide.Previous,
            HasValidOpeningValues = true,
            HasValidRepaymentValues = true,
            HasValidOutstandingValues = true,
            Opening = opening,
            Repayment = repayment,
            Outstanding = outstanding,
            DisplayedOpening = displayedOpening ?? opening
        };

        private static PortfolioFinancialValues Sum(
            IEnumerable<PortfolioSourceRow> rows,
            Func<PortfolioSourceRow, PortfolioFinancialValues> selector) => new(
            rows.Sum(x => selector(x).Principal),
            rows.Sum(x => selector(x).Profit),
            rows.Sum(x => selector(x).Total));
    }

    private sealed class WorkflowDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<CoopDbContext> _options;

        private WorkflowDatabase(SqliteConnection connection, DbContextOptions<CoopDbContext> options)
        {
            _connection = connection;
            _options = options;
        }

        public static async Task<WorkflowDatabase> CreateAsync(params IInterceptor[] interceptors)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var builder = new DbContextOptionsBuilder<CoopDbContext>().UseSqlite(connection);
            if (interceptors.Length > 0)
                builder.AddInterceptors(interceptors);
            var database = new WorkflowDatabase(connection, builder.Options);
            await using var context = database.CreateContext();
            await context.Database.EnsureCreatedAsync();
            return database;
        }

        public CoopDbContext CreateContext() => new(_options);

        public async Task SeedProtectedDataAsync()
        {
            await using var context = CreateContext();
            var member = new Member
            {
                MemberNo = "M001",
                FirstName = "Protected",
                LastName = "Name",
                FullName = "Protected Full Name"
            };
            var loanType = new LoanType { Code = "TEST", Name = "Test", IsActive = true };
            context.Members.Add(member);
            context.LoanTypes.Add(loanType);
            context.ImportBatches.Add(new ImportBatch { FileName = "protected.xlsx", Status = "Completed" });
            await context.SaveChangesAsync();
            context.LoanContracts.AddRange(
                new LoanContract
                {
                    ContractNo = "\u0E2A\u0E21-2569-000001",
                    ContractDate = new DateTime(2025, 1, 1),
                    MemberId = member.Id,
                    LoanTypeId = loanType.Id
                },
                new LoanContract
                {
                    ContractNo = "Malformed Person Shadow",
                    ContractDate = new DateTime(2025, 1, 1),
                    MemberId = member.Id,
                    LoanTypeId = loanType.Id
                });
            await context.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
    }

    private sealed class ThrowingPersistenceHook(PortfolioSnapshotPersistenceStage failureStage)
        : IPortfolioSnapshotPersistenceHook
    {
        public Task OnStageAsync(
            PortfolioSnapshotPersistenceStage stage,
            PortfolioSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            if (stage == failureStage)
                throw new InjectedPersistenceException(stage.ToString());
            return Task.CompletedTask;
        }
    }

    private sealed class InjectedPersistenceException(string message) : Exception(message);

    private sealed class ThrowOnInsertInterceptor(string tableName) : DbCommandInterceptor
    {
        public bool Enabled { get; set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (Enabled && command.CommandText.Contains($"INSERT INTO \"{tableName}\"", StringComparison.Ordinal))
                throw new InjectedPersistenceException(tableName);
            return ValueTask.FromResult(result);
        }
    }
}
