using COOPAI.API.Services.DebtSegmentation;

namespace COOPAI.API.Tests;

public sealed class ArrearsAgeV1Tests
{
    [Fact]
    public void InTerm_AllDueInstallmentsSatisfied_IsZeroEvenWithFutureBalance()
    {
        var contract = Make(
            currentPeriod: new DateOnly(2026, 4, 1),
            firstDue: new DateOnly(2026, 1, 1),
            endingOutstanding: 8000m,
            totalInstallments: 12,
            monthlyInstallment: 1000m,
            obligation: 12000m);

        Assert.Equal(0, WorkQueueService.CurrentDelinquencyMonths(contract));
    }

    [Fact]
    public void OldestUnpaidJanuary_AsOfMarch_IsThreeMonths()
    {
        // Due Jan-Mar = 3,000. Only 0 has been satisfied, so January is oldest unpaid.
        var contract = Make(
            currentPeriod: new DateOnly(2026, 3, 1),
            firstDue: new DateOnly(2026, 1, 1),
            endingOutstanding: 12000m,
            totalInstallments: 12,
            monthlyInstallment: 1000m,
            obligation: 12000m);

        Assert.Equal(3, WorkQueueService.CurrentDelinquencyMonths(contract));
    }

    [Fact]
    public void PaymentClosesJanuary_OldestUnpaidFebruary_AsOfMarch_IsTwoMonths()
    {
        // 1,000 satisfied oldest-first => Jan closed; Feb is oldest unpaid.
        var contract = Make(
            currentPeriod: new DateOnly(2026, 3, 1),
            firstDue: new DateOnly(2026, 1, 1),
            endingOutstanding: 11000m,
            totalInstallments: 12,
            monthlyInstallment: 1000m,
            obligation: 12000m);

        Assert.Equal(2, WorkQueueService.CurrentDelinquencyMonths(contract));
    }

    [Fact]
    public void PartialPaymentDoesNotCloseOldestInstallment()
    {
        // Only 500 of January's 1,000 is satisfied. January remains oldest unpaid.
        var contract = Make(
            currentPeriod: new DateOnly(2026, 3, 1),
            firstDue: new DateOnly(2026, 1, 1),
            endingOutstanding: 11500m,
            totalInstallments: 12,
            monthlyInstallment: 1000m,
            obligation: 12000m);

        Assert.Equal(3, WorkQueueService.CurrentDelinquencyMonths(contract));
    }

    [Fact]
    public void PostExpiryPaymentsMoveOldestUnpaidInstallmentForward()
    {
        // 12-month schedule Jan-Dec 2025, analyzed Aug 2026.
        // With 10 installments fully satisfied, Nov 2025 is oldest unpaid.
        // Nov 2025 through Aug 2026 inclusive = 10 months.
        var contract = Make(
            currentPeriod: new DateOnly(2026, 8, 1),
            firstDue: new DateOnly(2025, 1, 1),
            endingOutstanding: 2000m,
            totalInstallments: 12,
            monthlyInstallment: 1000m,
            obligation: 12000m,
            status: "Expired",
            expireDate: new DateOnly(2025, 12, 1),
            remainingOrOverdueMonths: -8);

        Assert.Equal(10, WorkQueueService.CurrentDelinquencyMonths(contract));
        Assert.Equal(8, WorkQueueService.ExpiredAgeMonths(contract));
    }

    [Fact]
    public void ExpiredContract_AllScheduledInstallmentsSatisfied_IsZeroArrearsAge()
    {
        var contract = Make(
            currentPeriod: new DateOnly(2026, 8, 1),
            firstDue: new DateOnly(2025, 1, 1),
            endingOutstanding: 0m,
            totalInstallments: 12,
            monthlyInstallment: 1000m,
            obligation: 12000m,
            status: "PaidOff",
            expireDate: new DateOnly(2025, 12, 1),
            remainingOrOverdueMonths: -8);

        Assert.Equal(0, WorkQueueService.CurrentDelinquencyMonths(contract));
    }

    private static MonthlyAmountDueContract Make(
        DateOnly currentPeriod,
        DateOnly firstDue,
        decimal endingOutstanding,
        int totalInstallments,
        decimal monthlyInstallment,
        decimal obligation,
        string status = "Active/InTerm",
        DateOnly? expireDate = null,
        int? remainingOrOverdueMonths = null)
    {
        return new MonthlyAmountDueContract(
            "TEST-ARREARS",
            "legacy",
            status,
            currentPeriod,
            firstDue,
            firstDue.AddMonths(-1),
            expireDate,
            remainingOrOverdueMonths,
            endingOutstanding,
            0m,
            endingOutstanding,
            Math.Min(totalInstallments,
                ((currentPeriod.Year - firstDue.Year) * 12) + currentPeriod.Month - firstDue.Month + 1),
            totalInstallments,
            monthlyInstallment,
            monthlyInstallment,
            monthlyInstallment,
            0m,
            0m,
            "ActiveInstallment",
            "Available",
            [])
        {
            ContractualObligation = obligation,
            FinalInstallment = monthlyInstallment
        };
    }
}