using COOPAI.API.Models.Import;

namespace COOPAI.API.Services.Import;

public class ValidationResult
{
    public bool IsValid { get; set; }

    public List<string> Errors { get; set; } = new();
}

public class ImportValidator
{
    public ValidationResult Validate(ImportLoanRecord record)
    {
        var result = new ValidationResult();

        ValidateRequired(record.MemberNo, "MemberNo", result.Errors);
        ValidateRequired(record.MemberName, "MemberName", result.Errors);
        ValidateRequired(record.ContractNo, "ContractNo", result.Errors);

        ValidateNonNegative(record.LoanAmount, "LoanAmount", result.Errors);
        ValidateNonNegative(record.PrincipalBalance, "PrincipalBalance", result.Errors);
        ValidateNonNegative(record.ProfitBalance, "ProfitBalance", result.Errors);
        ValidateNonNegative(record.TotalBalance, "TotalBalance", result.Errors);
        ValidateNonNegative(record.OverdueDays, "OverdueDays", result.Errors);

        result.IsValid = result.Errors.Count == 0;
        return result;
    }

    private static void ValidateRequired(string? value, string fieldName, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{fieldName} is required.");
        }
    }

    private static void ValidateNonNegative(decimal value, string fieldName, List<string> errors)
    {
        if (value < 0)
        {
            errors.Add($"{fieldName} must be greater than or equal to 0.");
        }
    }

    private static void ValidateNonNegative(int value, string fieldName, List<string> errors)
    {
        if (value < 0)
        {
            errors.Add($"{fieldName} must be greater than or equal to 0.");
        }
    }
}
