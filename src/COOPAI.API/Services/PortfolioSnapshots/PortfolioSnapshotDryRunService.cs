using System.Text;
using System.Text.Json;
using COOPAI.API.Data;
using COOPAI.API.Models.Portfolio;
using Microsoft.EntityFrameworkCore;

namespace COOPAI.API.Services.PortfolioSnapshots;

public sealed class PortfolioSnapshotDryRunService
{
    private readonly CoopDbContext _dbContext;
    private readonly IPortfolioSnapshotSource _source;
    private readonly SnapshotContractNoNormalizer _contractNormalizer;
    private readonly PortfolioSnapshotValidator _validator;
    private readonly PortfolioSnapshotContentHasher _contentHasher;

    public PortfolioSnapshotDryRunService(
        CoopDbContext dbContext,
        IPortfolioSnapshotSource source,
        SnapshotContractNoNormalizer? contractNormalizer = null,
        PortfolioSnapshotValidator? validator = null,
        PortfolioSnapshotContentHasher? contentHasher = null)
    {
        _dbContext = dbContext;
        _source = source;
        _contractNormalizer = contractNormalizer ?? new SnapshotContractNoNormalizer();
        _validator = validator ?? new PortfolioSnapshotValidator();
        _contentHasher = contentHasher ?? new PortfolioSnapshotContentHasher();
    }

    public async Task<PortfolioSnapshotDryRunResult> BuildAsync(
        string controlledCopyPath,
        DateOnly asOfDate,
        PortfolioSnapshotAcceptanceBaseline? acceptanceBaseline = null,
        CancellationToken cancellationToken = default)
    {
        var source = await _source.ReadAsync(controlledCopyPath, asOfDate, cancellationToken);
        var issues = new List<PortfolioValidationIssue>(source.Issues);
        var snapshot = CreateSnapshotHeader(source);

        var sourceCandidates = new List<(PortfolioSourceRow Row, ContractNoNormalizationResult Contract)>();
        foreach (var row in source.Rows.Where(x => x.SourceRowKind == PortfolioSourceRowKind.Unknown))
        {
            issues.Add(new PortfolioValidationIssue(
                "UnknownSourceRowKind",
                "A source row could not be proven to be either a contract or a template placeholder.",
                row.SourceRowNumber));
        }

        foreach (var row in source.Rows.Where(x => x.SourceRowKind == PortfolioSourceRowKind.Contract))
        {
            var normalized = _contractNormalizer.Normalize(row.ContractNo);
            if (!normalized.IsValid)
            {
                issues.Add(new PortfolioValidationIssue(
                    "MalformedSourceContractNo",
                    "A contract source row has a ContractNo that cannot be normalized safely.",
                    row.SourceRowNumber));
                continue;
            }

            sourceCandidates.Add((row, normalized));
            issues.AddRange(_validator.ValidateRow(row));
        }

        foreach (var duplicate in sourceCandidates
                     .GroupBy(x => x.Contract.NormalizedContractNo, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            foreach (var candidate in duplicate)
            {
                issues.Add(new PortfolioValidationIssue(
                    "DuplicateSourceContractNo",
                    "A normalized ContractNo occurs more than once in contract source rows.",
                    candidate.Row.SourceRowNumber));
            }
        }

        var canonicalContracts = await _dbContext.LoanContracts
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .Select(x => new { x.Id, x.ContractNo, x.MemberId })
            .ToListAsync(cancellationToken);
        var canonicalByContractNo = new Dictionary<string, List<(int Id, int MemberId)>>(StringComparer.Ordinal);
        foreach (var contract in canonicalContracts)
        {
            var normalized = _contractNormalizer.Normalize(contract.ContractNo);
            if (!normalized.IsValid)
            {
                var reason = normalized.ErrorCode == "UnsupportedContractType"
                    ? PortfolioSnapshotCodes.UnsupportedContractType
                    : PortfolioSnapshotCodes.MalformedShadowContractNo;
                snapshot.Exclusions.Add(new PortfolioSnapshotExclusion
                {
                    LoanContractId = contract.Id,
                    ReasonCode = reason
                });
                continue;
            }

            if (!canonicalByContractNo.TryGetValue(normalized.NormalizedContractNo, out var ids))
            {
                ids = new List<(int Id, int MemberId)>();
                canonicalByContractNo.Add(normalized.NormalizedContractNo, ids);
            }

            ids.Add((contract.Id, contract.MemberId));
        }

        var members = await _dbContext.Members
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .Select(x => new { x.Id, x.MemberNo })
            .ToListAsync(cancellationToken);
        var membersById = members.ToDictionary(x => x.Id, x => NormalizeMemberNo(x.MemberNo));

        foreach (var candidate in sourceCandidates)
        {
            canonicalByContractNo.TryGetValue(candidate.Contract.NormalizedContractNo, out var loanContractIds);
            var canonicalStatus = loanContractIds?.Count switch
            {
                1 => PortfolioCanonicalMatchStatus.Matched,
                _ => PortfolioCanonicalMatchStatus.Missing
            };
            int? loanContractId = loanContractIds?.Count == 1 ? loanContractIds[0].Id : null;
            if (loanContractIds?.Count > 1)
            {
                issues.Add(new PortfolioValidationIssue(
                    "AmbiguousCanonicalLoanContract",
                    "More than one non-deleted LoanContract matches the normalized ContractNo.",
                    candidate.Row.SourceRowNumber));
            }

            var canonicalMemberId = loanContractIds?.Count == 1
                ? loanContractIds[0].MemberId
                : (int?)null;
            var memberIsProven = canonicalMemberId.HasValue &&
                                 membersById.TryGetValue(canonicalMemberId.Value, out var canonicalMemberNo) &&
                                 !string.IsNullOrWhiteSpace(candidate.Row.MemberNo) &&
                                 NormalizeMemberNo(candidate.Row.MemberNo) == canonicalMemberNo;
            var memberStatus = memberIsProven
                ? PortfolioMemberMatchStatus.Matched
                : PortfolioMemberMatchStatus.Missing;
            int? memberId = memberIsProven ? canonicalMemberId : null;
            if (memberStatus != PortfolioMemberMatchStatus.Matched)
            {
                issues.Add(new PortfolioValidationIssue(
                    PortfolioSnapshotCodes.MissingMemberMatch,
                    "A stable member association could not be proven from the canonical contract and MemberNo.",
                    candidate.Row.SourceRowNumber,
                    PortfolioValidationSeverity.Warning));
            }

            var warningCodes = new SortedSet<string>(
                _validator.ClassifyWarnings(candidate.Row),
                StringComparer.Ordinal);
            if (canonicalStatus == PortfolioCanonicalMatchStatus.Missing)
                warningCodes.Add(PortfolioSnapshotCodes.MissingCanonicalContract);
            if (memberStatus == PortfolioMemberMatchStatus.Missing)
                warningCodes.Add(PortfolioSnapshotCodes.MissingMemberMatch);

            var termStatus = PortfolioContractStatusClassifier.ClassifyTerm(
                candidate.Row.ExpireDate,
                source.AsOfDate);
            var balanceStatus = PortfolioContractStatusClassifier.ClassifyBalance(
                candidate.Row.HasValidOutstandingValues
                    ? candidate.Row.Outstanding.Total
                    : null);

            var record = new PortfolioSnapshotRecord
            {
                SourceRowNumber = candidate.Row.SourceRowNumber,
                SourceRecordKey = candidate.Row.SourceRecordKey,
                NormalizedContractNo = candidate.Contract.NormalizedContractNo,
                ContractDate = candidate.Row.ContractDate,
                ExpireDate = candidate.Row.ExpireDate,
                LoanContractId = loanContractId,
                MemberId = memberId,
                SourceRowKind = candidate.Row.SourceRowKind,
                OpeningSide = candidate.Row.OpeningSide,
                TermStatus = termStatus,
                BalanceStatus = balanceStatus,
                CanonicalMatchStatus = canonicalStatus,
                MemberMatchStatus = memberStatus,
                LoanTypePrefix = candidate.Contract.LoanTypePrefix,
                InclusionStatus = warningCodes.Count == 0
                    ? PortfolioSnapshotInclusionStatus.Included
                    : PortfolioSnapshotInclusionStatus.IncludedWithWarning,
                WarningCodesJson = JsonSerializer.Serialize(warningCodes),
                PrincipalOpening = candidate.Row.Opening.Principal,
                ProfitOpening = candidate.Row.Opening.Profit,
                TotalOpening = candidate.Row.Opening.Total,
                PrincipalRepayment = candidate.Row.Repayment.Principal,
                ProfitRepayment = candidate.Row.Repayment.Profit,
                TotalRepayment = candidate.Row.Repayment.Total,
                PrincipalOutstanding = candidate.Row.Outstanding.Principal,
                ProfitOutstanding = candidate.Row.Outstanding.Profit,
                TotalOutstanding = candidate.Row.Outstanding.Total
            };
            snapshot.Records.Add(record);
        }

        PopulateAggregates(snapshot, source);
        issues.AddRange(_validator.ValidateOneInTermContractPerMember(snapshot));
        issues.AddRange(_validator.ValidateReportSummary(snapshot, source.ReportSummary));
        if (acceptanceBaseline is not null)
            issues.AddRange(_validator.ValidateBaseline(snapshot, acceptanceBaseline));

        snapshot.BlockingErrorCount = issues.Count(x => x.Severity == PortfolioValidationSeverity.Blocking);
        snapshot.Status = snapshot.BlockingErrorCount == 0
            ? PortfolioSnapshotStatus.Validated
            : PortfolioSnapshotStatus.Failed;
        snapshot.ValidatedAt = snapshot.Status == PortfolioSnapshotStatus.Validated
            ? DateTime.UtcNow
            : null;
        snapshot.SnapshotContentHash = _contentHasher.Compute(snapshot);

        return new PortfolioSnapshotDryRunResult
        {
            Source = source,
            Snapshot = snapshot,
            Issues = issues
        };
    }

    private static PortfolioSnapshot CreateSnapshotHeader(PortfolioSourceReadResult source) => new()
    {
        AsOfDate = source.AsOfDate,
        Revision = 1,
        Status = PortfolioSnapshotStatus.Draft,
        DefinitionVersion = 2,
        CurrencyCode = "THB",
        SourceType = PortfolioSnapshotCodes.ExcelSourceType,
        SourceFileName = source.SourceFileName,
        SourceFileHash = source.SourceFileHash,
        SourceFileSizeBytes = source.SourceFileSizeBytes,
        SourceRetrievedAt = source.SourceRetrievedAt,
        SourceDataThroughDate = source.SourceDataThroughDate
    };

    private static void PopulateAggregates(PortfolioSnapshot snapshot, PortfolioSourceReadResult source)
    {
        snapshot.TotalSourceRows = source.Rows.Count;
        snapshot.TotalContractCount = source.Rows.Count(x => x.SourceRowKind == PortfolioSourceRowKind.Contract);
        snapshot.PlaceholderRowCount = source.Rows.Count(x => x.SourceRowKind == PortfolioSourceRowKind.TemplatePlaceholder);
        snapshot.MatchedCanonicalCount = snapshot.Records.Count(x => x.CanonicalMatchStatus == PortfolioCanonicalMatchStatus.Matched);
        snapshot.MissingCanonicalCount = snapshot.Records.Count(x => x.CanonicalMatchStatus == PortfolioCanonicalMatchStatus.Missing);
        snapshot.UnresolvedMemberContractCount = snapshot.Records.Count(x => x.MemberMatchStatus != PortfolioMemberMatchStatus.Matched);
        snapshot.WarningRecordCount = snapshot.Records.Count(HasAuditedSourceWarning);
        snapshot.ShadowExcludedCount = snapshot.Exclusions.Count(
            x => x.ReasonCode == PortfolioSnapshotCodes.MalformedShadowContractNo);
        snapshot.InTermContractCount = snapshot.Records.Count(x => x.TermStatus == PortfolioTermStatus.InTerm);
        snapshot.ExpiredContractCount = snapshot.Records.Count(x => x.TermStatus == PortfolioTermStatus.Expired);
        snapshot.OutstandingContractCount = snapshot.Records.Count(x => x.BalanceStatus == PortfolioBalanceStatus.Outstanding);
        snapshot.PaidOffContractCount = snapshot.Records.Count(x => x.BalanceStatus == PortfolioBalanceStatus.PaidOff);
        snapshot.InTermOutstandingContractCount = snapshot.Records.Count(
            x => x.TermStatus == PortfolioTermStatus.InTerm && x.BalanceStatus == PortfolioBalanceStatus.Outstanding);
        snapshot.InTermPaidOffContractCount = snapshot.Records.Count(
            x => x.TermStatus == PortfolioTermStatus.InTerm && x.BalanceStatus == PortfolioBalanceStatus.PaidOff);
        snapshot.ExpiredOutstandingContractCount = snapshot.Records.Count(
            x => x.TermStatus == PortfolioTermStatus.Expired && x.BalanceStatus == PortfolioBalanceStatus.Outstanding);
        snapshot.ExpiredPaidOffContractCount = snapshot.Records.Count(
            x => x.TermStatus == PortfolioTermStatus.Expired && x.BalanceStatus == PortfolioBalanceStatus.PaidOff);
        snapshot.ExpiredOutstandingTotal = snapshot.Records
            .Where(x => x.TermStatus == PortfolioTermStatus.Expired &&
                        x.BalanceStatus == PortfolioBalanceStatus.Outstanding)
            .Sum(x => x.TotalOutstanding);

        snapshot.PrincipalOpening = snapshot.Records.Sum(x => x.PrincipalOpening);
        snapshot.ProfitOpening = snapshot.Records.Sum(x => x.ProfitOpening);
        snapshot.TotalOpening = snapshot.Records.Sum(x => x.TotalOpening);
        snapshot.PrincipalRepayment = snapshot.Records.Sum(x => x.PrincipalRepayment);
        snapshot.ProfitRepayment = snapshot.Records.Sum(x => x.ProfitRepayment);
        snapshot.TotalRepayment = snapshot.Records.Sum(x => x.TotalRepayment);
        snapshot.PrincipalOutstanding = snapshot.Records.Sum(x => x.PrincipalOutstanding);
        snapshot.ProfitOutstanding = snapshot.Records.Sum(x => x.ProfitOutstanding);
        snapshot.TotalOutstanding = snapshot.Records.Sum(x => x.TotalOutstanding);
        snapshot.PrincipalDifference = snapshot.PrincipalOpening - snapshot.PrincipalRepayment - snapshot.PrincipalOutstanding;
        snapshot.ProfitDifference = snapshot.ProfitOpening - snapshot.ProfitRepayment - snapshot.ProfitOutstanding;
        snapshot.TotalDifference = snapshot.TotalOpening - snapshot.TotalRepayment - snapshot.TotalOutstanding;
        snapshot.ComponentDifference = snapshot.TotalOutstanding - snapshot.PrincipalOutstanding - snapshot.ProfitOutstanding;
    }

    private static bool HasAuditedSourceWarning(PortfolioSnapshotRecord record)
    {
        var warningCodes = JsonSerializer.Deserialize<string[]>(record.WarningCodesJson) ?? [];
        return warningCodes.Contains(PortfolioSnapshotCodes.Negative, StringComparer.Ordinal) ||
               warningCodes.Contains(PortfolioSnapshotCodes.TotalBalanceMismatch, StringComparer.Ordinal);
    }

    private static string NormalizeMemberNo(string? memberNo) =>
        (memberNo ?? string.Empty).Trim().Normalize(NormalizationForm.FormC);
}
