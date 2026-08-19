using COOPAI.API.Models.Portfolio;

namespace COOPAI.API.Services.PortfolioSnapshots;

public sealed class PortfolioSnapshotValidator
{
    public IReadOnlyList<PortfolioValidationIssue> ValidateRow(PortfolioSourceRow row)
    {
        if (row.SourceRowKind != PortfolioSourceRowKind.Contract)
            return Array.Empty<PortfolioValidationIssue>();

        var issues = new List<PortfolioValidationIssue>();
        if (row.ContractDate is null)
        {
            issues.Add(new PortfolioValidationIssue(
                "ContractDateUnavailable",
                "ContractDate is missing or invalid on a contract source row.",
                row.SourceRowNumber));
        }
        if (row.ExpireDate is null)
        {
            issues.Add(new PortfolioValidationIssue(
                "ExpireDateUnavailable",
                "ExpireDate is missing or invalid on a contract source row.",
                row.SourceRowNumber));
        }
        if (!row.HasValidOpeningValues || !row.HasValidRepaymentValues || !row.HasValidOutstandingValues)
        {
            issues.Add(new PortfolioValidationIssue(
                "FinancialValuesUnavailable",
                "One or more required financial groups are missing or invalid.",
                row.SourceRowNumber));
        }
        CheckEqual(
            row.Opening.Principal + row.Opening.Profit,
            row.Opening.Total,
            "OpeningComponentMismatch",
            "Opening principal plus profit does not equal opening total.",
            row.SourceRowNumber,
            issues);
        CheckEqual(
            row.Repayment.Principal + row.Repayment.Profit,
            row.Repayment.Total,
            "RepaymentComponentMismatch",
            "Repayment principal plus profit does not equal repayment total.",
            row.SourceRowNumber,
            issues);
        CheckEqual(
            row.Outstanding.Principal + row.Outstanding.Profit,
            row.Outstanding.Total,
            "OutstandingComponentMismatch",
            "Outstanding principal plus profit does not equal outstanding total.",
            row.SourceRowNumber,
            issues);
        CheckEqual(
            row.Opening.Principal - row.Repayment.Principal,
            row.Outstanding.Principal,
            "PrincipalReconciliationMismatch",
            "Opening principal minus principal repayment does not equal principal outstanding.",
            row.SourceRowNumber,
            issues);
        CheckEqual(
            row.Opening.Profit - row.Repayment.Profit,
            row.Outstanding.Profit,
            "ProfitReconciliationMismatch",
            "Opening profit minus profit repayment does not equal profit outstanding.",
            row.SourceRowNumber,
            issues);
        CheckEqual(
            row.Opening.Total - row.Repayment.Total,
            row.Outstanding.Total,
            "TotalReconciliationMismatch",
            "Opening total minus total repayment does not equal total outstanding.",
            row.SourceRowNumber,
            issues);

        return issues;
    }

    public IReadOnlyList<string> ClassifyWarnings(PortfolioSourceRow row)
    {
        if (row.SourceRowKind != PortfolioSourceRowKind.Contract)
            return Array.Empty<string>();

        var warningCodes = new SortedSet<string>(StringComparer.Ordinal);
        if (HasNegative(row.Opening))
            warningCodes.Add(PortfolioSnapshotCodes.Negative);

        if (row.DisplayedOpening is { } displayed &&
            displayed.Principal + displayed.Profit != displayed.Total)
        {
            warningCodes.Add(PortfolioSnapshotCodes.TotalBalanceMismatch);
        }

        return warningCodes.ToArray();
    }

    public IReadOnlyList<PortfolioValidationIssue> ValidateOneInTermContractPerMember(
        PortfolioSnapshot snapshot) =>
        snapshot.Records
            .Where(record => record.MemberId.HasValue && record.TermStatus == PortfolioTermStatus.InTerm)
            .GroupBy(record => record.MemberId!.Value)
            .Where(group => group.Count() > 1)
            .OrderBy(group => group.Key)
            .Select(group => new PortfolioValidationIssue(
                "MultipleInTermContractsForMember",
                $"MemberId {group.Key} has more than one InTerm contract: {string.Join(", ", group.Select(x => x.NormalizedContractNo).OrderBy(x => x, StringComparer.Ordinal))}."))
            .ToArray();

    public IReadOnlyList<PortfolioValidationIssue> ValidateReportSummary(
        PortfolioSnapshot snapshot,
        PortfolioReportSummary? reportSummary)
    {
        if (reportSummary is null)
        {
            return
            [
                new PortfolioValidationIssue(
                    "ReportSummaryUnavailable",
                    "The report summary cannot be reconciled because cached summary values are unavailable.")
            ];
        }

        var issues = new List<PortfolioValidationIssue>();
        CheckAggregate(snapshot.PrincipalOpening, reportSummary.Opening.Principal, "ReportPrincipalOpeningMismatch", issues);
        CheckAggregate(snapshot.ProfitOpening, reportSummary.Opening.Profit, "ReportProfitOpeningMismatch", issues);
        CheckAggregate(snapshot.TotalOpening, reportSummary.Opening.Total, "ReportTotalOpeningMismatch", issues);
        CheckAggregate(snapshot.PrincipalRepayment, reportSummary.Repayment.Principal, "ReportPrincipalRepaymentMismatch", issues);
        CheckAggregate(snapshot.ProfitRepayment, reportSummary.Repayment.Profit, "ReportProfitRepaymentMismatch", issues);
        CheckAggregate(snapshot.TotalRepayment, reportSummary.Repayment.Total, "ReportTotalRepaymentMismatch", issues);
        CheckAggregate(snapshot.PrincipalOutstanding, reportSummary.Outstanding.Principal, "ReportPrincipalOutstandingMismatch", issues);
        CheckAggregate(snapshot.ProfitOutstanding, reportSummary.Outstanding.Profit, "ReportProfitOutstandingMismatch", issues);
        CheckAggregate(snapshot.TotalOutstanding, reportSummary.Outstanding.Total, "ReportTotalOutstandingMismatch", issues);
        return issues;
    }

    public IReadOnlyList<PortfolioValidationIssue> ValidateBaseline(
        PortfolioSnapshot snapshot,
        PortfolioSnapshotAcceptanceBaseline baseline)
    {
        var issues = new List<PortfolioValidationIssue>();
        CheckBaseline(snapshot.AsOfDate, baseline.AsOfDate, "BaselineAsOfDateMismatch", issues);
        CheckBaseline(snapshot.TotalSourceRows, baseline.TotalSourceRows, "BaselineTotalSourceRowsMismatch", issues);
        CheckBaseline(snapshot.TotalContractCount, baseline.TotalContractCount, "BaselineTotalContractCountMismatch", issues);
        CheckBaseline(snapshot.PlaceholderRowCount, baseline.PlaceholderRowCount, "BaselinePlaceholderRowCountMismatch", issues);
        CheckBaseline(snapshot.MatchedCanonicalCount, baseline.MatchedCanonicalCount, "BaselineMatchedCanonicalCountMismatch", issues);
        CheckBaseline(snapshot.MissingCanonicalCount, baseline.MissingCanonicalCount, "BaselineMissingCanonicalCountMismatch", issues);
        CheckBaseline(snapshot.UnresolvedMemberContractCount, baseline.UnresolvedMemberContractCount, "BaselineUnresolvedMemberContractCountMismatch", issues);
        CheckBaseline(snapshot.WarningRecordCount, baseline.WarningRecordCount, "BaselineWarningRecordCountMismatch", issues);
        CheckBaseline(snapshot.ShadowExcludedCount, baseline.ShadowExcludedCount, "BaselineShadowExcludedCountMismatch", issues);
        CheckBaseline(snapshot.InTermContractCount, baseline.InTermContractCount, "BaselineInTermContractCountMismatch", issues);
        CheckBaseline(snapshot.ExpiredContractCount, baseline.ExpiredContractCount, "BaselineExpiredContractCountMismatch", issues);
        CheckBaseline(snapshot.OutstandingContractCount, baseline.OutstandingContractCount, "BaselineOutstandingContractCountMismatch", issues);
        CheckBaseline(snapshot.PaidOffContractCount, baseline.PaidOffContractCount, "BaselinePaidOffContractCountMismatch", issues);
        CheckBaseline(snapshot.InTermOutstandingContractCount, baseline.InTermOutstandingContractCount, "BaselineInTermOutstandingContractCountMismatch", issues);
        CheckBaseline(snapshot.InTermPaidOffContractCount, baseline.InTermPaidOffContractCount, "BaselineInTermPaidOffContractCountMismatch", issues);
        CheckBaseline(snapshot.ExpiredOutstandingContractCount, baseline.ExpiredOutstandingContractCount, "BaselineExpiredOutstandingContractCountMismatch", issues);
        CheckBaseline(snapshot.ExpiredPaidOffContractCount, baseline.ExpiredPaidOffContractCount, "BaselineExpiredPaidOffContractCountMismatch", issues);
        CheckBaseline(snapshot.ExpiredOutstandingTotal, baseline.ExpiredOutstandingTotal, "BaselineExpiredOutstandingTotalMismatch", issues);
        CheckBaseline(snapshot.PrincipalOpening, baseline.Opening.Principal, "BaselinePrincipalOpeningMismatch", issues);
        CheckBaseline(snapshot.ProfitOpening, baseline.Opening.Profit, "BaselineProfitOpeningMismatch", issues);
        CheckBaseline(snapshot.TotalOpening, baseline.Opening.Total, "BaselineTotalOpeningMismatch", issues);
        CheckBaseline(snapshot.PrincipalRepayment, baseline.Repayment.Principal, "BaselinePrincipalRepaymentMismatch", issues);
        CheckBaseline(snapshot.ProfitRepayment, baseline.Repayment.Profit, "BaselineProfitRepaymentMismatch", issues);
        CheckBaseline(snapshot.TotalRepayment, baseline.Repayment.Total, "BaselineTotalRepaymentMismatch", issues);
        CheckBaseline(snapshot.PrincipalOutstanding, baseline.Outstanding.Principal, "BaselinePrincipalOutstandingMismatch", issues);
        CheckBaseline(snapshot.ProfitOutstanding, baseline.Outstanding.Profit, "BaselineProfitOutstandingMismatch", issues);
        CheckBaseline(snapshot.TotalOutstanding, baseline.Outstanding.Total, "BaselineTotalOutstandingMismatch", issues);
        CheckBaseline(snapshot.PrincipalDifference, 0m, "BaselinePrincipalRemainderMismatch", issues);
        CheckBaseline(snapshot.ProfitDifference, 0m, "BaselineProfitRemainderMismatch", issues);
        CheckBaseline(snapshot.TotalDifference, 0m, "BaselineTotalRemainderMismatch", issues);
        CheckBaseline(snapshot.ComponentDifference, 0m, "BaselineComponentRemainderMismatch", issues);
        return issues;
    }

    private static bool HasNegative(PortfolioFinancialValues values) =>
        values.Principal < 0m || values.Profit < 0m || values.Total < 0m;

    private static void CheckEqual(
        decimal actual,
        decimal expected,
        string code,
        string message,
        int sourceRowNumber,
        ICollection<PortfolioValidationIssue> issues)
    {
        if (actual != expected)
            issues.Add(new PortfolioValidationIssue(code, message, sourceRowNumber));
    }

    private static void CheckAggregate(
        decimal actual,
        decimal expected,
        string code,
        ICollection<PortfolioValidationIssue> issues)
    {
        if (actual != expected)
        {
            issues.Add(new PortfolioValidationIssue(
                code,
                "Snapshot aggregates do not match the cached report summary."));
        }
    }

    private static void CheckBaseline<T>(
        T actual,
        T expected,
        string code,
        ICollection<PortfolioValidationIssue> issues)
        where T : IEquatable<T>
    {
        if (!actual.Equals(expected))
            issues.Add(new PortfolioValidationIssue(code, "Snapshot output does not match the approved acceptance baseline."));
    }
}
