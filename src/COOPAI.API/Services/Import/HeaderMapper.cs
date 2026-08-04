// ==========================================================
// COOP-AI
// File        : HeaderMapper.cs
// Module      : Import Engine
// Version     : 0.1.001
// Description : Read Excel Header
// ==========================================================

using System.Data;

namespace COOPAI.API.Services.Import;

public class HeaderMapper
{
    private readonly Dictionary<string, int> _columns = new();

    public HeaderMapper(DataTable table)
    {
        if (table.Rows.Count == 0)
            return;

        var header = table.Rows[0];

        for (int i = 0; i < table.Columns.Count; i++)
        {
            var name = header[i]?.ToString()?.Trim();

            if (string.IsNullOrWhiteSpace(name))
                continue;

            if (!_columns.ContainsKey(name))
                _columns.Add(name, i);
        }
    }

    public bool HasColumn(string columnName)
    {
        return _columns.ContainsKey(columnName);
    }

    public int GetIndex(string columnName)
    {
        if (_columns.TryGetValue(columnName, out var index))
            return index;

        return -1;
    }

    public IReadOnlyDictionary<string, int> Columns => _columns;
}