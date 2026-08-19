using Microsoft.AspNetCore.Http;

namespace COOPAI.API.DTOs.PortfolioSnapshots;

public class PortfolioSnapshotValidateRequest
{
    public IFormFile? File { get; set; }
    public DateOnly AsOfDate { get; set; }
    public int? DefinitionVersion { get; set; }
}

public sealed class PortfolioSnapshotCreateDraftRequest : PortfolioSnapshotValidateRequest
{
    public string ExpectedSourceFileHash { get; set; } = string.Empty;
    public string ExpectedSnapshotContentHash { get; set; } = string.Empty;
}

public sealed class PortfolioSnapshotRejectRequest
{
    public string Reason { get; set; } = string.Empty;
}

public sealed record PortfolioSnapshotCountsDto(
    int SourceRows,
    int TotalContracts,
    int PlaceholderRows,
    int InTermContracts,
    int ExpiredContracts,
    int OutstandingContracts,
    int PaidOffContracts,
    int InTermOutstandingContracts,
    int InTermPaidOffContracts,
    int ExpiredOutstandingContracts,
    int ExpiredPaidOffContracts,
    int CanonicalMatchedContracts,
    int MissingCanonicalContracts,
    int ShadowExcludedContracts,
    int WarningContracts,
    int UnresolvedMemberContracts);

public sealed record PortfolioSnapshotFinancialDto(
    decimal PrincipalOutstanding,
    decimal ProfitOutstanding,
    decimal TotalOutstanding,
    decimal ExpiredOutstandingTotal);

public sealed record PortfolioSnapshotIssueDto(
    string Code,
    string Message,
    int? SourceRowNumber,
    string Severity);

public sealed record PortfolioSnapshotQualityDto(
    int BlockingErrorCount,
    IReadOnlyList<PortfolioSnapshotIssueDto> Warnings,
    int UnresolvedMembers,
    bool Reconciled);

public sealed record PortfolioSnapshotValidationDto(
    bool IsValid,
    string SourceFileHash,
    string SnapshotContentHash,
    DateOnly AsOfDate,
    int DefinitionVersion,
    PortfolioSnapshotCountsDto Counts,
    PortfolioSnapshotFinancialDto Financial,
    PortfolioSnapshotQualityDto Quality);

public sealed record PortfolioSnapshotDraftDto(
    int Id,
    string Status,
    bool WasExisting,
    string SourceFileHash,
    string SnapshotContentHash,
    DateOnly AsOfDate,
    int Revision);

public sealed record PortfolioSnapshotListItemDto(
    int Id,
    DateOnly AsOfDate,
    int Revision,
    string Status,
    DateTime CreatedAt,
    string SourceFileName,
    int WarningContracts,
    decimal TotalOutstanding);

public sealed record PortfolioSnapshotMetadataDto(
    int Id,
    DateOnly AsOfDate,
    int Revision,
    string Status,
    int DefinitionVersion,
    string SourceFileName,
    string SourceFileHash,
    string SnapshotContentHash,
    DateTime CreatedAt,
    DateTime? ValidatedAt,
    DateTime? RejectedAt,
    string? RejectionReason);

public sealed record PortfolioWarningAffectedRecordDto(
    int SourceRowNumber,
    string ContractNo,
    string TermStatus,
    string BalanceStatus,
    decimal PrincipalOpening,
    decimal ProfitOpening,
    decimal TotalOpening,
    decimal PrincipalOutstanding,
    decimal ProfitOutstanding,
    decimal TotalOutstanding);

public sealed record PortfolioWarningSummaryDto(
    string Code,
    int Count,
    IReadOnlyList<PortfolioWarningAffectedRecordDto> Records);

public sealed record PortfolioUnresolvedMemberDto(
    int SourceRowNumber,
    string ContractNo,
    string TermStatus,
    string BalanceStatus,
    bool MemberIdAbsent);

public sealed record PortfolioExclusionSummaryDto(
    string ReasonCode,
    int Count);

public sealed record PortfolioSnapshotReviewDto(
    PortfolioSnapshotMetadataDto Snapshot,
    PortfolioSnapshotCountsDto Counts,
    PortfolioSnapshotFinancialDto Financial,
    IReadOnlyList<PortfolioWarningSummaryDto> WarningSummary,
    IReadOnlyList<PortfolioUnresolvedMemberDto> UnresolvedMembers,
    IReadOnlyList<PortfolioExclusionSummaryDto> Exclusions,
    bool PublishingEnabled);

public sealed record PortfolioSnapshotRecordDto(
    long Id,
    int SourceRowNumber,
    string ContractNo,
    DateOnly? ContractDate,
    DateOnly? ExpireDate,
    string TermStatus,
    string BalanceStatus,
    string InclusionStatus,
    string CanonicalMatchStatus,
    string MemberMatchStatus,
    string LoanTypePrefix,
    IReadOnlyList<string> WarningCodes,
    decimal PrincipalOutstanding,
    decimal ProfitOutstanding,
    decimal TotalOutstanding);

public sealed record PortfolioSnapshotRecordPageDto(
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<PortfolioSnapshotRecordDto> Items);

public sealed record PortfolioSnapshotLifecycleDto(
    int Id,
    string Status,
    DateTime? ValidatedAt,
    DateTime? RejectedAt,
    string? RejectionReason);

public sealed record PortfolioSnapshotErrorDto(
    string Code,
    string Message);
