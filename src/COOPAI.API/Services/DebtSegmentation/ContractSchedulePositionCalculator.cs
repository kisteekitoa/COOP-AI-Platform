namespace COOPAI.API.Services.DebtSegmentation;

public static class ContractSchedulePositionCalculator
{
    public const decimal Tolerance = 0.01m;

    public static int DueCount(DateOnly firstDuePeriod, DateOnly period, int totalInstallments)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(totalInstallments);
        if (period < firstDuePeriod)
            return 0;

        var elapsed = ((period.Year - firstDuePeriod.Year) * 12) +
            period.Month - firstDuePeriod.Month + 1;
        return Math.Clamp(elapsed, 0, totalInstallments);
    }

    public static decimal FinalInstallment(
        decimal contractualObligation,
        int totalInstallments,
        decimal monthlyInstallment) =>
        contractualObligation - (monthlyInstallment * (totalInstallments - 1));

    public static decimal ExpectedDue(
        DateOnly firstDuePeriod,
        DateOnly period,
        decimal contractualObligation,
        int totalInstallments,
        decimal monthlyInstallment)
    {
        var dueCount = DueCount(firstDuePeriod, period, totalInstallments);
        if (dueCount == 0)
            return 0m;
        return dueCount == totalInstallments
            ? contractualObligation
            : dueCount * monthlyInstallment;
    }

    public static ContractSchedulePosition Calculate(
        DateOnly firstDuePeriod,
        DateOnly period,
        decimal contractualObligation,
        int totalInstallments,
        decimal monthlyInstallment,
        decimal endingOutstanding)
    {
        var finalInstallment = FinalInstallment(
            contractualObligation, totalInstallments, monthlyInstallment);
        var expectedDue = ExpectedDue(
            firstDuePeriod, period, contractualObligation, totalInstallments, monthlyInstallment);
        var satisfied = contractualObligation - endingOutstanding;
        var positionAmount = expectedDue - satisfied;
        var position = Math.Abs(positionAmount) <= Tolerance
            ? ContractSchedulePositions.Current
            : positionAmount > 0m
                ? ContractSchedulePositions.Behind
                : ContractSchedulePositions.Ahead;
        return new ContractSchedulePosition(
            DueCount(firstDuePeriod, period, totalInstallments),
            finalInstallment,
            expectedDue,
            satisfied,
            positionAmount,
            position,
            Math.Max(positionAmount, 0m));
    }
}

public static class ContractSchedulePositions
{
    public const string Ahead = "Ahead";
    public const string Current = "Current";
    public const string Behind = "Behind";
}

public sealed record ContractSchedulePosition(
    int DueCount,
    decimal FinalInstallment,
    decimal ExpectedDue,
    decimal Satisfied,
    decimal PositionAmount,
    string Position,
    decimal OverdueContractualAmount);
