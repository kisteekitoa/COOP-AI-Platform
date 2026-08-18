using System.Globalization;

namespace COOPAI.API.Services.Import;

public sealed class ContractNoClassifier
{
    private static readonly IReadOnlyDictionary<string, ContractTypeDefinition> ContractTypes =
        new Dictionary<string, ContractTypeDefinition>(StringComparer.Ordinal)
        {
            ["\u0E2A\u0E1B"] = new("Type \u0E2A\u0E1B", true),
            ["\u0E2A\u0E09"] = new("Type \u0E2A\u0E09", true),
            ["\u0E2A\u0E2B"] = new("Type \u0E2A\u0E2B", true),
            ["\u0E2A\u0E17"] = new("Type \u0E2A\u0E17", true),
            ["\u0E2A\u0E28"] = new("Type \u0E2A\u0E28", false),
            ["\u0E2A\u0E08"] = new("Type \u0E2A\u0E08", true),
            ["\u0E2A\u0E21"] = new("\u0E2A\u0E34\u0E19\u0E40\u0E0A\u0E37\u0E48\u0E2D\u0E2A\u0E32\u0E21\u0E31\u0E0D\u0E17\u0E31\u0E48\u0E27\u0E44\u0E1B (\u0E23\u0E38\u0E48\u0E19\u0E40\u0E01\u0E48\u0E32)", true),
            ["\u0E2A\u0E2D"] = new("\u0E2A\u0E34\u0E19\u0E40\u0E0A\u0E37\u0E48\u0E2D\u0E2E\u0E31\u0E08\u0E22\u0E4C\u0E41\u0E25\u0E30\u0E2D\u0E38\u0E21\u0E40\u0E23\u0E32\u0E30\u0E2B\u0E4C", true)
        };

    public ContractClassificationResult Classify(string? contractNo)
    {
        if (string.IsNullOrWhiteSpace(contractNo))
            return Invalid(contractNo, "MissingContractNo", "ContractNo is required.");

        if (contractNo.Any(char.IsWhiteSpace))
            return Invalid(contractNo, "InvalidContractNo", "ContractNo must not contain whitespace.");

        var segments = contractNo.Split('-', StringSplitOptions.None);
        if (segments.Length != 3 || segments.Any(string.IsNullOrEmpty))
            return Invalid(contractNo, "InvalidContractNo", "ContractNo must have the format PREFIX-YYYY-RUNNING.");

        var prefixCode = segments[0];
        if (!ContractTypes.TryGetValue(prefixCode, out var contractType))
            return Invalid(contractNo, "UnknownContractType", $"ContractNo prefix '{prefixCode}-' is not supported.");

        if (!IsAsciiDigits(segments[1], 4) || !int.TryParse(segments[1], NumberStyles.None, CultureInfo.InvariantCulture, out var contractYear))
            return Invalid(contractNo, "InvalidContractYear", $"Contract year '{segments[1]}' is not a four-digit integer.");

        if (!IsAsciiDigits(segments[2]) || !int.TryParse(segments[2], NumberStyles.None, CultureInfo.InvariantCulture, out _))
            return Invalid(contractNo, "InvalidRunningNumber", $"Running number '{segments[2]}' is not a valid integer.");

        return new ContractClassificationResult
        {
            IsValid = true,
            ContractNo = contractNo,
            Prefix = $"{prefixCode}-",
            ContractYear = contractYear,
            RunningNumber = segments[2],
            ContractTypeCode = prefixCode,
            ContractTypeName = contractType.Name,
            HasProfit = contractType.HasProfit
        };
    }

    private static bool IsAsciiDigits(string value, int? exactLength = null)
    {
        if (value.Length == 0 || (exactLength.HasValue && value.Length != exactLength.Value))
            return false;

        return value.All(character => character is >= '0' and <= '9');
    }

    private static ContractClassificationResult Invalid(string? contractNo, string errorType, string errorMessage) => new()
    {
        ContractNo = contractNo ?? string.Empty,
        IsValid = false,
        ErrorType = errorType,
        ErrorMessage = errorMessage
    };

    private sealed record ContractTypeDefinition(string Name, bool HasProfit);
}
