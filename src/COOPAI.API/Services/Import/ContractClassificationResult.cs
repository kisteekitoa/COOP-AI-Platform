namespace COOPAI.API.Services.Import;

public sealed class ContractClassificationResult
{
    public string ContractNo { get; init; } = string.Empty;
    public bool IsValid { get; init; }
    public string Prefix { get; init; } = string.Empty;
    public int ContractYear { get; init; }
    public string RunningNumber { get; init; } = string.Empty;
    public string ContractTypeCode { get; init; } = string.Empty;
    public string ContractTypeName { get; init; } = string.Empty;
    public bool HasProfit { get; init; }
    public string ErrorType { get; init; } = string.Empty;
    public string ErrorMessage { get; init; } = string.Empty;
}
