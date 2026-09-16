namespace COOPAI.API.Services.DebtSegmentation;

public sealed class DebtSegmentationAnalyzer
{
    private readonly IDebtSegmentationPolicy _policy;

    public DebtSegmentationAnalyzer(IDebtSegmentationPolicy? policy = null)
    {
        _policy = policy ?? new DebtSegmentationPolicyV1();
    }

    public string PolicyVersion => _policy.Version;

    public DebtClassification Classify(DebtContractHistory contract, DateOnly period)
    {
        var normalizedPeriod = MonthStart(period);
        if (!contract.MonthlyStates.TryGetValue(normalizedPeriod, out var state))
            throw new ArgumentException($"Monthly state {normalizedPeriod:yyyy-MM} is unavailable.", nameof(period));

        var firstDuePeriod = _policy.FirstDuePeriod(contract.ContractDate);
        var lastPaymentPeriod = LastPaymentPeriod(contract, normalizedPeriod);
        var paymentStatus = PaymentStatus(contract, state, normalizedPeriod);
        if (state.TotalOutstanding <= 0m)
            return new DebtClassification(
                DebtBucket.PaidOff, paymentStatus, firstDuePeriod, null, 0, lastPaymentPeriod,
                DebtClassificationBasis.PaidOffBalance);

        if (paymentStatus == MonthlyPaymentStatus.NotDue)
            return new DebtClassification(
                DebtBucket.Normal, paymentStatus, firstDuePeriod, null, 0, lastPaymentPeriod,
                DebtClassificationBasis.BeforeFirstDuePeriod);

        var asOfDate = normalizedPeriod.AddMonths(1).AddDays(-1);
        var expiryBucket = _policy.ClassifyExpiredDebt(contract.ExpireDate, asOfDate);
        if (expiryBucket.HasValue)
        {
            var (firstMissed, consecutive) = CurrentMissedSequence(contract, normalizedPeriod);
            return new DebtClassification(
                expiryBucket.Value, paymentStatus, firstDuePeriod, firstMissed, consecutive, lastPaymentPeriod,
                DebtClassificationBasis.ContractExpiryAge);
        }

        if (paymentStatus == MonthlyPaymentStatus.Paid)
            return new DebtClassification(
                DebtBucket.Normal, paymentStatus, firstDuePeriod, null, 0, lastPaymentPeriod,
                DebtClassificationBasis.LatestMonthPayment);

        var (firstMissedPeriod, missedMonths) = CurrentMissedSequence(contract, normalizedPeriod);
        var bucket = _policy.ClassifyConsecutiveMisses(missedMonths);
        return new DebtClassification(
            bucket, paymentStatus, firstDuePeriod, firstMissedPeriod, missedMonths, lastPaymentPeriod,
            DebtClassificationBasis.ConsecutiveNoPayment);
    }

    public DebtSegmentationAnalysis Analyze(
        IReadOnlyList<DebtContractHistory> histories,
        DateOnly previousPeriod,
        DateOnly currentPeriod)
    {
        previousPeriod = MonthStart(previousPeriod);
        currentPeriod = MonthStart(currentPeriod);
        var contracts = histories
            .Where(x => x.MonthlyStates.ContainsKey(currentPeriod))
            .Select(history => AnalyzeContract(history, previousPeriod, currentPeriod))
            .ToArray();

        var bucketSummaries = _policy.OrderedBuckets
            .Select(bucket =>
            {
                var matches = contracts.Where(x => x.CurrentClassification.Bucket == bucket).ToArray();
                var paid = matches.Where(x => x.CurrentClassification.LatestPaymentStatus == MonthlyPaymentStatus.Paid).ToArray();
                var notPaid = matches.Where(x => x.CurrentClassification.LatestPaymentStatus == MonthlyPaymentStatus.NotPaid).ToArray();
                var notDue = matches.Where(x => x.CurrentClassification.LatestPaymentStatus == MonthlyPaymentStatus.NotDue).ToArray();
                return new DebtBucketSummary(
                    bucket,
                    MemberCount(matches),
                    matches.Length,
                    matches.Sum(x => x.CurrentState.PrincipalOutstanding),
                    matches.Sum(x => x.CurrentState.ProfitOutstanding),
                    matches.Sum(x => x.CurrentState.TotalOutstanding),
                    MemberCount(paid),
                    paid.Length,
                    MemberCount(notPaid),
                    notPaid.Length,
                    MemberCount(notDue),
                    notDue.Length);
            })
            .ToArray();

        var bucketComparisons = _policy.OrderedBuckets
            .Select(bucket =>
            {
                var previous = contracts.Where(x => !x.IsNewContract && x.PreviousClassification.Bucket == bucket).ToArray();
                var current = contracts.Where(x => x.CurrentClassification.Bucket == bucket).ToArray();
                var previousOutstanding = previous.Sum(x => x.PreviousState.TotalOutstanding);
                var currentOutstanding = current.Sum(x => x.CurrentState.TotalOutstanding);
                return new DebtBucketPeriodComparison(
                    bucket,
                    MemberCount(previous),
                    MemberCount(current),
                    MemberCount(current) - MemberCount(previous),
                    previous.Length,
                    current.Length,
                    current.Length - previous.Length,
                    previousOutstanding,
                    currentOutstanding,
                    currentOutstanding - previousOutstanding,
                    current.Count(x => x.PreviousClassification.Bucket != bucket),
                    current.Count(x => x.MovementCategory == DebtMovementCategory.Improved),
                    current.Count(x => x.MovementCategory == DebtMovementCategory.Worsened),
                    previous.Count(x => x.MovementCategory == DebtMovementCategory.ClosedPaidOff),
                    current.Count(x => x.PreviousClassification.Bucket == bucket &&
                                       x.MovementCategory == DebtMovementCategory.SameBucketBalanceDecreased),
                    current.Count(x => x.PreviousClassification.Bucket == bucket &&
                                       x.MovementCategory == DebtMovementCategory.SameBucketBalanceIncreased));
            })
            .ToArray();

        var paymentMovements = contracts
            .GroupBy(x => x.PaymentMovement, StringComparer.Ordinal)
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(group => MovementSummary(group, paymentMovement: group.Key))
            .ToArray();
        var bucketMovements = contracts
            .GroupBy(x => new
            {
                Previous = x.PreviousClassification.Bucket,
                Current = x.CurrentClassification.Bucket,
                x.MovementCategory
            })
            .OrderBy(x => _policy.Rank(x.Key.Previous))
            .ThenBy(x => _policy.Rank(x.Key.Current))
            .ThenBy(x => x.Key.MovementCategory)
            .Select(group => MovementSummary(
                group,
                previousBucket: DebtLabels.Bucket(group.Key.Previous),
                currentBucket: DebtLabels.Bucket(group.Key.Current),
                category: group.Key.MovementCategory))
            .ToArray();

        return new DebtSegmentationAnalysis(
            previousPeriod,
            currentPeriod,
            contracts,
            bucketSummaries,
            bucketComparisons,
            paymentMovements,
            bucketMovements);
    }

    private DebtContractAnalysis AnalyzeContract(
        DebtContractHistory history,
        DateOnly previousPeriod,
        DateOnly currentPeriod)
    {
        var hasPreviousState = history.MonthlyStates.TryGetValue(previousPeriod, out var previousState);
        previousState ??= new DebtMonthlyState(previousPeriod, 0m, 0m, 0m, 0m);
        var currentState = history.MonthlyStates[currentPeriod];
        var contractStartPeriod = history.ContractDate.HasValue
            ? (DateOnly?)MonthStart(history.ContractDate.Value)
            : null;
        var isNewContract = !hasPreviousState || contractStartPeriod == currentPeriod;
        var previous = isNewContract
            ? new DebtClassification(
                DebtBucket.Normal, MonthlyPaymentStatus.NotDue,
                _policy.FirstDuePeriod(history.ContractDate), null, 0,
                LastPaymentPeriod(history, previousPeriod), DebtClassificationBasis.BeforeFirstDuePeriod)
            : Classify(history, previousPeriod);
        var current = Classify(history, currentPeriod);
        var paymentMovement = isNewContract
            ? "New Contract"
            : $"{DebtLabels.Payment(previous.LatestPaymentStatus)} -> {DebtLabels.Payment(current.LatestPaymentStatus)}";
        var category = MovementCategory(
            previous.Bucket, current.Bucket, previousState.TotalOutstanding,
            currentState.TotalOutstanding, isNewContract);
        return new DebtContractAnalysis(
            history, previousState, currentState, previous, current,
            paymentMovement, category, isNewContract);
    }

    private DebtMovementCategory MovementCategory(
        DebtBucket previous,
        DebtBucket current,
        decimal previousBalance,
        decimal currentBalance,
        bool isNewContract)
    {
        if (isNewContract)
            return DebtMovementCategory.NewContract;
        if (current == DebtBucket.PaidOff && previous != DebtBucket.PaidOff)
            return DebtMovementCategory.ClosedPaidOff;
        if (previous == DebtBucket.PaidOff && current != DebtBucket.PaidOff)
            return DebtMovementCategory.NewEnteredRisk;
        if (previous == DebtBucket.Normal && IsShortTermDelinquent(current))
            return DebtMovementCategory.NewDelinquency;
        if (previous != current)
            return _policy.Rank(current) < _policy.Rank(previous)
                ? DebtMovementCategory.Improved
                : DebtMovementCategory.Worsened;
        if (currentBalance < previousBalance)
            return DebtMovementCategory.SameBucketBalanceDecreased;
        if (currentBalance > previousBalance)
            return DebtMovementCategory.SameBucketBalanceIncreased;
        return DebtMovementCategory.SameBucketUnchanged;
    }

    private static DebtMovementSummary MovementSummary(
        IEnumerable<DebtContractAnalysis> source,
        string previousBucket = "",
        string currentBucket = "",
        string paymentMovement = "",
        DebtMovementCategory? category = null)
    {
        var items = source.ToArray();
        var previous = items.Sum(x => x.PreviousState.TotalOutstanding);
        var current = items.Sum(x => x.CurrentState.TotalOutstanding);
        return new DebtMovementSummary(
            previousBucket,
            currentBucket,
            paymentMovement,
            category,
            MemberCount(items),
            items.Length,
            previous,
            current,
            current - previous);
    }

    private (DateOnly? FirstMissed, int Count) CurrentMissedSequence(
        DebtContractHistory contract,
        DateOnly period)
    {
        var count = 0;
        DateOnly? first = null;
        var cursor = period;
        var firstDuePeriod = _policy.FirstDuePeriod(contract.ContractDate) ?? DateOnly.MinValue;
        while (cursor >= firstDuePeriod &&
               contract.MonthlyStates.TryGetValue(cursor, out var state) &&
               PaymentStatus(contract, state, cursor) == MonthlyPaymentStatus.NotPaid)
        {
            count++;
            first = cursor;
            cursor = cursor.AddMonths(-1);
        }

        return (first, count);
    }

    private static DateOnly? LastPaymentPeriod(DebtContractHistory contract, DateOnly period)
    {
        var contractStartPeriod = contract.ContractDate.HasValue
            ? MonthStart(contract.ContractDate.Value)
            : DateOnly.MinValue;
        return contract.MonthlyStates
            .Where(x => x.Key >= contractStartPeriod && x.Key <= period &&
                        x.Value.HasActualPayment)
            .Select(x => (DateOnly?)x.Key)
            .OrderByDescending(x => x)
            .FirstOrDefault();
    }

    private static int MemberCount(IEnumerable<DebtContractAnalysis> items) =>
        items.Select(x => x.Contract.MemberKey).Distinct(StringComparer.OrdinalIgnoreCase).Count();

    private MonthlyPaymentStatus PaymentStatus(
        DebtContractHistory contract,
        DebtMonthlyState state,
        DateOnly period)
    {
        if (state.TotalOutstanding <= 0m && !state.HasActualPayment)
            return MonthlyPaymentStatus.Closed;
        var firstDuePeriod = _policy.FirstDuePeriod(contract.ContractDate);
        if (firstDuePeriod.HasValue && period < firstDuePeriod.Value)
            return MonthlyPaymentStatus.NotDue;
        return state.HasActualPayment
            ? MonthlyPaymentStatus.Paid
            : MonthlyPaymentStatus.NotPaid;
    }

    private static bool IsShortTermDelinquent(DebtBucket bucket) => bucket is
        DebtBucket.OneMonthDelinquent or
        DebtBucket.TwoMonthsDelinquent or
        DebtBucket.ThreeToSixMonthsDelinquent or
        DebtBucket.SevenToTwelveMonthsDelinquent;

    private static DateOnly MonthStart(DateOnly value) => new(value.Year, value.Month, 1);
}
