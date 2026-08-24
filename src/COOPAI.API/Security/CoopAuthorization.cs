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
    public const string AuthenticatedUser = "AuthenticatedUser";
    public const string PortfolioRead = "PortfolioRead";
    public const string PortfolioReview = "PortfolioReview";
    public const string PortfolioManage = "PortfolioManage";
    public const string ImportRead = "ImportRead";
    public const string ImportExecute = "ImportExecute";
    public const string ManagerOnly = "ManagerOnly";
    public const string AdminOnly = "AdminOnly";
}
