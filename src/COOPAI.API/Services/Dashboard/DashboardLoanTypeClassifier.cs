using System.Globalization;
using COOPAI.API.Services.Import;

namespace COOPAI.API.Services.Dashboard;

internal sealed class DashboardLoanTypeClassifier
{
    public const string UnknownPrefix = "Unknown";
    public const string UnknownTypeName = "ไม่ทราบประเภท";

    private static readonly IReadOnlyDictionary<string, string> ApprovedLoanTypes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["สม"] = "สินเชื่อสามัญทั่วไป (รุ่นเก่า)",
            ["สจ"] = "สินเชื่อสามัญเพื่อรถจักรยานยนต์",
            ["สท"] = "สินเชื่อสามัญประกันด้วยอสังหาริมทรัพย์",
            ["สป"] = "สินเชื่อสามัญประกันด้วยเงินฝาก,สมาชิก,อสังหาริมทรัพย์",
            ["สห"] = "สินเชื่อสามัญประกันด้วยสมาชิก วงเงิน 100% ของทุนเรือนหุ้น",
            ["สอ"] = "สินเชื่อฮัจย์และอุมเราะห์",
            ["สศ"] = "สินเชื่อสามัญเพื่อเหตุจำเป็น",
            ["สย"] = "สินเชื่อสามัญเพื่อผู้ประกอบการรายย่อย"
        };

    private readonly ContractNoClassifier _sharedClassifier = new();

    public DashboardLoanTypeClassification Classify(string? contractNo)
    {
        var normalizedContractNo = contractNo?.Trim();
        var sharedClassification = _sharedClassifier.Classify(normalizedContractNo);
        if (sharedClassification.IsValid &&
            ApprovedLoanTypes.TryGetValue(sharedClassification.ContractTypeCode, out var approvedName))
        {
            return new DashboardLoanTypeClassification(
                sharedClassification.ContractTypeCode,
                approvedName);
        }

        if (TryClassifyDashboardOnlyPrefix(normalizedContractNo, out var prefix) &&
            ApprovedLoanTypes.TryGetValue(prefix, out approvedName))
        {
            return new DashboardLoanTypeClassification(prefix, approvedName);
        }

        return new DashboardLoanTypeClassification(UnknownPrefix, UnknownTypeName);
    }

    public DashboardLoanTypeClassification ClassifyPrefix(string? prefix) =>
        prefix is not null && ApprovedLoanTypes.TryGetValue(prefix, out var approvedName)
            ? new DashboardLoanTypeClassification(prefix, approvedName)
            : new DashboardLoanTypeClassification(UnknownPrefix, UnknownTypeName);

    private static bool TryClassifyDashboardOnlyPrefix(string? contractNo, out string prefix)
    {
        prefix = string.Empty;
        if (string.IsNullOrWhiteSpace(contractNo) || contractNo.Any(char.IsWhiteSpace))
            return false;

        var segments = contractNo.Split('-', StringSplitOptions.None);
        if (segments.Length != 3 || segments.Any(string.IsNullOrEmpty))
            return false;

        if (!ApprovedLoanTypes.ContainsKey(segments[0]))
            return false;

        if (!IsAsciiDigits(segments[1], 4) ||
            !int.TryParse(segments[1], NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            return false;
        }

        if (!IsAsciiDigits(segments[2]) ||
            !int.TryParse(segments[2], NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            return false;
        }

        prefix = segments[0];
        return true;
    }

    private static bool IsAsciiDigits(string value, int? exactLength = null)
    {
        if (value.Length == 0 || (exactLength.HasValue && value.Length != exactLength.Value))
            return false;

        return value.All(character => character is >= '0' and <= '9');
    }
}

internal readonly record struct DashboardLoanTypeClassification(string Prefix, string Name);
