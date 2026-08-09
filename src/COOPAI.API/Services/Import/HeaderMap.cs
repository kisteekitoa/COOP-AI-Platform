// ==========================================================
// COOP-AI
// File        : HeaderMap.cs
// Module      : Import Engine
// Version     : 0.4.000
// Description : Stores Excel header positions
// ==========================================================

namespace COOPAI.API.Services.Import;

public class HeaderMap
{
    private readonly Dictionary<string, int> _columns = new();

    public void Add(string key, int columnIndex)
    {
        if (!_columns.ContainsKey(key))
            _columns.Add(key, columnIndex);
    }

    public bool Contains(string key)
    {
        return _columns.ContainsKey(key);
    }

    public int this[string key]
    {
        get
        {
            if (_columns.TryGetValue(key, out var value))
                return value;

            return -1;
        }
    }

    public IReadOnlyDictionary<string, int> Columns => _columns;
}