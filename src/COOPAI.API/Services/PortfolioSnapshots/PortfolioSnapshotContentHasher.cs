using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using COOPAI.API.Models.Portfolio;

namespace COOPAI.API.Services.PortfolioSnapshots;

public sealed class PortfolioSnapshotContentHasher
{
    public string Compute(PortfolioSnapshot snapshot)
    {
        var content = new StringBuilder();
        content.Append("definition=").Append(snapshot.DefinitionVersion).Append('\n');
        content.Append("asOf=").Append(snapshot.AsOfDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('\n');
        content.Append("currency=").Append(snapshot.CurrencyCode).Append('\n');

        foreach (var record in snapshot.Records
                     .OrderBy(x => x.NormalizedContractNo, StringComparer.Ordinal))
        {
            AppendField(content, record.NormalizedContractNo);
            AppendField(content, record.ContractDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty);
            AppendField(content, record.ExpireDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty);
            AppendField(content, record.SourceRowKind.ToString());
            AppendField(content, record.OpeningSide.ToString());
            AppendField(content, record.TermStatus.ToString());
            AppendField(content, record.BalanceStatus.ToString());
            AppendMoney(content, record.PrincipalOpening);
            AppendMoney(content, record.ProfitOpening);
            AppendMoney(content, record.TotalOpening);
            AppendMoney(content, record.PrincipalRepayment);
            AppendMoney(content, record.ProfitRepayment);
            AppendMoney(content, record.TotalRepayment);
            AppendMoney(content, record.PrincipalOutstanding);
            AppendMoney(content, record.ProfitOutstanding);
            AppendMoney(content, record.TotalOutstanding);
            AppendField(content, CanonicalizeWarningCodes(record.WarningCodesJson));
            AppendField(content, record.InclusionStatus.ToString());
            AppendField(content, record.CanonicalMatchStatus.ToString());
            AppendField(content, record.MemberMatchStatus.ToString());
            content.Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content.ToString())));
    }

    private static void AppendMoney(StringBuilder content, decimal value) =>
        AppendField(content, value.ToString("0.00", CultureInfo.InvariantCulture));

    private static void AppendField(StringBuilder content, string value) =>
        content.Append(value.Length).Append(':').Append(value).Append('|');

    private static string CanonicalizeWarningCodes(string warningCodesJson)
    {
        var warningCodes = JsonSerializer.Deserialize<string[]>(warningCodesJson) ?? Array.Empty<string>();
        return JsonSerializer.Serialize(warningCodes.OrderBy(x => x, StringComparer.Ordinal));
    }
}
