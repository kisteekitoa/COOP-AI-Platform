using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using COOPAI.API.Services.DebtSegmentation;
using OfficeOpenXml;
using Xunit.Abstractions;

namespace COOPAI.API.Tests;

public sealed class DebtSegmentationJulyAcceptanceTests
{
    private static readonly DateOnly June = new(2026, 6, 1);
    private static readonly DateOnly July = new(2026, 7, 1);
    private readonly ITestOutputHelper _output;

    public DebtSegmentationJulyAcceptanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void NewJulyWorkbook_ProducesSanitizedOwnerAcceptanceEvidence()
    {
        var workspace = FindWorkspaceRoot();
        var julyPath = Environment.GetEnvironmentVariable("COOPAI_DEBT_JULY_WORKBOOK") ??
            (workspace is null ? string.Empty : Path.Combine(workspace, "data", "preview", "Loan-July-2569.xlsx"));
        var junePath = Environment.GetEnvironmentVariable("COOPAI_DEBT_JUNE_WORKBOOK") ??
            (workspace is null ? string.Empty : Path.Combine(workspace, "Loan.xlsx"));
        Assert.True(File.Exists(julyPath), "The newer July workbook is required.");
        Assert.True(File.Exists(junePath), "The preserved June workbook is required for comparison.");

        var reader = new OperationalDebtWorkbookReader();
        var julySource = reader.Read(julyPath);
        var juneSource = reader.Read(junePath, allowLegacyDiagnostic: true);
        var analyzer = new DebtSegmentationAnalyzer();
        var analysis = analyzer.Analyze(julySource.Contracts, June, July);

        ExcelPackage.License.SetNonCommercialPersonal("COOP-AI July acceptance");
        int worksheetRows;
        string[] sheetNames;
        bool julyColumnsPresent;
        bool dcGroupColumnPresent;
        using (var package = new ExcelPackage(new FileInfo(julyPath)))
        {
            sheetNames = package.Workbook.Worksheets.Select(x => x.Name).ToArray();
            var sheet = package.Workbook.Worksheets[OperationalDebtWorkbookReader.CurrentWorksheetName];
            Assert.NotNull(sheet);
            worksheetRows = sheet.Dimension?.End.Row ?? 0;
            julyColumnsPresent = sheet.Cells[3, 56].Text.Contains("กรกฎาคม", StringComparison.Ordinal) &&
                                 sheet.Cells[6, 57].Text.Contains("รับชำระ", StringComparison.Ordinal) &&
                                 sheet.Cells[6, 59].Text.Contains("รวม", StringComparison.Ordinal) &&
                                 sheet.Cells[6, 60].Text.Contains("ลูกหนี้", StringComparison.Ordinal) &&
                                 sheet.Cells[6, 62].Text.Contains("คงเหลือ", StringComparison.Ordinal);
            dcGroupColumnPresent = sheet.Cells[3, 107].Text.Contains("กลุ่ม", StringComparison.Ordinal);
        }

        var bucketSummaries = analysis.BucketSummaries.Select(bucket => new
        {
            Bucket = DebtLabels.Bucket(bucket.Bucket),
            bucket.MemberCount,
            bucket.ContractCount,
            bucket.OutstandingAmount,
            PaidInJuly = bucket.PaidContractCount,
            NotPaidInJuly = bucket.NotPaidContractCount,
            NotDueInJuly = bucket.NotDueContractCount
        }).ToArray();
        var paymentMovements = analysis.PaymentMovements.Select(MapMovement).ToArray();
        var bucketMovements = analysis.BucketMovements.Select(MapMovement).ToArray();
        var selectedBuckets = new[]
        {
            DebtBucket.Normal,
            DebtBucket.OneMonthDelinquent,
            DebtBucket.TwoMonthsDelinquent,
            DebtBucket.ThreeToSixMonthsDelinquent,
            DebtBucket.SevenToTwelveMonthsDelinquent,
            DebtBucket.OverOneToThreeYears,
            DebtBucket.OverThreeToFiveYears,
            DebtBucket.OverFiveToTenYears,
            DebtBucket.TenYearsAndAbove,
            DebtBucket.PaidOff
        };
        var samples = selectedBuckets.Select(bucket =>
        {
            var sample = analysis.Contracts
                .Where(x => x.CurrentClassification.Bucket == bucket)
                .OrderByDescending(x => x.CurrentClassification.LatestPaymentStatus == MonthlyPaymentStatus.Paid)
                .ThenBy(x => x.Contract.SourceRowNumber)
                .First();
            return new
            {
                Reference = SanitizeReference(sample.Contract.MemberCode, sample.Contract.ContractNumber),
                sample.Contract.ExpireDate,
                PaymentSequence = sample.Contract.MonthlyStates
                    .Where(x => x.Key <= July)
                    .OrderBy(x => x.Key)
                    .Select(x => $"{x.Key:yyyy-MM}:{DebtLabels.Payment(analyzer.Classify(sample.Contract, x.Key).LatestPaymentStatus)}")
                    .ToArray(),
                JulyPaymentEvent = DebtLabels.Payment(sample.CurrentClassification.LatestPaymentStatus),
                Bucket = DebtLabels.Bucket(sample.CurrentClassification.Bucket),
                Balance = sample.CurrentState.TotalOutstanding
            };
        }).ToArray();

        var evidence = new
        {
            Source = new
            {
                WorkbookPath = julyPath,
                Sha256 = HashFile(julyPath),
                SheetNames = sheetNames,
                SelectedSheet = julySource.WorksheetName,
                WorksheetRowCount = worksheetRows,
                ActiveContractCount = analysis.Contracts.Count,
                DistinctMemberCount = analysis.Contracts.Select(x => x.Contract.MemberKey)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                OutstandingTotal = analysis.Contracts.Sum(x => x.CurrentState.TotalOutstanding),
                JulyPaymentColumnsPresent = julyColumnsPresent,
                DcGroupColumnPresent = dcGroupColumnPresent,
                UsableDcGroupCodeCount = julySource.Contracts.Count(x => !string.IsNullOrWhiteSpace(x.GroupCode)),
                julySource.DataThroughPeriod
            },
            PreservedJune = new
            {
                WorkbookPath = junePath,
                Sha256 = HashFile(junePath),
                SheetNames = new[] { juneSource.WorksheetName },
                ActiveContractCount = juneSource.Contracts.Count,
                DistinctMemberCount = juneSource.Contracts.Select(x => x.MemberKey)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                OutstandingTotal = juneSource.Contracts.Sum(x => x.MonthlyStates[June].TotalOutstanding),
                juneSource.DataThroughPeriod
            },
            JulyPaymentTotals = new
            {
                PaidContracts = analysis.Contracts.Count(x => x.CurrentClassification.LatestPaymentStatus == MonthlyPaymentStatus.Paid),
                NotPaidContracts = analysis.Contracts.Count(x => x.CurrentClassification.LatestPaymentStatus == MonthlyPaymentStatus.NotPaid),
                NotDueContracts = analysis.Contracts.Count(x => x.CurrentClassification.LatestPaymentStatus == MonthlyPaymentStatus.NotDue),
                PaidMembers = analysis.Contracts.Where(x => x.CurrentClassification.LatestPaymentStatus == MonthlyPaymentStatus.Paid)
                    .Select(x => x.Contract.MemberKey).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                NotPaidMembers = analysis.Contracts.Where(x => x.CurrentClassification.LatestPaymentStatus == MonthlyPaymentStatus.NotPaid)
                    .Select(x => x.Contract.MemberKey).Distinct(StringComparer.OrdinalIgnoreCase).Count()
            },
            SequenceDiagnostics = new
            {
                SevenToTwelveContractsOriginatedAfterJanuary = analysis.Contracts.Count(x =>
                    x.CurrentClassification.Bucket == DebtBucket.SevenToTwelveMonthsDelinquent &&
                    x.Contract.ContractDate >= new DateOnly(2026, 2, 1))
            },
            Buckets = bucketSummaries,
            BucketComparisons = analysis.BucketComparisons.Select(item => new
            {
                Bucket = DebtLabels.Bucket(item.Bucket),
                item.PreviousMemberCount,
                item.CurrentMemberCount,
                item.PreviousContractCount,
                item.CurrentContractCount,
                item.PreviousOutstanding,
                item.CurrentOutstanding
            }).ToArray(),
            PaymentMovements = paymentMovements,
            BucketMovements = bucketMovements,
            ManagementMovement = new
            {
                NewContracts = Aggregate(analysis.Contracts.Where(x => x.MovementCategory == DebtMovementCategory.NewContract)),
                NewDelinquency = Aggregate(analysis.Contracts.Where(x => x.MovementCategory == DebtMovementCategory.NewDelinquency)),
                NewlyDeteriorated = Aggregate(analysis.Contracts.Where(x =>
                    x.PreviousClassification.Bucket == DebtBucket.Normal &&
                    x.CurrentClassification.Bucket is not DebtBucket.Normal and not DebtBucket.PaidOff)),
                ImprovedRecovered = Aggregate(analysis.Contracts.Where(x => x.MovementCategory == DebtMovementCategory.Improved)),
                MovedToWorseBucket = Aggregate(analysis.Contracts.Where(x =>
                    x.MovementCategory == DebtMovementCategory.Worsened &&
                    x.PreviousClassification.Bucket != DebtBucket.Normal)),
                SameBucketBalanceDecreased = Aggregate(analysis.Contracts.Where(x => x.MovementCategory == DebtMovementCategory.SameBucketBalanceDecreased)),
                SameBucketBalanceIncreased = Aggregate(analysis.Contracts.Where(x => x.MovementCategory == DebtMovementCategory.SameBucketBalanceIncreased)),
                PaidOff = Aggregate(analysis.Contracts.Where(x => x.MovementCategory == DebtMovementCategory.ClosedPaidOff))
            },
            SanitizedSamples = samples
        };

        _output.WriteLine(JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));

        Assert.NotEqual(HashFile(junePath), HashFile(julyPath));
        Assert.Equal(OperationalDebtWorkbookReader.CurrentWorksheetName, julySource.WorksheetName);
        Assert.Equal(July, julySource.DataThroughPeriod);
        Assert.True(worksheetRows > 5_000);
        Assert.True(julyColumnsPresent);
        Assert.True(dcGroupColumnPresent);
        Assert.True(analysis.Contracts.Count > juneSource.Contracts.Count);
        Assert.All(selectedBuckets, bucket => Assert.Contains(analysis.Contracts, x => x.CurrentClassification.Bucket == bucket));
    }

    private static object MapMovement(DebtMovementSummary movement) => new
    {
        movement.PreviousBucket,
        movement.CurrentBucket,
        movement.PaymentMovement,
        Category = movement.Category.HasValue ? DebtLabels.Category(movement.Category.Value) : string.Empty,
        movement.MemberCount,
        movement.ContractCount,
        movement.PreviousOutstanding,
        movement.CurrentOutstanding,
        movement.AmountChange
    };

    private static object Aggregate(IEnumerable<DebtContractAnalysis> source)
    {
        var items = source.ToArray();
        var previous = items.Sum(x => x.PreviousState.TotalOutstanding);
        var current = items.Sum(x => x.CurrentState.TotalOutstanding);
        return new
        {
            MemberCount = items.Select(x => x.Contract.MemberKey).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            ContractCount = items.Length,
            PreviousBalance = previous,
            CurrentBalance = current,
            BalanceChange = current - previous
        };
    }

    private static string SanitizeReference(string memberCode, string contractNumber) =>
        $"M-{HashText(memberCode)[..10]}/C-{HashText(contractNumber)[..10]}";

    private static string HashText(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim().Normalize())));

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string? FindWorkspaceRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "src", "COOPAI.sln")))
                    return directory.FullName;
                directory = directory.Parent;
            }
        }

        return null;
    }
}
