using COOPAI.API.Models.Import;
using COOPAI.API.Services.Import;
using Xunit;

namespace COOPAI.API.Tests;

public class ImportValidatorTests
{
    [Fact]
    public void Validate_ValidRecord_ReturnsValidResult()
    {
        var validator = new ImportValidator();
        var record = new ImportLoanRecord
        {
            MemberNo = "M001",
            MemberName = "Jane Doe",
            ContractNo = "C001",
            LoanAmount = 1000m,
            PrincipalBalance = 500m,
            ProfitBalance = 50m,
            TotalBalance = 550m,
            OverdueDays = 0
        };

        var result = validator.Validate(record);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_InvalidRecord_ReturnsErrors()
    {
        var validator = new ImportValidator();
        var record = new ImportLoanRecord
        {
            MemberNo = string.Empty,
            MemberName = string.Empty,
            ContractNo = string.Empty,
            LoanAmount = -1m,
            PrincipalBalance = -2m,
            ProfitBalance = -3m,
            TotalBalance = -4m,
            OverdueDays = -1
        };

        var result = validator.Validate(record);

        Assert.False(result.IsValid);
        Assert.Contains("MemberNo is required.", result.Errors);
        Assert.Contains("MemberName is required.", result.Errors);
        Assert.Contains("ContractNo is required.", result.Errors);
        Assert.Contains("LoanAmount must be greater than or equal to 0.", result.Errors);
        Assert.Contains("PrincipalBalance must be greater than or equal to 0.", result.Errors);
        Assert.Contains("ProfitBalance must be greater than or equal to 0.", result.Errors);
        Assert.Contains("TotalBalance must be greater than or equal to 0.", result.Errors);
        Assert.Contains("OverdueDays must be greater than or equal to 0.", result.Errors);
    }
}
