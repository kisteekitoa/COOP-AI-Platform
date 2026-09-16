using COOPAI.API.Services.DebtSegmentation;

namespace COOPAI.API.Tests;

public sealed class DebtStatusModelV2Tests
{
    [Theory]
    [InlineData(0, "สถานะขาดชำระ: ปกติ")]
    [InlineData(1, "ขาดชำระ 1 เดือน")]
    [InlineData(2, "ขาดชำระ 2 เดือน")]
    [InlineData(3, "ขาดชำระ 3–6 เดือน")]
    [InlineData(6, "ขาดชำระ 3–6 เดือน")]
    [InlineData(7, "ขาดชำระ 7–12 เดือน")]
    [InlineData(12, "ขาดชำระ 7–12 เดือน")]
    [InlineData(13, "ขาดชำระเกิน 1 ปี แต่ไม่เกิน 3 ปี")]
    [InlineData(37, "ขาดชำระเกิน 3 ปี แต่ไม่เกิน 5 ปี")]
    [InlineData(61, "ขาดชำระเกิน 5 ปี แต่ไม่เกิน 10 ปี")]
    [InlineData(120, "ขาดชำระ 10 ปีขึ้นไป")]
    public void CurrentDelinquencyLabel_FollowsLockedBusinessTerms(int months, string expected)
    {
        Assert.Equal(expected, WorkQueueService.CurrentDelinquencyLabelThai(months));
    }

    [Theory]
    [InlineData(1, "เลยสัญญา 1 เดือน")]
    [InlineData(2, "เลยสัญญา 2 เดือน")]
    [InlineData(3, "เลยสัญญา 3–6 เดือน")]
    [InlineData(6, "เลยสัญญา 3–6 เดือน")]
    [InlineData(7, "เลยสัญญา 7–12 เดือน")]
    [InlineData(11, "เลยสัญญา 7–12 เดือน")]
    [InlineData(12, "เลยสัญญา 7–12 เดือน")]
    [InlineData(13, "เลยสัญญา 1–3 ปี")]
    [InlineData(37, "เลยสัญญา 3–5 ปี")]
    [InlineData(61, "เลยสัญญา 5–10 ปี")]
    [InlineData(120, "เลยสัญญา 10 ปีขึ้นไป")]
    public void ExpiredAgeLabel_FollowsLockedBusinessTerms(int months, string expected)
    {
        Assert.Equal(expected, WorkQueueService.ExpiredAgeLabelThai(months));
    }

    [Fact]
    public void CurrentDelinquency_UsesEndingOutstanding_AfterPostExpiryPayments()
    {
        // 36 installments x 1,600 = 57,600.
        // If 52,800 has been satisfied after all payments, 4,800 remains:
        // exactly 3 monthly installments remain delinquent.
        var contract = new MonthlyAmountDueContract(
            "TEST-001",
            "legacy",
            "Expired",
            new DateOnly(2026, 8, 1),
            new DateOnly(2023, 9, 1),
            new DateOnly(2023, 8, 1),
            new DateOnly(2025, 9, 1),
            -11,
            6400m,
            1600m,
            4800m,
            36,
            36,
            1600m,
            6400m,
            4800m,
            0m,
            0m,
            "ExpiredFullBalance",
            "Available",
            [])
        {
            ContractualObligation = 57600m,
            EndingArrears = 4800m
        };

        Assert.Equal(3, WorkQueueService.CurrentDelinquencyMonths(contract));
        Assert.Equal(11, WorkQueueService.ExpiredAgeMonths(contract));
    }

    [Fact]
    public void PostExpiryPayment_ReducesCurrentDelinquencyMonths()
    {
        var beforePayment = MakeExpired(endingOutstanding: 8000m);
        var afterPayment = MakeExpired(endingOutstanding: 4800m);

        Assert.Equal(5, WorkQueueService.CurrentDelinquencyMonths(beforePayment));
        Assert.Equal(3, WorkQueueService.CurrentDelinquencyMonths(afterPayment));
    }

    private static MonthlyAmountDueContract MakeExpired(decimal endingOutstanding) =>
        new(
            "TEST-002",
            "legacy",
            "Expired",
            new DateOnly(2026, 8, 1),
            new DateOnly(2023, 9, 1),
            new DateOnly(2023, 8, 1),
            new DateOnly(2025, 9, 1),
            -11,
            endingOutstanding + 1600m,
            1600m,
            endingOutstanding,
            36,
            36,
            1600m,
            endingOutstanding + 1600m,
            endingOutstanding,
            0m,
            0m,
            "ExpiredFullBalance",
            "Available",
            [])
        {
            ContractualObligation = 57600m,
            EndingArrears = endingOutstanding
        };
}