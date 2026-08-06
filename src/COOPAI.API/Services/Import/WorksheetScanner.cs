// ==========================================================
// COOP-AI
// File        : WorksheetScanner.cs
// Module      : Import Engine
// Version     : 1.0.000
// Description : Scan worksheet structure
// ==========================================================

using System.Data;

namespace COOPAI.API.Services.Import;

public class WorksheetScanner
{
    public List<List<string>> Scan(DataTable table)
    {
        var rows = new List<List<string>>();

        foreach (DataRow dataRow in table.Rows)
        {
            var row = new List<string>();

            foreach (DataColumn column in table.Columns)
            {
                row.Add(dataRow[column]?.ToString()?.Trim() ?? "");
            }

            rows.Add(row);
        }

        return rows;
    }
}