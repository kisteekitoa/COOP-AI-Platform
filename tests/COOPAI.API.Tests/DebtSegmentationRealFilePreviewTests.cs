using System.Text.Json;
using COOPAI.API.Services.DebtSegmentation;
using Xunit.Abstractions;

namespace COOPAI.API.Tests;

public sealed class DebtSegmentationRealFilePreviewTests
{
    private readonly ITestOutputHelper _output;

    public DebtSegmentationRealFilePreviewTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void PreservedControlledWorkbook_IsProvenJuneOnly_AndCanProduceJuneDiagnostic()
    {
        var workbookPath = FindWorkspaceFile("Loan.xlsx");
        if (workbookPath is null)
            return;

        var source = new OperationalDebtWorkbookReader().Read(workbookPath, allowLegacyDiagnostic: true);
        Assert.Equal(OperationalDebtWorkbookReader.LegacyWorksheetName, source.WorksheetName);
        Assert.Equal(new DateOnly(2026, 6, 1), source.DataThroughPeriod);

        var analysis = new DebtSegmentationAnalyzer().Analyze(
            source.Contracts,
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 6, 1));
        var evidence = new
        {
            source.SourceFileName,
            source.WorksheetName,
            source.DataThroughPeriod,
            ContractCount = source.Contracts.Count,
            MemberCount = source.Contracts.Select(x => x.MemberKey).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            TotalOutstanding = analysis.Contracts.Sum(x => x.CurrentState.TotalOutstanding),
            Buckets = analysis.BucketSummaries.Select(x => new
            {
                Bucket = DebtLabels.Bucket(x.Bucket),
                x.MemberCount,
                x.ContractCount,
                x.OutstandingAmount
            }),
            Samples = analysis.Contracts
                .GroupBy(x => x.CurrentClassification.Bucket)
                .OrderBy(x => x.Key)
                .Select(group =>
                {
                    var sample = group.OrderBy(x => x.Contract.SourceRowNumber).First();
                    return new
                    {
                        Bucket = DebtLabels.Bucket(group.Key),
                        sample.Contract.SourceRowNumber,
                        sample.Contract.MemberCode,
                        sample.Contract.MemberName,
                        sample.Contract.ContractNumber,
                        sample.Contract.ExpireDate,
                        Payment = DebtLabels.Payment(sample.CurrentClassification.LatestPaymentStatus),
                        sample.CurrentClassification.FirstMissedPaymentPeriod,
                        sample.CurrentState.TotalOutstanding
                    };
                })
        };
        _output.WriteLine(JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));

        Assert.Equal(4_454, source.Contracts.Count);
        Assert.Equal(4_454, analysis.Contracts.Count);
    }

    private static string? FindWorkspaceFile(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, name);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        return null;
    }
}
