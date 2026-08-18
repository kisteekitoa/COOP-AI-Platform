using System.Security.Cryptography;
using COOPAI.API.Data;
using COOPAI.API.Models;
using COOPAI.API.Models.Import;
using COOPAI.API.Services.Import;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OfficeOpenXml;

namespace COOPAI.API.Tests;

public class ExcelImportAtomicTransactionTests
{
    private const string Contract1 = "\u0E2A\u0E2B-2569-000001";
    private const string Contract2 = "\u0E2A\u0E2B-2569-000002";
    private const string MissingContract = "\u0E2A\u0E2B-2569-009999";

    [Fact]
    public async Task SuccessfulBatch_CommitsContractsHistoryBatchAndLogsAtomically()
    {
        await using var database = await AtomicTestDatabase.CreateAsync();
        await database.SeedContractsAsync(Contract1, Contract2);

        var result = await database.ImportAsync(
            new TestRow(Contract1, 100m, 20m, 120m, null, null, null),
            new TestRow(Contract2, null, null, null, 200m, 30m, 230m));

        Assert.True(result.Success);
        Assert.Equal(2, result.UpdatedRows);
        Assert.NotNull(result.BatchId);

        await using var verificationContext = database.CreateContext();
        var contracts = await verificationContext.LoanContracts
            .AsNoTracking()
            .OrderBy(contract => contract.ContractNo)
            .ToListAsync();
        Assert.Equal(2, contracts.Count);
        Assert.Equal((100m, 20m, 120m), BalanceTuple(contracts[0]));
        Assert.Equal((200m, 30m, 230m), BalanceTuple(contracts[1]));
        Assert.All(contracts, contract =>
        {
            Assert.Equal(777777.77m, contract.LoanAmount);
            Assert.Equal(database.MemberId, contract.MemberId);
        });
        Assert.Equal(2, await verificationContext.ImportLoanRecords.CountAsync());
        Assert.Single(await verificationContext.ImportBatches.AsNoTracking().ToListAsync());
        Assert.Equal(2, await verificationContext.ImportLogs.CountAsync());
    }

    [Fact]
    public async Task UnexpectedExceptionOnSecondRow_RollsBackFirstRowAndAllAuditWrites()
    {
        await using var database = await AtomicTestDatabase.CreateAsync();
        await database.SeedContractsAsync(Contract1, Contract2);

        var result = await database.ImportAsync(
            context => new ThrowOnSecondLoanImporter(context),
            null,
            true,
            new TestRow(Contract1, 100m, 20m, 120m, null, null, null),
            new TestRow(Contract2, 200m, 30m, 230m, null, null, null));

        Assert.False(result.Success);
        Assert.Equal(0, result.UpdatedRows);
        Assert.Contains(result.ErrorDetails, error => error.ErrorType == "ImportTransactionRolledBack");
        await AssertOriginalBusinessAndNoAuditWritesAsync(database, Contract1, Contract2);
    }

    [Fact]
    public async Task UnresolvedNegative_BlocksAllBusinessPersistenceBeforeTransaction()
    {
        await using var database = await AtomicTestDatabase.CreateAsync();
        await database.SeedContractsAsync(Contract1);

        var result = await database.ImportAsync(
            new TestRow(Contract1, 100m, 20m, 120m, null, null, null),
            new TestRow(Contract2, 100m, -1m, 99m, null, null, null));

        Assert.False(result.Success);
        Assert.Equal(1, result.FailedRows);
        Assert.Equal(1, result.UpdateCandidates);
        Assert.Equal(0, result.UpdatedRows);
        Assert.Contains(result.ErrorDetails, error => error.ErrorType == "Negative");
        await AssertOriginalBusinessAndNoAuditWritesAsync(database, Contract1);
    }

    [Fact]
    public async Task ApprovedNegative_IsSkippedWhileValidRowsCommit()
    {
        await using var database = await AtomicTestDatabase.CreateAsync();
        await database.SeedContractsAsync(Contract1, Contract2);
        var negative = new TestRow(Contract2, 100m, -1m, 99m, null, null, null);

        var result = await database.ImportAsync(
            null,
            new[] { new ApprovedErrorSkipSelection { RowNumber = 3, ContractNo = Contract2 } },
            true,
            new TestRow(Contract1, 100m, 20m, 120m, null, null, null),
            negative);

        Assert.True(result.Success);
        Assert.Equal(1, result.UpdatedRows);
        Assert.Equal(1, result.UserSkippedErrorRows);
        Assert.Equal(0, result.FailedRows);

        await using var verificationContext = database.CreateContext();
        var updated = await verificationContext.LoanContracts.AsNoTracking().SingleAsync(x => x.ContractNo == Contract1);
        var skipped = await verificationContext.LoanContracts.AsNoTracking().SingleAsync(x => x.ContractNo == Contract2);
        Assert.Equal((100m, 20m, 120m), BalanceTuple(updated));
        Assert.Equal((1m, 2m, 3m), BalanceTuple(skipped));
        Assert.Single(await verificationContext.ImportLoanRecords.AsNoTracking().ToListAsync());
        Assert.Single(await verificationContext.ImportBatches.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task UnresolvedTotalMismatch_BlocksAllBusinessPersistence()
    {
        await using var database = await AtomicTestDatabase.CreateAsync();
        await database.SeedContractsAsync(Contract1);

        var result = await database.ImportAsync(
            new TestRow(Contract1, 100m, 20m, 120m, null, null, null),
            new TestRow(Contract2, 100m, 20m, 119m, null, null, null));

        Assert.False(result.Success);
        Assert.Equal(0, result.UpdatedRows);
        Assert.Contains(result.ErrorDetails, error => error.ErrorType == "TotalBalanceMismatch");
        await AssertOriginalBusinessAndNoAuditWritesAsync(database, Contract1);
    }

    [Fact]
    public async Task ApprovedTotalMismatch_IsSkippedWhileValidRowsCommit()
    {
        await using var database = await AtomicTestDatabase.CreateAsync();
        await database.SeedContractsAsync(Contract1, Contract2);

        var result = await database.ImportAsync(
            null,
            new[] { new ApprovedErrorSkipSelection { RowNumber = 3, ContractNo = Contract2 } },
            true,
            new TestRow(Contract1, 100m, 20m, 120m, null, null, null),
            new TestRow(Contract2, 100m, 20m, 119m, null, null, null));

        Assert.True(result.Success);
        Assert.Equal(1, result.UpdatedRows);
        Assert.Equal(1, result.UserSkippedErrorRows);

        await using var verificationContext = database.CreateContext();
        Assert.Equal((100m, 20m, 120m), BalanceTuple(
            await verificationContext.LoanContracts.AsNoTracking().SingleAsync(x => x.ContractNo == Contract1)));
        Assert.Equal((1m, 2m, 3m), BalanceTuple(
            await verificationContext.LoanContracts.AsNoTracking().SingleAsync(x => x.ContractNo == Contract2)));
    }

    [Fact]
    public async Task AutomaticInactive_DoesNotBlockValidCommitOrUpdateInactiveContract()
    {
        await using var database = await AtomicTestDatabase.CreateAsync();
        await database.SeedContractsAsync(Contract1, Contract2);

        var result = await database.ImportAsync(
            new TestRow(Contract1, 100m, 20m, 120m, null, null, null),
            new TestRow(Contract2, null, null, null, null, null, 0m));

        Assert.True(result.Success);
        Assert.Equal(1, result.UpdatedRows);
        Assert.Equal(1, result.SkippedRows);

        await using var verificationContext = database.CreateContext();
        Assert.Equal((100m, 20m, 120m), BalanceTuple(
            await verificationContext.LoanContracts.AsNoTracking().SingleAsync(x => x.ContractNo == Contract1)));
        Assert.Equal((1m, 2m, 3m), BalanceTuple(
            await verificationContext.LoanContracts.AsNoTracking().SingleAsync(x => x.ContractNo == Contract2)));
        Assert.Single(await verificationContext.ImportLoanRecords.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task FileHashMismatch_ReturnsBeforeTransactionAndWritesNothing()
    {
        await using var database = await AtomicTestDatabase.CreateAsync();
        await database.SeedContractsAsync(Contract1);

        var result = await database.ImportAsync(
            null,
            new[] { new ApprovedErrorSkipSelection { RowNumber = 2, ContractNo = Contract1 } },
            false,
            new TestRow(Contract1, 100m, -1m, 99m, null, null, null));

        Assert.False(result.Success);
        Assert.Contains(result.ErrorDetails, error => error.ErrorType == "ValidationFileHashMismatch");
        await AssertOriginalBusinessAndNoAuditWritesAsync(database, Contract1);
    }

    [Fact]
    public async Task MissingContract_BlocksExistingValidRowAndWritesNothing()
    {
        await using var database = await AtomicTestDatabase.CreateAsync();
        await database.SeedContractsAsync(Contract1);

        var result = await database.ImportAsync(
            new TestRow(Contract1, 100m, 20m, 120m, null, null, null),
            new TestRow(MissingContract, 200m, 30m, 230m, null, null, null));

        Assert.False(result.Success);
        Assert.Equal(1, result.MissingContracts);
        Assert.Equal(0, result.UpdatedRows);
        Assert.Contains(result.ErrorDetails, error => error.ErrorType == "ContractNotFound");
        await AssertOriginalBusinessAndNoAuditWritesAsync(database, Contract1);
    }

    [Fact]
    public async Task CurrentActiveRegression000600_CommitsBalancesAndPreservesProtectedFields()
    {
        const string regressionContract = "\u0E2A\u0E2B-2569-000600";
        await using var database = await AtomicTestDatabase.CreateAsync();
        await database.SeedContractsAsync(regressionContract);

        var result = await database.ImportAsync(
            new TestRow(regressionContract, null, null, null, 123000m, 25830m, 148830m));

        Assert.True(result.Success);
        await using var verificationContext = database.CreateContext();
        var contract = await verificationContext.LoanContracts.AsNoTracking().SingleAsync();
        Assert.Equal((123000m, 25830m, 148830m), BalanceTuple(contract));
        Assert.Equal(777777.77m, contract.LoanAmount);
        Assert.Equal(database.MemberId, contract.MemberId);
        Assert.Equal(regressionContract, contract.ContractNo);
    }

    [Fact]
    public async Task ExceptionAfterFinalSave_RollsBackDatabaseWritesBeforeCommit()
    {
        var interceptor = new ThrowAfterSaveInterceptor();
        await using var database = await AtomicTestDatabase.CreateAsync(interceptor);
        await database.SeedContractsAsync(Contract1, Contract2);
        interceptor.Enabled = true;

        var result = await database.ImportAsync(
            new TestRow(Contract1, 100m, 20m, 120m, null, null, null),
            new TestRow(Contract2, 200m, 30m, 230m, null, null, null));

        Assert.False(result.Success);
        Assert.Equal(0, result.UpdatedRows);
        Assert.Contains(result.ErrorDetails, error => error.ErrorType == "ImportTransactionRolledBack");
        await AssertOriginalBusinessAndNoAuditWritesAsync(database, Contract1, Contract2);
    }

    private static (decimal Principal, decimal Profit, decimal Total) BalanceTuple(LoanContract contract) =>
        (contract.PrincipalBalance, contract.ProfitBalance, contract.TotalBalance);

    private static async Task AssertOriginalBusinessAndNoAuditWritesAsync(
        AtomicTestDatabase database,
        params string[] contractNos)
    {
        await using var verificationContext = database.CreateContext();
        var contracts = await verificationContext.LoanContracts
            .AsNoTracking()
            .Where(contract => contractNos.Contains(contract.ContractNo))
            .ToListAsync();
        Assert.Equal(contractNos.Length, contracts.Count);
        Assert.All(contracts, contract => Assert.Equal((1m, 2m, 3m), BalanceTuple(contract)));
        Assert.Empty(await verificationContext.ImportLoanRecords.AsNoTracking().ToListAsync());
        Assert.Empty(await verificationContext.ImportBatches.AsNoTracking().ToListAsync());
        Assert.Empty(await verificationContext.ImportLogs.AsNoTracking().ToListAsync());
    }

    private sealed class AtomicTestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<CoopDbContext> _options;

        private AtomicTestDatabase(SqliteConnection connection, DbContextOptions<CoopDbContext> options)
        {
            _connection = connection;
            _options = options;
        }

        public int MemberId { get; private set; }

        public static async Task<AtomicTestDatabase> CreateAsync(params IInterceptor[] interceptors)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var builder = new DbContextOptionsBuilder<CoopDbContext>().UseSqlite(connection);
            if (interceptors.Length > 0)
                builder.AddInterceptors(interceptors);

            var database = new AtomicTestDatabase(connection, builder.Options);
            await using var context = database.CreateContext();
            await context.Database.EnsureCreatedAsync();
            return database;
        }

        public CoopDbContext CreateContext() => new(_options);

        public async Task SeedContractsAsync(params string[] contractNos)
        {
            await using var context = CreateContext();
            var member = new Member
            {
                MemberNo = "DB-MEMBER",
                FirstName = "Database",
                LastName = "Member",
                FullName = "Database Member",
                IsActive = true
            };
            var loanType = new LoanType
            {
                Code = "TEST",
                Name = "Test Loan",
                IsActive = true
            };
            context.Members.Add(member);
            context.LoanTypes.Add(loanType);
            await context.SaveChangesAsync();
            MemberId = member.Id;

            foreach (var contractNo in contractNos)
            {
                context.LoanContracts.Add(new LoanContract
                {
                    ContractNo = contractNo,
                    MemberId = member.Id,
                    LoanTypeId = loanType.Id,
                    ContractDate = new DateTime(2020, 1, 1),
                    ExpireDate = new DateTime(2030, 1, 1),
                    LoanAmount = 777777.77m,
                    PrincipalBalance = 1m,
                    ProfitBalance = 2m,
                    TotalBalance = 3m,
                    OverdueDays = 4,
                    IsClosed = false
                });
            }

            await context.SaveChangesAsync();
        }

        public Task<ImportResult> ImportAsync(params TestRow[] rows) =>
            ImportAsync(null, null, true, rows);

        public async Task<ImportResult> ImportAsync(
            Func<CoopDbContext, LoanImporter>? importerFactory,
            IReadOnlyCollection<ApprovedErrorSkipSelection>? approvedSkips,
            bool useMatchingFileHash,
            params TestRow[] rows)
        {
            var filePath = Path.Combine(Path.GetTempPath(), $"atomic_import_{Guid.NewGuid():N}.xlsx");
            CreateWorkbook(filePath, rows);

            try
            {
                await using var context = CreateContext();
                var importer = importerFactory?.Invoke(context) ?? new LoanImporter(context);
                var service = new ExcelImportService(context, new ExcelReader(), new ImportValidator(), importer);
                ImportExecutionOptions? options = null;
                if (approvedSkips is { Count: > 0 })
                {
                    options = new ImportExecutionOptions
                    {
                        ValidationFileHash = useMatchingFileHash ? ComputeFileHash(filePath) : new string('0', 64),
                        ApprovedErrorSkips = approvedSkips
                    };
                }

                return await service.ImportAsync(filePath, options);
            }
            finally
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
        }

        public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
    }

    private sealed class ThrowOnSecondLoanImporter : LoanImporter
    {
        private int _callCount;

        public ThrowOnSecondLoanImporter(CoopDbContext dbContext)
            : base(dbContext)
        {
        }

        public override Task<LoanImportResult> ImportAsync(ImportLoanRecord record)
        {
            _callCount++;
            if (_callCount == 2)
                throw new InvalidOperationException("Injected failure on second row.");

            return base.ImportAsync(record);
        }
    }

    private sealed class ThrowAfterSaveInterceptor : SaveChangesInterceptor
    {
        public bool Enabled { get; set; }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            if (Enabled)
                throw new InvalidOperationException("Injected failure after final SaveChanges.");

            return ValueTask.FromResult(result);
        }
    }

    private static string ComputeFileHash(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void CreateWorkbook(string filePath, IReadOnlyList<TestRow> rows)
    {
        ExcelPackage.License.SetNonCommercialPersonal("COOP-AI Atomic Import Tests");
        using var package = new ExcelPackage(new FileInfo(filePath));
        var sheet = package.Workbook.Worksheets.Add("\u0E1B\u0E49\u0E2D\u0E19\u0E1B\u0E23\u0E30\u0E08\u0E33\u0E27\u0E31\u0E19");
        var headers = new[]
        {
            "MemberNo", "MemberName", "ContractNo", "ContractDate", "ExpireDate", "OverdueDays",
            "PreviousPrincipal", "PreviousProfit", "PreviousTotal", "CurrentPrincipal", "CurrentProfit", "CurrentTotal"
        };
        for (var column = 0; column < headers.Length; column++)
            sheet.Cells[1, column + 1].Value = headers[column];

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var values = new object?[]
            {
                $"M{index + 1:000}", $"Member {index + 1}", row.ContractNo, "2026-01-01", "2026-12-31", 0,
                row.G, row.H, row.I, row.J, row.K, row.L
            };
            for (var column = 0; column < values.Length; column++)
                sheet.Cells[index + 2, column + 1].Value = values[column];
        }

        package.Save();
    }

    private sealed record TestRow(
        string ContractNo,
        decimal? G,
        decimal? H,
        decimal? I,
        decimal? J,
        decimal? K,
        decimal? L);
}
