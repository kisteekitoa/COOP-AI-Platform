namespace COOPAI.API.Security;

public static class CoopRoles
{
    public const string Manager = "Manager";
    public const string LoanOfficer = "LoanOfficer";
    public const string Admin = "Admin";
    public const string Viewer = "Viewer";

    public static IReadOnlyList<string> All { get; } =
    [
        Manager,
        LoanOfficer,
        Admin,
        Viewer
    ];
}

public static class CoopPolicies
{
    public const string ManagerOnly = "ManagerOnly";
}
