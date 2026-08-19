using System.Text.Json;
using COOPAI.API.Data;
using COOPAI.API.DTOs.PortfolioSnapshots;
using COOPAI.API.Models.Portfolio;
using Microsoft.EntityFrameworkCore;

namespace COOPAI.API.Services.PortfolioSnapshots;

public enum PortfolioSnapshotPersistenceStage
{
    HeaderTracked,
    RecordsTracked,
    ExclusionsTracked,
    BeforeSave
}

public interface IPortfolioSnapshotPersistenceHook
{
    Task OnStageAsync(
        PortfolioSnapshotPersistenceStage stage,
        PortfolioSnapshot snapshot,
        CancellationToken cancellationToken);
}

public sealed class PortfolioSnapshotWorkflowException(
    string code,
    string message,
    bool isConflict = false) : Exception(message)
{
    public string Code { get; } = code;
    public bool IsConflict { get; } = isConflict;
}

public interface IPortfolioSnapshotWorkflowService
{
    Task<PortfolioSnapshotValidationDto> ValidateAsync(
        string controlledCopyPath,
        string sourceFileName,
        DateOnly asOfDate,
        int? definitionVersion = null,
        CancellationToken cancellationToken = default);

    Task<PortfolioSnapshotDraftDto> CreateDraftAsync(
        string controlledCopyPath,
        string sourceFileName,
        DateOnly asOfDate,
        string expectedSourceFileHash,
        string expectedSnapshotContentHash,
        int? definitionVersion = null,
        CancellationToken cancellationToken = default);

    Task<PortfolioSnapshotLifecycleDto> ValidateDraftAsync(
        int id,
        CancellationToken cancellationToken = default);

    Task<PortfolioSnapshotLifecycleDto> RejectAsync(
        int id,
        string reason,
        CancellationToken cancellationToken = default);

    Task<PortfolioSnapshotReviewDto?> GetReviewAsync(
        int id,
        CancellationToken cancellationToken = default);

    Task<PortfolioSnapshotRecordPageDto?> GetRecordsAsync(
        int id,
        int page,
        int pageSize,
        string? termStatus,
        string? balanceStatus,
        string? inclusionStatus,
        string? warningCode,
        string? canonicalMatchStatus,
        string? memberMatchStatus,
        string? loanTypePrefix,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PortfolioSnapshotListItemDto>> ListAsync(
        CancellationToken cancellationToken = default);
}

public sealed class PortfolioSnapshotWorkflowService : IPortfolioSnapshotWorkflowService
{
    public const int CurrentDefinitionVersion = 2;

    private readonly CoopDbContext _dbContext;
    private readonly PortfolioSnapshotDryRunService _dryRunService;
    private readonly PortfolioSnapshotContentHasher _contentHasher;
    private readonly PortfolioSnapshotValidator _validator;
    private readonly IReadOnlyList<IPortfolioSnapshotPersistenceHook> _persistenceHooks;

    public PortfolioSnapshotWorkflowService(
        CoopDbContext dbContext,
        IPortfolioSnapshotSource source,
        IEnumerable<IPortfolioSnapshotPersistenceHook>? persistenceHooks = null,
        PortfolioSnapshotContentHasher? contentHasher = null,
        PortfolioSnapshotValidator? validator = null)
    {
        _dbContext = dbContext;
        _contentHasher = contentHasher ?? new PortfolioSnapshotContentHasher();
        _validator = validator ?? new PortfolioSnapshotValidator();
        _dryRunService = new PortfolioSnapshotDryRunService(
            dbContext,
            source,
            validator: _validator,
            contentHasher: _contentHasher);
        _persistenceHooks = persistenceHooks?.ToArray() ?? [];
    }

    public async Task<PortfolioSnapshotValidationDto> ValidateAsync(
        string controlledCopyPath,
        string sourceFileName,
        DateOnly asOfDate,
        int? definitionVersion = null,
        CancellationToken cancellationToken = default)
    {
        EnsureDefinitionVersion(definitionVersion);
        var result = await _dryRunService.BuildAsync(
            controlledCopyPath,
            asOfDate,
            cancellationToken: cancellationToken);
        result.Snapshot.SourceFileName = SafeFileName(sourceFileName);
        return MapValidation(result);
    }

    public async Task<PortfolioSnapshotDraftDto> CreateDraftAsync(
        string controlledCopyPath,
        string sourceFileName,
        DateOnly asOfDate,
        string expectedSourceFileHash,
        string expectedSnapshotContentHash,
        int? definitionVersion = null,
        CancellationToken cancellationToken = default)
    {
        EnsureDefinitionVersion(definitionVersion);
        EnsureHash(expectedSourceFileHash, nameof(expectedSourceFileHash));
        EnsureHash(expectedSnapshotContentHash, nameof(expectedSnapshotContentHash));

        var result = await _dryRunService.BuildAsync(
            controlledCopyPath,
            asOfDate,
            cancellationToken: cancellationToken);
        result.Snapshot.SourceFileName = SafeFileName(sourceFileName);

        if (!result.IsValid)
        {
            throw new PortfolioSnapshotWorkflowException(
                "SnapshotValidationFailed",
                "The source did not pass snapshot validation and no Draft was created.");
        }

        if (!HashEquals(result.Snapshot.SourceFileHash, expectedSourceFileHash))
        {
            throw new PortfolioSnapshotWorkflowException(
                "SourceFileHashMismatch",
                "The uploaded source file differs from the previously validated file.",
                true);
        }

        if (!HashEquals(result.Snapshot.SnapshotContentHash, expectedSnapshotContentHash))
        {
            throw new PortfolioSnapshotWorkflowException(
                "SnapshotContentHashMismatch",
                "The rebuilt snapshot content differs from the previously validated content.",
                true);
        }

        var existing = await FindSameIdentityAsync(result.Snapshot, cancellationToken);
        if (existing is not null)
            return ExistingIdentityResult(existing);

        var snapshot = result.Snapshot;
        snapshot.Status = PortfolioSnapshotStatus.Draft;
        snapshot.ValidatedAt = null;
        snapshot.RejectedAt = null;
        snapshot.RejectionReason = null;
        snapshot.CreatedAt = DateTime.UtcNow;
        snapshot.Revision = (await _dbContext.PortfolioSnapshots
            .AsNoTracking()
            .Where(x => x.AsOfDate == asOfDate)
            .MaxAsync(x => (int?)x.Revision, cancellationToken) ?? 0) + 1;

        DbUpdateException? concurrentInsertException = null;
        await using (var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            try
            {
                _dbContext.PortfolioSnapshots.Add(snapshot);
                await NotifyAsync(PortfolioSnapshotPersistenceStage.HeaderTracked, snapshot, cancellationToken);
                await NotifyAsync(PortfolioSnapshotPersistenceStage.RecordsTracked, snapshot, cancellationToken);
                await NotifyAsync(PortfolioSnapshotPersistenceStage.ExclusionsTracked, snapshot, cancellationToken);
                await NotifyAsync(PortfolioSnapshotPersistenceStage.BeforeSave, snapshot, cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateException exception)
            {
                await transaction.RollbackAsync(cancellationToken);
                _dbContext.ChangeTracker.Clear();
                concurrentInsertException = exception;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                _dbContext.ChangeTracker.Clear();
                throw;
            }
        }

        if (concurrentInsertException is not null)
        {
            var concurrent = await FindSameIdentityAsync(snapshot, cancellationToken);
            if (concurrent is not null)
                return ExistingIdentityResult(concurrent);
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(concurrentInsertException).Throw();
        }

        return MapDraft(snapshot, false);
    }

    public async Task<PortfolioSnapshotLifecycleDto> ValidateDraftAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await _dbContext.PortfolioSnapshots
            .Include(x => x.Records)
            .Include(x => x.Exclusions)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw NotFound(id);

        if (snapshot.Status != PortfolioSnapshotStatus.Draft)
        {
            throw new PortfolioSnapshotWorkflowException(
                "InvalidSnapshotTransition",
                $"Snapshot {id} cannot transition from {snapshot.Status} to Validated.",
                true);
        }

        var issues = ValidatePersistedSnapshot(snapshot);
        if (issues.Count > 0)
        {
            throw new PortfolioSnapshotWorkflowException(
                "PersistedSnapshotValidationFailed",
                string.Join("; ", issues.Select(x => x.Code)),
                true);
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        snapshot.Status = PortfolioSnapshotStatus.Validated;
        snapshot.ValidatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return MapLifecycle(snapshot);
    }

    public async Task<PortfolioSnapshotLifecycleDto> RejectAsync(
        int id,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var normalizedReason = reason?.Trim() ?? string.Empty;
        if (normalizedReason.Length is < 3 or > 1000)
        {
            throw new PortfolioSnapshotWorkflowException(
                "InvalidRejectionReason",
                "A rejection reason between 3 and 1000 characters is required.");
        }

        var snapshot = await _dbContext.PortfolioSnapshots.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw NotFound(id);
        if (snapshot.Status is not (PortfolioSnapshotStatus.Draft or PortfolioSnapshotStatus.Validated))
        {
            throw new PortfolioSnapshotWorkflowException(
                "InvalidSnapshotTransition",
                $"Snapshot {id} cannot transition from {snapshot.Status} to Rejected.",
                true);
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        snapshot.Status = PortfolioSnapshotStatus.Rejected;
        snapshot.RejectedAt = DateTime.UtcNow;
        snapshot.RejectionReason = normalizedReason;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return MapLifecycle(snapshot);
    }

    public async Task<PortfolioSnapshotReviewDto?> GetReviewAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await _dbContext.PortfolioSnapshots
            .AsNoTracking()
            .Include(x => x.Records)
            .Include(x => x.Exclusions)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        return snapshot is null ? null : MapReview(snapshot);
    }

    public async Task<PortfolioSnapshotRecordPageDto?> GetRecordsAsync(
        int id,
        int page,
        int pageSize,
        string? termStatus,
        string? balanceStatus,
        string? inclusionStatus,
        string? warningCode,
        string? canonicalMatchStatus,
        string? memberMatchStatus,
        string? loanTypePrefix,
        CancellationToken cancellationToken = default)
    {
        if (!await _dbContext.PortfolioSnapshots.AsNoTracking().AnyAsync(x => x.Id == id, cancellationToken))
            return null;

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var query = _dbContext.PortfolioSnapshotRecords.AsNoTracking().Where(x => x.PortfolioSnapshotId == id);
        query = ApplyEnumFilter(query, termStatus, x => x.TermStatus, "TermStatus");
        query = ApplyEnumFilter(query, balanceStatus, x => x.BalanceStatus, "BalanceStatus");
        query = ApplyEnumFilter(query, inclusionStatus, x => x.InclusionStatus, "InclusionStatus");
        query = ApplyEnumFilter(query, canonicalMatchStatus, x => x.CanonicalMatchStatus, "CanonicalMatchStatus");
        query = ApplyEnumFilter(query, memberMatchStatus, x => x.MemberMatchStatus, "MemberMatchStatus");
        if (!string.IsNullOrWhiteSpace(loanTypePrefix))
            query = query.Where(x => x.LoanTypePrefix == loanTypePrefix.Trim());

        var candidates = await query.OrderBy(x => x.SourceRowNumber).ThenBy(x => x.Id).ToListAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(warningCode))
        {
            var normalizedWarningCode = warningCode.Trim();
            candidates = candidates
                .Where(x => WarningCodes(x).Contains(normalizedWarningCode, StringComparer.Ordinal))
                .ToList();
        }

        var totalCount = candidates.Count;
        var items = candidates
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(MapRecord)
            .ToArray();
        return new PortfolioSnapshotRecordPageDto(page, pageSize, totalCount, items);
    }

    public async Task<IReadOnlyList<PortfolioSnapshotListItemDto>> ListAsync(
        CancellationToken cancellationToken = default) =>
        await _dbContext.PortfolioSnapshots
            .AsNoTracking()
            .Where(x => x.Status == PortfolioSnapshotStatus.Draft ||
                        x.Status == PortfolioSnapshotStatus.Validated ||
                        x.Status == PortfolioSnapshotStatus.Rejected)
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Select(x => new PortfolioSnapshotListItemDto(
                x.Id,
                x.AsOfDate,
                x.Revision,
                x.Status.ToString(),
                x.CreatedAt,
                x.SourceFileName,
                x.WarningRecordCount,
                x.TotalOutstanding))
            .ToListAsync(cancellationToken);

    private async Task<PortfolioSnapshot?> FindSameIdentityAsync(
        PortfolioSnapshot candidate,
        CancellationToken cancellationToken) =>
        await _dbContext.PortfolioSnapshots
            .AsNoTracking()
            .SingleOrDefaultAsync(x =>
                    x.SourceFileHash == candidate.SourceFileHash &&
                    x.AsOfDate == candidate.AsOfDate &&
                    x.DefinitionVersion == candidate.DefinitionVersion &&
                    x.SnapshotContentHash == candidate.SnapshotContentHash,
                cancellationToken);

    private static PortfolioSnapshotDraftDto ExistingIdentityResult(PortfolioSnapshot existing) =>
        existing.Status switch
        {
            PortfolioSnapshotStatus.Draft or PortfolioSnapshotStatus.Validated => MapDraft(existing, true),
            PortfolioSnapshotStatus.Rejected => throw new PortfolioSnapshotWorkflowException(
                "RejectedSnapshotRequiresExplicitReprocess",
                "This exact snapshot was rejected. Gate 2A does not support implicit reprocessing.",
                true),
            PortfolioSnapshotStatus.Published => throw new PortfolioSnapshotWorkflowException(
                "PublishedSnapshotIsImmutable",
                "This exact snapshot is already Published and cannot be altered by Gate 2A.",
                true),
            _ => throw new PortfolioSnapshotWorkflowException(
                "SnapshotIdentityConflict",
                $"This exact snapshot already exists with status {existing.Status}.",
                true)
        };

    private IReadOnlyList<PortfolioValidationIssue> ValidatePersistedSnapshot(PortfolioSnapshot snapshot)
    {
        var issues = new List<PortfolioValidationIssue>();
        Check(snapshot.Records.Count == snapshot.TotalContractCount, "PersistedTotalContractCountMismatch", issues);
        Check(snapshot.TotalSourceRows == snapshot.TotalContractCount + snapshot.PlaceholderRowCount, "PersistedSourcePopulationMismatch", issues);
        Check(snapshot.Records.Count(x => x.TermStatus == PortfolioTermStatus.InTerm) == snapshot.InTermContractCount, "PersistedInTermCountMismatch", issues);
        Check(snapshot.Records.Count(x => x.TermStatus == PortfolioTermStatus.Expired) == snapshot.ExpiredContractCount, "PersistedExpiredCountMismatch", issues);
        Check(snapshot.Records.Count(x => x.BalanceStatus == PortfolioBalanceStatus.Outstanding) == snapshot.OutstandingContractCount, "PersistedOutstandingCountMismatch", issues);
        Check(snapshot.Records.Count(x => x.BalanceStatus == PortfolioBalanceStatus.PaidOff) == snapshot.PaidOffContractCount, "PersistedPaidOffCountMismatch", issues);
        Check(snapshot.Records.Count(x => x.TermStatus == PortfolioTermStatus.InTerm && x.BalanceStatus == PortfolioBalanceStatus.Outstanding) == snapshot.InTermOutstandingContractCount, "PersistedInTermOutstandingCountMismatch", issues);
        Check(snapshot.Records.Count(x => x.TermStatus == PortfolioTermStatus.InTerm && x.BalanceStatus == PortfolioBalanceStatus.PaidOff) == snapshot.InTermPaidOffContractCount, "PersistedInTermPaidOffCountMismatch", issues);
        Check(snapshot.Records.Count(x => x.TermStatus == PortfolioTermStatus.Expired && x.BalanceStatus == PortfolioBalanceStatus.Outstanding) == snapshot.ExpiredOutstandingContractCount, "PersistedExpiredOutstandingCountMismatch", issues);
        Check(snapshot.Records.Count(x => x.TermStatus == PortfolioTermStatus.Expired && x.BalanceStatus == PortfolioBalanceStatus.PaidOff) == snapshot.ExpiredPaidOffContractCount, "PersistedExpiredPaidOffCountMismatch", issues);
        Check(snapshot.Records.Count(x => x.CanonicalMatchStatus == PortfolioCanonicalMatchStatus.Matched) == snapshot.MatchedCanonicalCount, "PersistedCanonicalMatchedCountMismatch", issues);
        Check(snapshot.Records.Count(x => x.CanonicalMatchStatus == PortfolioCanonicalMatchStatus.Missing) == snapshot.MissingCanonicalCount, "PersistedCanonicalMissingCountMismatch", issues);
        Check(snapshot.Records.Count(x => x.MemberMatchStatus == PortfolioMemberMatchStatus.Missing) == snapshot.UnresolvedMemberContractCount, "PersistedUnresolvedMemberCountMismatch", issues);
        Check(snapshot.Records.Count(HasAuditedSourceWarning) == snapshot.WarningRecordCount, "PersistedWarningCountMismatch", issues);
        Check(snapshot.Exclusions.Count(x => x.ReasonCode == PortfolioSnapshotCodes.MalformedShadowContractNo) == snapshot.ShadowExcludedCount, "PersistedShadowCountMismatch", issues);
        Check(snapshot.Records.Sum(x => x.PrincipalOutstanding) == snapshot.PrincipalOutstanding, "PersistedPrincipalOutstandingMismatch", issues);
        Check(snapshot.Records.Sum(x => x.ProfitOutstanding) == snapshot.ProfitOutstanding, "PersistedProfitOutstandingMismatch", issues);
        Check(snapshot.Records.Sum(x => x.TotalOutstanding) == snapshot.TotalOutstanding, "PersistedTotalOutstandingMismatch", issues);
        Check(snapshot.Records.Where(x => x.TermStatus == PortfolioTermStatus.Expired && x.BalanceStatus == PortfolioBalanceStatus.Outstanding).Sum(x => x.TotalOutstanding) == snapshot.ExpiredOutstandingTotal, "PersistedExpiredOutstandingTotalMismatch", issues);
        Check(snapshot.PrincipalDifference == 0m && snapshot.ProfitDifference == 0m && snapshot.TotalDifference == 0m && snapshot.ComponentDifference == 0m, "PersistedFinancialRemainderMismatch", issues);
        Check(HashEquals(_contentHasher.Compute(snapshot), snapshot.SnapshotContentHash), "PersistedSnapshotContentHashMismatch", issues);
        issues.AddRange(_validator.ValidateOneInTermContractPerMember(snapshot));
        return issues;
    }

    private static PortfolioSnapshotValidationDto MapValidation(PortfolioSnapshotDryRunResult result)
    {
        var warningIssues = result.Snapshot.Records
            .SelectMany(record => WarningCodes(record).Select(code => new PortfolioSnapshotIssueDto(
                code,
                "The record is included with a review warning.",
                record.SourceRowNumber,
                PortfolioValidationSeverity.Warning.ToString())))
            .Concat(result.Issues
                .Where(x => x.Severity == PortfolioValidationSeverity.Warning)
                .Select(x => new PortfolioSnapshotIssueDto(
                    x.Code,
                    x.Message,
                    x.SourceRowNumber,
                    x.Severity.ToString())))
            .GroupBy(x => new { x.Code, x.SourceRowNumber })
            .Select(group => group.First())
            .OrderBy(x => x.SourceRowNumber)
            .ThenBy(x => x.Code, StringComparer.Ordinal)
            .ToArray();
        var snapshot = result.Snapshot;
        return new PortfolioSnapshotValidationDto(
            result.IsValid,
            snapshot.SourceFileHash,
            snapshot.SnapshotContentHash,
            snapshot.AsOfDate,
            snapshot.DefinitionVersion,
            MapCounts(snapshot),
            MapFinancial(snapshot),
            new PortfolioSnapshotQualityDto(
                snapshot.BlockingErrorCount,
                warningIssues,
                snapshot.UnresolvedMemberContractCount,
                IsReconciled(snapshot)));
    }

    private static PortfolioSnapshotReviewDto MapReview(PortfolioSnapshot snapshot)
    {
        var warningSummary = snapshot.Records
            .SelectMany(record => WarningCodes(record)
                .Where(IsAuditedWarningCode)
                .Select(code => new { Code = code, Record = record }))
            .GroupBy(x => x.Code, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new PortfolioWarningSummaryDto(
                group.Key,
                group.Count(),
                group.OrderBy(x => x.Record.SourceRowNumber)
                    .Select(x => new PortfolioWarningAffectedRecordDto(
                        x.Record.SourceRowNumber,
                        x.Record.NormalizedContractNo,
                        x.Record.TermStatus.ToString(),
                        x.Record.BalanceStatus.ToString(),
                        x.Record.PrincipalOpening,
                        x.Record.ProfitOpening,
                        x.Record.TotalOpening,
                        x.Record.PrincipalOutstanding,
                        x.Record.ProfitOutstanding,
                        x.Record.TotalOutstanding))
                    .ToArray()))
            .ToArray();
        var unresolved = snapshot.Records
            .Where(x => x.MemberMatchStatus == PortfolioMemberMatchStatus.Missing)
            .OrderBy(x => x.SourceRowNumber)
            .Select(x => new PortfolioUnresolvedMemberDto(
                x.SourceRowNumber,
                x.NormalizedContractNo,
                x.TermStatus.ToString(),
                x.BalanceStatus.ToString(),
                !x.MemberId.HasValue))
            .ToArray();
        var exclusions = snapshot.Exclusions
            .GroupBy(x => x.ReasonCode, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new PortfolioExclusionSummaryDto(group.Key, group.Count()))
            .ToArray();
        return new PortfolioSnapshotReviewDto(
            new PortfolioSnapshotMetadataDto(
                snapshot.Id,
                snapshot.AsOfDate,
                snapshot.Revision,
                snapshot.Status.ToString(),
                snapshot.DefinitionVersion,
                snapshot.SourceFileName,
                snapshot.SourceFileHash,
                snapshot.SnapshotContentHash,
                snapshot.CreatedAt,
                snapshot.ValidatedAt,
                snapshot.RejectedAt,
                snapshot.RejectionReason),
            MapCounts(snapshot),
            MapFinancial(snapshot),
            warningSummary,
            unresolved,
            exclusions,
            false);
    }

    private static PortfolioSnapshotCountsDto MapCounts(PortfolioSnapshot snapshot) => new(
        snapshot.TotalSourceRows,
        snapshot.TotalContractCount,
        snapshot.PlaceholderRowCount,
        snapshot.InTermContractCount,
        snapshot.ExpiredContractCount,
        snapshot.OutstandingContractCount,
        snapshot.PaidOffContractCount,
        snapshot.InTermOutstandingContractCount,
        snapshot.InTermPaidOffContractCount,
        snapshot.ExpiredOutstandingContractCount,
        snapshot.ExpiredPaidOffContractCount,
        snapshot.MatchedCanonicalCount,
        snapshot.MissingCanonicalCount,
        snapshot.ShadowExcludedCount,
        snapshot.WarningRecordCount,
        snapshot.UnresolvedMemberContractCount);

    private static PortfolioSnapshotFinancialDto MapFinancial(PortfolioSnapshot snapshot) => new(
        snapshot.PrincipalOutstanding,
        snapshot.ProfitOutstanding,
        snapshot.TotalOutstanding,
        snapshot.ExpiredOutstandingTotal);

    private static PortfolioSnapshotRecordDto MapRecord(PortfolioSnapshotRecord record) => new(
        record.Id,
        record.SourceRowNumber,
        record.NormalizedContractNo,
        record.ContractDate,
        record.ExpireDate,
        record.TermStatus.ToString(),
        record.BalanceStatus.ToString(),
        record.InclusionStatus.ToString(),
        record.CanonicalMatchStatus.ToString(),
        record.MemberMatchStatus.ToString(),
        record.LoanTypePrefix,
        WarningCodes(record),
        record.PrincipalOutstanding,
        record.ProfitOutstanding,
        record.TotalOutstanding);

    private static PortfolioSnapshotDraftDto MapDraft(PortfolioSnapshot snapshot, bool wasExisting) => new(
        snapshot.Id,
        snapshot.Status.ToString(),
        wasExisting,
        snapshot.SourceFileHash,
        snapshot.SnapshotContentHash,
        snapshot.AsOfDate,
        snapshot.Revision);

    private static PortfolioSnapshotLifecycleDto MapLifecycle(PortfolioSnapshot snapshot) => new(
        snapshot.Id,
        snapshot.Status.ToString(),
        snapshot.ValidatedAt,
        snapshot.RejectedAt,
        snapshot.RejectionReason);

    private static IQueryable<PortfolioSnapshotRecord> ApplyEnumFilter<TEnum>(
        IQueryable<PortfolioSnapshotRecord> query,
        string? rawValue,
        System.Linq.Expressions.Expression<Func<PortfolioSnapshotRecord, TEnum>> selector,
        string fieldName)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(rawValue))
            return query;
        if (!Enum.TryParse<TEnum>(rawValue.Trim(), true, out var parsed) || !Enum.IsDefined(parsed))
            throw new PortfolioSnapshotWorkflowException("InvalidFilter", $"{fieldName} is invalid.");
        return query.Where(BuildEquals(selector, parsed));
    }

    private static System.Linq.Expressions.Expression<Func<PortfolioSnapshotRecord, bool>> BuildEquals<TEnum>(
        System.Linq.Expressions.Expression<Func<PortfolioSnapshotRecord, TEnum>> selector,
        TEnum value)
        where TEnum : struct, Enum
    {
        var body = System.Linq.Expressions.Expression.Equal(selector.Body, System.Linq.Expressions.Expression.Constant(value));
        return System.Linq.Expressions.Expression.Lambda<Func<PortfolioSnapshotRecord, bool>>(body, selector.Parameters);
    }

    private static IReadOnlyList<string> WarningCodes(PortfolioSnapshotRecord record) =>
        JsonSerializer.Deserialize<string[]>(record.WarningCodesJson) ?? [];

    private static bool HasAuditedSourceWarning(PortfolioSnapshotRecord record)
    {
        var codes = WarningCodes(record);
        return codes.Any(IsAuditedWarningCode);
    }

    private static bool IsAuditedWarningCode(string code) =>
        string.Equals(code, PortfolioSnapshotCodes.Negative, StringComparison.Ordinal) ||
        string.Equals(code, PortfolioSnapshotCodes.TotalBalanceMismatch, StringComparison.Ordinal);

    private static bool IsReconciled(PortfolioSnapshot snapshot) =>
        snapshot.TotalSourceRows == snapshot.TotalContractCount + snapshot.PlaceholderRowCount &&
        snapshot.TotalContractCount == snapshot.InTermContractCount + snapshot.ExpiredContractCount &&
        snapshot.TotalContractCount == snapshot.OutstandingContractCount + snapshot.PaidOffContractCount &&
        snapshot.TotalContractCount == snapshot.InTermOutstandingContractCount +
            snapshot.InTermPaidOffContractCount + snapshot.ExpiredOutstandingContractCount +
            snapshot.ExpiredPaidOffContractCount &&
        snapshot.PrincipalDifference == 0m && snapshot.ProfitDifference == 0m &&
        snapshot.TotalDifference == 0m && snapshot.ComponentDifference == 0m;

    private async Task NotifyAsync(
        PortfolioSnapshotPersistenceStage stage,
        PortfolioSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        foreach (var hook in _persistenceHooks)
            await hook.OnStageAsync(stage, snapshot, cancellationToken);
    }

    private static void Check(
        bool condition,
        string code,
        ICollection<PortfolioValidationIssue> issues)
    {
        if (!condition)
            issues.Add(new PortfolioValidationIssue(code, "Persisted snapshot reconciliation failed."));
    }

    private static void EnsureDefinitionVersion(int? definitionVersion)
    {
        if (definitionVersion is not null && definitionVersion != CurrentDefinitionVersion)
        {
            throw new PortfolioSnapshotWorkflowException(
                "UnsupportedDefinitionVersion",
                $"Gate 2A supports DefinitionVersion {CurrentDefinitionVersion} only.");
        }
    }

    private static void EnsureHash(string hash, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(hash) || hash.Trim().Length != 64 ||
            !hash.Trim().All(Uri.IsHexDigit))
        {
            throw new PortfolioSnapshotWorkflowException(
                "InvalidExpectedHash",
                $"{fieldName} must be a 64-character SHA-256 hexadecimal value.");
        }
    }

    private static bool HashEquals(string left, string right) =>
        string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string SafeFileName(string sourceFileName) =>
        Path.GetFileName(sourceFileName ?? string.Empty);

    private static PortfolioSnapshotWorkflowException NotFound(int id) => new(
        "SnapshotNotFound",
        $"Portfolio Snapshot {id} was not found.");
}
