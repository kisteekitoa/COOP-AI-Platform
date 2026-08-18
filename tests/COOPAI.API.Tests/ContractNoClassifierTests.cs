using COOPAI.API.Services.Import;

namespace COOPAI.API.Tests;

public class ContractNoClassifierTests
{
    private readonly ContractNoClassifier _classifier = new();

    [Theory]
    [InlineData("\u0E2A\u0E1B-2569-000001", "\u0E2A\u0E1B", true)]
    [InlineData("\u0E2A\u0E09-2569-000001", "\u0E2A\u0E09", true)]
    [InlineData("\u0E2A\u0E2B-2569-000001", "\u0E2A\u0E2B", true)]
    [InlineData("\u0E2A\u0E17-2569-000001", "\u0E2A\u0E17", true)]
    [InlineData("\u0E2A\u0E28-2569-000001", "\u0E2A\u0E28", false)]
    [InlineData("\u0E2A\u0E08-2569-000001", "\u0E2A\u0E08", true)]
    public void Classify_SupportedPrefix_ReturnsClassification(string contractNo, string code, bool hasProfit)
    {
        var result = _classifier.Classify(contractNo);

        Assert.True(result.IsValid);
        Assert.Equal(contractNo, result.ContractNo);
        Assert.Equal($"{code}-", result.Prefix);
        Assert.Equal(2569, result.ContractYear);
        Assert.Equal("000001", result.RunningNumber);
        Assert.Equal(code, result.ContractTypeCode);
        Assert.Equal($"Type {code}", result.ContractTypeName);
        Assert.Equal(hasProfit, result.HasProfit);
        Assert.Empty(result.ErrorType);
        Assert.Empty(result.ErrorMessage);
    }

    [Fact]
    public void Classify_RealInterestBearingContract_PreservesOriginalContractNoAndLeadingZeros()
    {
        const string contractNo = "\u0E2A\u0E2B-2569-000600";
        var result = _classifier.Classify(contractNo);

        Assert.True(result.IsValid);
        Assert.Equal(contractNo, result.ContractNo);
        Assert.Equal("\u0E2A\u0E2B-", result.Prefix);
        Assert.Equal(2569, result.ContractYear);
        Assert.Equal("000600", result.RunningNumber);
        Assert.Equal("\u0E2A\u0E2B", result.ContractTypeCode);
        Assert.True(result.HasProfit);
    }

    [Fact]
    public void Classify_RealNoProfitContract_ReturnsNoProfitClassification()
    {
        const string contractNo = "\u0E2A\u0E28-2569-000001";
        var result = _classifier.Classify(contractNo);

        Assert.True(result.IsValid);
        Assert.Equal(contractNo, result.ContractNo);
        Assert.Equal("\u0E2A\u0E28", result.ContractTypeCode);
        Assert.False(result.HasProfit);
    }

    [Theory]
    [InlineData("\u0E2A\u0E21-2534-000005", "\u0E2A\u0E21", "\u0E2A\u0E34\u0E19\u0E40\u0E0A\u0E37\u0E48\u0E2D\u0E2A\u0E32\u0E21\u0E31\u0E0D\u0E17\u0E31\u0E48\u0E27\u0E44\u0E1B (\u0E23\u0E38\u0E48\u0E19\u0E40\u0E01\u0E48\u0E32)")]
    [InlineData("\u0E2A\u0E2D-2566-000415", "\u0E2A\u0E2D", "\u0E2A\u0E34\u0E19\u0E40\u0E0A\u0E37\u0E48\u0E2D\u0E2E\u0E31\u0E08\u0E22\u0E4C\u0E41\u0E25\u0E30\u0E2D\u0E38\u0E21\u0E40\u0E23\u0E32\u0E30\u0E2B\u0E4C")]
    public void Classify_ApprovedNewPrefix_ReturnsBusinessClassification(string contractNo, string code, string name)
    {
        var result = _classifier.Classify(contractNo);

        Assert.True(result.IsValid);
        Assert.Equal(code, result.ContractTypeCode);
        Assert.Equal(name, result.ContractTypeName);
        Assert.True(result.HasProfit);
    }

    [Theory]
    [InlineData(null, "MissingContractNo")]
    [InlineData("", "MissingContractNo")]
    [InlineData("   ", "MissingContractNo")]
    [InlineData("ABC", "InvalidContractNo")]
    [InlineData("\u0E2A\u0E2B2569-000001", "InvalidContractNo")]
    [InlineData("\u0E2A\u0E2B-2569", "InvalidContractNo")]
    [InlineData("\u0E2A\u0E2B-", "InvalidContractNo")]
    [InlineData("\u0E2A\u0E2B -2569-000001", "InvalidContractNo")]
    [InlineData("XX-2569-000001", "UnknownContractType")]
    [InlineData("\u0E2A\u0E2B-256-000001", "InvalidContractYear")]
    [InlineData("\u0E2A\u0E2B-256A-000001", "InvalidContractYear")]
    [InlineData("\u0E2A\u0E2B-+2569-000001", "InvalidContractYear")]
    [InlineData("\u0E2A\u0E2B--2569-000001", "InvalidContractNo")]
    [InlineData("\u0E2A\u0E2B-2569-ABC", "InvalidRunningNumber")]
    [InlineData("\u0E2A\u0E2B-2569-+000001", "InvalidRunningNumber")]
    [InlineData("\u0E2A\u0E2B-2569-00 001", "InvalidContractNo")]
    public void Classify_InvalidContractNo_ReturnsSpecificError(string? contractNo, string errorType)
    {
        var result = _classifier.Classify(contractNo);

        Assert.False(result.IsValid);
        Assert.Equal(contractNo ?? string.Empty, result.ContractNo);
        Assert.Equal(errorType, result.ErrorType);
        Assert.NotEmpty(result.ErrorMessage);
    }
}
