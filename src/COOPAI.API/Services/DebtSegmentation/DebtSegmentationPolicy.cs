namespace COOPAI.API.Services.DebtSegmentation;

public interface IDebtSegmentationPolicy
{
    string Version { get; }
    IReadOnlyList<DebtBucket> OrderedBuckets { get; }
    DateOnly? FirstDuePeriod(DateOnly? contractDate);
    DebtBucket ClassifyConsecutiveMisses(int missedMonths);
    DebtBucket? ClassifyExpiredDebt(DateOnly? expireDate, DateOnly asOfDate);
    int Rank(DebtBucket bucket);
}

public sealed class DebtSegmentationPolicyV1 : IDebtSegmentationPolicy
{
    private static readonly DebtBucket[] Order =
    [
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
    ];

    public string Version => "debt-segmentation-v1";

    public IReadOnlyList<DebtBucket> OrderedBuckets => Order;

    public DateOnly? FirstDuePeriod(DateOnly? contractDate) => contractDate.HasValue
        ? new DateOnly(contractDate.Value.Year, contractDate.Value.Month, 1).AddMonths(1)
        : null;

    public DebtBucket ClassifyConsecutiveMisses(int missedMonths) => missedMonths switch
    {
        <= 1 => DebtBucket.OneMonthDelinquent,
        2 => DebtBucket.TwoMonthsDelinquent,
        <= 6 => DebtBucket.ThreeToSixMonthsDelinquent,
        _ => DebtBucket.SevenToTwelveMonthsDelinquent
    };

    public DebtBucket? ClassifyExpiredDebt(DateOnly? expireDate, DateOnly asOfDate)
    {
        if (!expireDate.HasValue || asOfDate <= expireDate.Value.AddYears(1))
            return null;
        if (asOfDate >= expireDate.Value.AddYears(10))
            return DebtBucket.TenYearsAndAbove;
        if (asOfDate > expireDate.Value.AddYears(5))
            return DebtBucket.OverFiveToTenYears;
        if (asOfDate > expireDate.Value.AddYears(3))
            return DebtBucket.OverThreeToFiveYears;
        return DebtBucket.OverOneToThreeYears;
    }

    public int Rank(DebtBucket bucket) => bucket == DebtBucket.PaidOff
        ? -1
        : Array.IndexOf(Order, bucket);
}
