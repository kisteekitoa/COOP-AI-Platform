using System.Globalization;
using System.Text;

namespace COOPAI.API.Services.PortfolioSnapshots;

public sealed class SnapshotContractNoNormalizer
{
    private static readonly HashSet<string> ApprovedPrefixes = new(StringComparer.Ordinal)
    {
        "\u0E2A\u0E21", // สม
        "\u0E2A\u0E08", // สจ
        "\u0E2A\u0E17", // สท
        "\u0E2A\u0E1B", // สป
        "\u0E2A\u0E2B", // สห
        "\u0E2A\u0E2D", // สอ
        "\u0E2A\u0E28", // สศ
        "\u0E2A\u0E22"  // สย
    };

    private static readonly HashSet<char> HyphenVariants =
    [
        '\u2010', '\u2011', '\u2012', '\u2013', '\u2014', '\u2212', '\uFE63', '\uFF0D'
    ];

    public ContractNoNormalizationResult Normalize(string? contractNo)
    {
        if (string.IsNullOrWhiteSpace(contractNo))
            return ContractNoNormalizationResult.Invalid("MissingContractNo");

        var canonical = new string(contractNo
                .Trim()
                .Normalize(NormalizationForm.FormC)
                .Select(character => HyphenVariants.Contains(character) ? '-' : character)
                .ToArray());

        if (canonical.Any(char.IsWhiteSpace))
            return ContractNoNormalizationResult.Invalid("InvalidContractNo");

        var segments = canonical.Split('-', StringSplitOptions.None);
        if (segments.Length != 3 || segments.Any(string.IsNullOrEmpty))
            return ContractNoNormalizationResult.Invalid("InvalidContractNo");

        if (!ApprovedPrefixes.Contains(segments[0]))
            return ContractNoNormalizationResult.Invalid("UnsupportedContractType");

        if (!IsAsciiDigits(segments[1], 4) ||
            !int.TryParse(segments[1], NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            return ContractNoNormalizationResult.Invalid("InvalidContractYear");
        }

        if (!IsAsciiDigits(segments[2]) ||
            !int.TryParse(segments[2], NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            return ContractNoNormalizationResult.Invalid("InvalidRunningNumber");
        }

        return new ContractNoNormalizationResult(true, canonical, segments[0], string.Empty);
    }

    private static bool IsAsciiDigits(string value, int? exactLength = null)
    {
        if (value.Length == 0 || (exactLength.HasValue && value.Length != exactLength.Value))
            return false;

        return value.All(character => character is >= '0' and <= '9');
    }
}

public readonly record struct ContractNoNormalizationResult(
    bool IsValid,
    string NormalizedContractNo,
    string LoanTypePrefix,
    string ErrorCode)
{
    public static ContractNoNormalizationResult Invalid(string errorCode) =>
        new(false, string.Empty, string.Empty, errorCode);
}
