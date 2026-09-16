using COOPAI.API.Services.DebtSegmentation;

namespace COOPAI.API.Tests;

public sealed class ContractSchedulePositionCalculatorTests
{
    [Fact]
    public void UsesAhAtFinalInsteadOfTotalInstallmentsTimesMonthlyInstallment()
    {
        var firstDue = new DateOnly(2026, 1, 1);

        var beforeFinal = ContractSchedulePositionCalculator.Calculate(
            firstDue, new DateOnly(2026, 11, 1), 1_205m, 12, 100m, 105m);
        var atFinal = ContractSchedulePositionCalculator.Calculate(
            firstDue, new DateOnly(2026, 12, 1), 1_205m, 12, 100m, 0m);

        Assert.Equal(105m, beforeFinal.FinalInstallment);
        Assert.Equal(1_100m, beforeFinal.ExpectedDue);
        Assert.Equal(1_205m, atFinal.ExpectedDue);
        Assert.Equal(ContractSchedulePositions.Current, atFinal.Position);
    }

    [Fact]
    public void ClassifiesAheadCurrentAndBehindFromAuthoritativeBalance()
    {
        var firstDue = new DateOnly(2026, 1, 1);

        Assert.Equal(ContractSchedulePositions.Behind,
            Position(ending: 800m).Position);
        Assert.Equal(ContractSchedulePositions.Current,
            Position(ending: 700m).Position);
        Assert.Equal(ContractSchedulePositions.Ahead,
            Position(ending: 650m).Position);

        ContractSchedulePosition Position(decimal ending) =>
            ContractSchedulePositionCalculator.Calculate(
                firstDue, new DateOnly(2026, 5, 1), 1_200m, 12, 100m, ending);
    }

    [Fact]
    public void DueCountClampsBeforeFirstDueAndAfterFinalInstallment()
    {
        var firstDue = new DateOnly(2026, 3, 1);

        Assert.Equal(0, ContractSchedulePositionCalculator.DueCount(
            firstDue, new DateOnly(2026, 2, 1), 3));
        Assert.Equal(1, ContractSchedulePositionCalculator.DueCount(
            firstDue, new DateOnly(2026, 3, 1), 3));
        Assert.Equal(3, ContractSchedulePositionCalculator.DueCount(
            firstDue, new DateOnly(2027, 3, 1), 3));
    }
}
