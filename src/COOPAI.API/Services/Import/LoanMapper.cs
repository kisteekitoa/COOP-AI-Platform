// ==========================================================
// COOP-AI
// File        : LoanMapper.cs
// Module      : Loan Import
// Version     : 0.1.001
// Description : Convert Excel rows to LoanImportRow
// ==========================================================

using System.Data;
using System.Globalization;

namespace COOPAI.API.Services.Import;

public class LoanMapper
{
    public LoanImportRow Map(DataRow row)
    {
        return new LoanImportRow
        {
            MemberNo = GetString(row, 0),
            FullName = GetString(row, 1),
            ContractNo = GetString(row, 2),

            ContractDate = GetDateTime(row, 3),
            ExpireDate = GetDateTime(row, 4),

            LoanAmount = GetDecimal(row, 5),
            PrincipalBalance = GetDecimal(row, 6),
            ProfitBalance = GetDecimal(row, 7),
            TotalBalance = GetDecimal(row, 8),

            OverdueDays = GetInt(row, 9)
        };
    }

    private static string GetString(DataRow row, int columnIndex)
    {
        if (!HasColumn(row, columnIndex))
            return string.Empty;

        var value = row[columnIndex];

        if (value == null || value == DBNull.Value)
            return string.Empty;

        return value.ToString()?.Trim() ?? string.Empty;
    }

    private static decimal GetDecimal(DataRow row, int columnIndex)
    {
        if (!HasColumn(row, columnIndex))
            return 0m;

        var value = row[columnIndex];

        if (value == null || value == DBNull.Value)
            return 0m;

        if (value is decimal decimalValue)
            return decimalValue;

        if (value is double doubleValue)
            return Convert.ToDecimal(doubleValue);

        if (value is float floatValue)
            return Convert.ToDecimal(floatValue);

        if (value is int intValue)
            return intValue;

        if (value is long longValue)
            return longValue;

        var text = value.ToString()?.Trim();

        if (string.IsNullOrWhiteSpace(text))
            return 0m;

        text = text.Replace(",", "");

        if (decimal.TryParse(
                text,
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out var result))
        {
            return result;
        }

        return 0m;
    }

    private static int GetInt(DataRow row, int columnIndex)
    {
        if (!HasColumn(row, columnIndex))
            return 0;

        var value = row[columnIndex];

        if (value == null || value == DBNull.Value)
            return 0;

        if (value is int intValue)
            return intValue;

        if (value is long longValue)
            return Convert.ToInt32(longValue);

        if (value is double doubleValue)
            return Convert.ToInt32(doubleValue);

        var text = value.ToString()?.Trim();

        if (string.IsNullOrWhiteSpace(text))
            return 0;

        text = text.Replace(",", "");

        if (int.TryParse(text, out var result))
            return result;

        if (decimal.TryParse(
                text,
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out var decimalResult))
        {
            return Convert.ToInt32(decimalResult);
        }

        return 0;
    }

    private static DateTime? GetDateTime(DataRow row, int columnIndex)
    {
        if (!HasColumn(row, columnIndex))
            return null;

        var value = row[columnIndex];

        if (value == null || value == DBNull.Value)
            return null;

        if (value is DateTime dateTime)
            return dateTime;

        // Excel serial date
        if (value is double serialDate)
        {
            try
            {
                return DateTime.FromOADate(serialDate);
            }
            catch
            {
                return null;
            }
        }

        var text = value.ToString()?.Trim();

        if (string.IsNullOrWhiteSpace(text))
            return null;

        if (DateTime.TryParse(
                text,
                CultureInfo.GetCultureInfo("th-TH"),
                DateTimeStyles.None,
                out var thaiDate))
        {
            return NormalizeBuddhistYear(thaiDate);
        }

        if (DateTime.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
        {
            return NormalizeBuddhistYear(date);
        }

        return null;
    }

    private static DateTime NormalizeBuddhistYear(DateTime date)
    {
        if (date.Year <= 2400)
            return date;

        try
        {
            return date.AddYears(-543);
        }
        catch
        {
            return date;
        }
    }

    private static bool HasColumn(DataRow row, int columnIndex)
    {
        return columnIndex >= 0 &&
               columnIndex < row.Table.Columns.Count;
    }
}