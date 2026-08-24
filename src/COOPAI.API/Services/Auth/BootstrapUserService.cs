using COOPAI.API.Models.Auth;
using COOPAI.API.Security;
using Microsoft.AspNetCore.Identity;

namespace COOPAI.API.Services.Auth;

public sealed record BootstrapUserRequest(
    string UserName,
    string? DisplayName,
    string Role,
    string Password);

public sealed record BootstrapUserResult(
    bool Succeeded,
    int? UserId,
    string UserName,
    string Role,
    IReadOnlyList<string> CreatedRoles,
    IReadOnlyList<string> Errors);

public interface IBootstrapUserService
{
    Task<BootstrapUserResult> BootstrapAsync(
        BootstrapUserRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class BootstrapUserService(
    RoleManager<IdentityRole<int>> roleManager,
    UserManager<CoopUser> userManager) : IBootstrapUserService
{
    public async Task<BootstrapUserResult> BootstrapAsync(
        BootstrapUserRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var userName = request.UserName.Trim();
        if (string.IsNullOrWhiteSpace(userName))
            return Failure(userName, request.Role, "Username is required.");
        if (request.Role is not (CoopRoles.Admin or CoopRoles.Manager))
            return Failure(userName, request.Role, "Bootstrap role must be Admin or Manager.");
        if (await userManager.FindByNameAsync(userName) is not null)
            return Failure(userName, request.Role, "The username already exists.");

        var createdRoles = new List<string>();
        foreach (var roleName in CoopRoles.All)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await roleManager.RoleExistsAsync(roleName))
                continue;
            var roleResult = await roleManager.CreateAsync(new IdentityRole<int>(roleName));
            if (!roleResult.Succeeded)
                return Failure(userName, request.Role, roleResult.Errors.Select(SafeIdentityError), createdRoles);
            createdRoles.Add(roleName);
        }

        var user = new CoopUser
        {
            UserName = userName,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? userName : request.DisplayName.Trim(),
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow
        };
        var createResult = await userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
            return Failure(userName, request.Role, createResult.Errors.Select(SafeIdentityError), createdRoles);

        var roleResultForUser = await userManager.AddToRoleAsync(user, request.Role);
        if (!roleResultForUser.Succeeded)
        {
            await userManager.DeleteAsync(user);
            return Failure(userName, request.Role, roleResultForUser.Errors.Select(SafeIdentityError), createdRoles);
        }

        return new BootstrapUserResult(true, user.Id, userName, request.Role, createdRoles, []);
    }

    private static string SafeIdentityError(IdentityError error) => $"{error.Code}: {error.Description}";

    private static BootstrapUserResult Failure(
        string userName,
        string role,
        string error) => Failure(userName, role, [error], []);

    private static BootstrapUserResult Failure(
        string userName,
        string role,
        IEnumerable<string> errors,
        IReadOnlyList<string> createdRoles) =>
        new(false, null, userName, role, createdRoles, errors.ToArray());
}

public static class BootstrapUserCommand
{
    private const string CommandName = "bootstrap-user";
    private const string DefaultPasswordEnvironmentVariable = "COOPAI_BOOTSTRAP_PASSWORD";

    public static bool IsRequested(string[] args) =>
        args.Length > 0 && string.Equals(args[0], CommandName, StringComparison.OrdinalIgnoreCase);

    public static async Task<int> RunAsync(
        IServiceProvider services,
        string[] args,
        CancellationToken cancellationToken = default)
    {
        var values = ParseArguments(args.Skip(1));
        if (!values.TryGetValue("role", out var role) ||
            !values.TryGetValue("username", out var userName))
        {
            Console.Error.WriteLine("Usage: bootstrap-user --role <Admin|Manager> --username <name> [--display-name <name>] [--password-env <variable>]");
            return 2;
        }

        var passwordEnvironmentVariable = values.GetValueOrDefault("password-env", DefaultPasswordEnvironmentVariable);
        var password = Environment.GetEnvironmentVariable(passwordEnvironmentVariable);
        if (string.IsNullOrEmpty(password))
        {
            if (Console.IsInputRedirected)
            {
                Console.Error.WriteLine($"Bootstrap password is required through environment variable '{passwordEnvironmentVariable}' when input is redirected.");
                return 2;
            }

            Console.Error.Write("Password: ");
            password = ReadMaskedPassword();
            Console.Error.WriteLine();
        }

        await using var scope = services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IBootstrapUserService>();
        var result = await service.BootstrapAsync(
            new BootstrapUserRequest(userName, values.GetValueOrDefault("display-name"), role, password),
            cancellationToken);

        if (!result.Succeeded)
        {
            Console.Error.WriteLine($"Bootstrap failed for user '{result.UserName}' and role '{result.Role}'.");
            foreach (var error in result.Errors)
                Console.Error.WriteLine(error);
            return 1;
        }

        Console.WriteLine($"Bootstrap succeeded. UserId={result.UserId}; UserName={result.UserName}; Role={result.Role}; RolesCreated={result.CreatedRoles.Count}.");
        return 0;
    }

    private static Dictionary<string, string> ParseArguments(IEnumerable<string> args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var iterator = args.GetEnumerator();
        while (iterator.MoveNext())
        {
            var key = iterator.Current;
            if (!key.StartsWith("--", StringComparison.Ordinal) || !iterator.MoveNext())
                continue;
            result[key[2..]] = iterator.Current;
        }

        return result;
    }

    private static string ReadMaskedPassword()
    {
        var characters = new List<char>();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
                return new string(characters.ToArray());
            if (key.Key == ConsoleKey.Backspace)
            {
                if (characters.Count > 0)
                    characters.RemoveAt(characters.Count - 1);
                continue;
            }
            if (!char.IsControl(key.KeyChar))
                characters.Add(key.KeyChar);
        }
    }
}
