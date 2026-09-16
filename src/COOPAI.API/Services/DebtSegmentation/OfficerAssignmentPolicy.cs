namespace COOPAI.API.Services.DebtSegmentation;

public static class OfficerAssignmentStatuses
{
    public const string Assigned = "ASSIGNED";
    public const string Unassigned = "UNASSIGNED";
    public const string Conflict = "CONFLICT";
}

public sealed record OfficerAssignmentResult(
    string Status,
    string? OfficerName,
    string AssignmentRule,
    IReadOnlyList<string> OfficerCandidates);

public static class OfficerAssignmentPolicy
{
    public const string Tuaeso = "\u0E19\u0E32\u0E22\u0E15\u0E39\u0E41\u0E27\u0E42\u0E0B\u0E30 \u0E42\u0E15\u0E30\u0E01\u0E39\u0E22\u0E37\u0E2D\u0E41\u0E23";
    public const string Burhanuddin = "\u0E19\u0E32\u0E22\u0E1A\u0E39\u0E23\u0E2E\u0E31\u0E19\u0E19\u0E38\u0E14\u0E14\u0E35\u0E19 \u0E01\u0E32\u0E40\u0E08";
    public const string Waesobri = "\u0E19\u0E32\u0E22\u0E41\u0E27\u0E0B\u0E2D\u0E1A\u0E23\u0E35 \u0E21\u0E39\u0E2B\u0E19\u0E30";
    public const string Irifan = "\u0E19\u0E32\u0E22\u0E2D\u0E34\u0E23\u0E34\u0E1F\u0E32\u0E19 \u0E2A\u0E32\u0E40\u0E21\u0E32\u0E30";

    public static IReadOnlyList<string> OfficerNames { get; } =
        [Tuaeso, Burhanuddin, Waesobri, Irifan];

    public static OfficerAssignmentResult Resolve(string? groupCode)
    {
        var normalized = Normalize(groupCode);
        if (string.IsNullOrEmpty(normalized))
            return Unassigned("\u0E44\u0E21\u0E48\u0E1E\u0E1A\u0E23\u0E2B\u0E31\u0E2A\u0E01\u0E25\u0E38\u0E48\u0E21");

        var matches = new List<(string Officer, string Rule)>();

        // Roster rules:
        // เธเธฒเธขเธ•เธนเนเธงเนเธเธฐ: M เธ—เธฑเนเธเธซเธกเธ” + L01-L03
        // เธเธฒเธขเธเธนเธฃเธฎเธฑเธเธเธธเธ”เธ”เธตเธ: R เธ—เธฑเนเธเธซเธกเธ” + L03-L31
        // เธเธฒเธขเนเธงเธเธญเธเธฃเธต: Y เธ—เธฑเนเธเธซเธกเธ”
        // เธเธฒเธขเธญเธดเธฃเธดเธเธฒเธ: C เธ—เธฑเนเธเธซเธกเธ” + L31-L53
        if (StartsWithGroup(normalized, "M"))
            matches.Add((Tuaeso, "M (\u0E17\u0E31\u0E49\u0E07\u0E2B\u0E21\u0E14)"));
        if (IsLRange(normalized, 1, 3))
            matches.Add((Tuaeso, "L01-L03"));

        if (StartsWithGroup(normalized, "R"))
            matches.Add((Burhanuddin, "R (\u0E17\u0E31\u0E49\u0E07\u0E2B\u0E21\u0E14)"));
        if (IsLRange(normalized, 3, 31))
            matches.Add((Burhanuddin, "L03-L31"));

        if (StartsWithGroup(normalized, "Y"))
            matches.Add((Waesobri, "Y (\u0E17\u0E31\u0E49\u0E07\u0E2B\u0E21\u0E14)"));

        if (StartsWithGroup(normalized, "C"))
            matches.Add((Irifan, "C (\u0E17\u0E31\u0E49\u0E07\u0E2B\u0E21\u0E14)"));
        if (IsLRange(normalized, 31, 53))
            matches.Add((Irifan, "L31-L53"));

        var byOfficer = matches
            .GroupBy(match => match.Officer, StringComparer.Ordinal)
            .Select(group => new
            {
                Officer = group.Key,
                Rules = group.Select(item => item.Rule).Distinct(StringComparer.Ordinal).ToArray()
            })
            .ToArray();

        if (byOfficer.Length == 0)
            return Unassigned($"\u0E44\u0E21\u0E48\u0E21\u0E35\u0E01\u0E0E\u0E23\u0E2D\u0E07\u0E23\u0E31\u0E1A\u0E01\u0E25\u0E38\u0E48\u0E21 {normalized}");

        if (byOfficer.Length == 1)
        {
            var match = byOfficer[0];
            return new OfficerAssignmentResult(
                OfficerAssignmentStatuses.Assigned,
                match.Officer,
                string.Join(" + ", match.Rules),
                [match.Officer]);
        }

        return new OfficerAssignmentResult(
            OfficerAssignmentStatuses.Conflict,
            null,
            string.Join(" / ", byOfficer.SelectMany(item => item.Rules).Distinct(StringComparer.Ordinal)),
            byOfficer.Select(item => item.Officer).ToArray());
    }

    private static OfficerAssignmentResult Unassigned(string rule) =>
        new(OfficerAssignmentStatuses.Unassigned, null, rule, []);

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : string.Concat(value.Where(character => !char.IsWhiteSpace(character))).ToUpperInvariant();

    private static bool StartsWithGroup(string value, string prefix) =>
        value.StartsWith(prefix, StringComparison.Ordinal);

    private static bool IsLRange(string value, int minimum, int maximum)
    {
        if (value.Length < 2 || value[0] != 'L')
            return false;

        return int.TryParse(value[1..], out var number) &&
            number >= minimum && number <= maximum;
    }
}