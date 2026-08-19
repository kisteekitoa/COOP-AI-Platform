using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using COOPAI.API.Data;
using COOPAI.API.DTOs.Auth;
using COOPAI.API.Models.Auth;
using COOPAI.API.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace COOPAI.API.Tests;

public sealed class AuthenticationFoundationTests
{
    [Fact]
    public async Task Login_SucceedsWithSecureCookieAndStoresOnlyPasswordHash()
    {
        await using var app = await AuthTestApplication.CreateAsync();
        var password = NewPassword();
        var user = await app.CreateUserAsync("manager.account", password, true, CoopRoles.Manager);
        using var client = app.CreateCookieClient();

        var response = await app.LoginAsync(client, "MANAGER.ACCOUNT", password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), value => value.StartsWith("COOPAI.Auth=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("password", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("securityStamp", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("concurrencyStamp", body, StringComparison.OrdinalIgnoreCase);

        await using var scope = app.Services.CreateAsyncScope();
        var persisted = await scope.ServiceProvider.GetRequiredService<UserManager<CoopUser>>().FindByIdAsync(user.Id.ToString());
        Assert.NotNull(persisted);
        Assert.False(string.IsNullOrWhiteSpace(persisted.PasswordHash));
        Assert.NotEqual(password, persisted.PasswordHash);
    }

    [Fact]
    public async Task Login_InvalidPasswordAndUnknownUserReturnSameGenericFailure()
    {
        await using var app = await AuthTestApplication.CreateAsync();
        await app.CreateUserAsync("known.account", NewPassword(), true, CoopRoles.Viewer);
        using var invalidClient = app.CreateCookieClient();
        using var unknownClient = app.CreateCookieClient();

        var invalid = await app.LoginAsync(invalidClient, "known.account", NewPassword());
        var unknown = await app.LoginAsync(unknownClient, "unknown.account", NewPassword());

        Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(
            await invalid.Content.ReadAsStringAsync(),
            await unknown.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Login_DisabledUserIsRejected()
    {
        await using var app = await AuthTestApplication.CreateAsync();
        var password = NewPassword();
        await app.CreateUserAsync("disabled.account", password, false, CoopRoles.Viewer);
        using var client = app.CreateCookieClient();

        var response = await app.LoginAsync(client, "disabled.account", password);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_RequiresAuthenticationAndReturnsStableSafeIdentityAndRoles()
    {
        await using var app = await AuthTestApplication.CreateAsync();
        var password = NewPassword();
        var expected = await app.CreateUserAsync("loan.account", password, true, CoopRoles.LoanOfficer);
        using var anonymous = app.CreateCookieClient();
        using var authenticated = app.CreateCookieClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await app.LoginAsync(authenticated, "loan.account", password)).StatusCode);

        var response = await authenticated.GetAsync("/api/auth/me");
        var user = await response.Content.ReadFromJsonAsync<AuthUserDto>();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(user);
        Assert.Equal(expected.Id, user.Id);
        Assert.Equal("loan.account", user.UserName);
        Assert.Equal([CoopRoles.LoanOfficer], user.Roles);

        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stamp", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Logout_InvalidatesAuthenticatedSession()
    {
        await using var app = await AuthTestApplication.CreateAsync();
        var password = NewPassword();
        await app.CreateUserAsync("logout.account", password, true, CoopRoles.Viewer);
        using var client = app.CreateCookieClient();
        Assert.Equal(HttpStatusCode.OK, (await app.LoginAsync(client, "logout.account", password)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);

        var token = await app.GetAntiforgeryTokenAsync(client);
        using var logout = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        logout.Headers.Add("X-CSRF-TOKEN", token);
        var logoutResponse = await client.SendAsync(logout);

        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Login_WithoutAntiforgeryTokenIsRejected()
    {
        await using var app = await AuthTestApplication.CreateAsync();
        using var client = app.CreateCookieClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            userName = "any.account",
            password = NewPassword()
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(CoopRoles.Manager, true)]
    [InlineData(CoopRoles.LoanOfficer, false)]
    [InlineData(CoopRoles.Viewer, false)]
    [InlineData(CoopRoles.Admin, false)]
    public async Task ManagerPolicy_UsesExplicitManagerRoleOnly(string role, bool expected)
    {
        await using var app = await AuthTestApplication.CreateAsync();
        var authorization = app.Services.GetRequiredService<IAuthorizationService>();
        var principal = Principal(role);

        var result = await authorization.AuthorizeAsync(principal, null, CoopPolicies.ManagerOnly);

        Assert.Equal(expected, result.Succeeded);
    }

    [Fact]
    public async Task ManagerPolicy_RejectsUnauthenticatedPrincipal()
    {
        await using var app = await AuthTestApplication.CreateAsync();
        var authorization = app.Services.GetRequiredService<IAuthorizationService>();

        var result = await authorization.AuthorizeAsync(
            new ClaimsPrincipal(new ClaimsIdentity()),
            null,
            CoopPolicies.ManagerOnly);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Login_ClientSuppliedRolesAreIgnored()
    {
        await using var app = await AuthTestApplication.CreateAsync();
        var password = NewPassword();
        await app.CreateUserAsync("viewer.account", password, true, CoopRoles.Viewer);
        using var client = app.CreateCookieClient();
        var token = await app.GetAntiforgeryTokenAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new
            {
                userName = "viewer.account",
                password,
                roles = new[] { CoopRoles.Manager }
            })
        };
        request.Headers.Add("X-CSRF-TOKEN", token);
        var response = await client.SendAsync(request);
        var user = await response.Content.ReadFromJsonAsync<AuthUserDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(user);
        Assert.Equal([CoopRoles.Viewer], user.Roles);
    }

    [Fact]
    public async Task UserManager_RejectsCaseInsensitiveDuplicateUsername()
    {
        await using var app = await AuthTestApplication.CreateAsync();
        var password = NewPassword();
        await app.CreateUserAsync("unique.account", password, true, CoopRoles.Viewer);

        await using var scope = app.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<CoopUser>>();
        var duplicate = new CoopUser
        {
            UserName = "UNIQUE.ACCOUNT",
            DisplayName = "Duplicate Test Account",
            IsEnabled = true
        };
        var result = await userManager.CreateAsync(duplicate, NewPassword());

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Code == "DuplicateUserName");
    }

    [Fact]
    public async Task ManagerSession_PortfolioPublishRemainsHardDisabled()
    {
        await using var app = await AuthTestApplication.CreateAsync();
        var password = NewPassword();
        await app.CreateUserAsync("publish.manager", password, true, CoopRoles.Manager);
        using var client = app.CreateCookieClient();
        Assert.Equal(HttpStatusCode.OK, (await app.LoginAsync(client, "publish.manager", password)).StatusCode);

        var response = await client.PostAsync("/api/portfolio-snapshots/987/publish", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("PublishingDisabled", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AuthenticationFailure_DoesNotWritePortfolioOrBusinessTables()
    {
        await using var app = await AuthTestApplication.CreateAsync();
        using var client = app.CreateCookieClient();
        var before = await app.BusinessCountsAsync();

        var response = await app.LoginAsync(client, "missing.account", NewPassword());
        var after = await app.BusinessCountsAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(before, after);
    }

    private static ClaimsPrincipal Principal(string role) => new(
        new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "42"),
            new Claim(ClaimTypes.Name, "policy-test"),
            new Claim(ClaimTypes.Role, role)
        ],
        authenticationType: "Test"));

    private static string NewPassword() => $"T!9a{Guid.NewGuid():N}";

    private sealed class AuthTestApplication : WebApplicationFactory<Program>, IAsyncDisposable
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");

        public static async Task<AuthTestApplication> CreateAsync()
        {
            var application = new AuthTestApplication();
            await application._connection.OpenAsync();
            _ = application.Services;
            await using var scope = application.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<CoopDbContext>().Database.EnsureCreatedAsync();
            return application;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddConsole();
            });
            builder.ConfigureServices(services =>
            {
                var databaseRegistrations = services
                    .Where(descriptor =>
                        descriptor.ServiceType == typeof(DbContextOptions<CoopDbContext>) ||
                        descriptor.ServiceType.GenericTypeArguments.Contains(typeof(CoopDbContext)))
                    .ToArray();
                foreach (var registration in databaseRegistrations)
                    services.Remove(registration);
                services.AddDbContext<CoopDbContext>(options => options.UseSqlite(_connection));
            });
        }

        public HttpClient CreateCookieClient() => CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true
        });

        public async Task<CoopUser> CreateUserAsync(
            string userName,
            string password,
            bool isEnabled,
            params string[] roles)
        {
            await using var scope = Services.CreateAsyncScope();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<int>>>();
            foreach (var role in roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                    Assert.True((await roleManager.CreateAsync(new IdentityRole<int>(role))).Succeeded);
            }

            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<CoopUser>>();
            var user = new CoopUser
            {
                UserName = userName,
                DisplayName = $"Test {userName}",
                IsEnabled = isEnabled,
                CreatedAt = DateTime.UtcNow
            };
            Assert.True((await userManager.CreateAsync(user, password)).Succeeded);
            if (roles.Length > 0)
                Assert.True((await userManager.AddToRolesAsync(user, roles)).Succeeded);
            return user;
        }

        public async Task<HttpResponseMessage> LoginAsync(
            HttpClient client,
            string userName,
            string password)
        {
            var token = await GetAntiforgeryTokenAsync(client);
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
            {
                Content = JsonContent.Create(new { userName, password })
            };
            request.Headers.Add("X-CSRF-TOKEN", token);
            return await client.SendAsync(request);
        }

        public async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
        {
            var response = await client.GetAsync("/api/auth/csrf");
            response.EnsureSuccessStatusCode();
            var token = await response.Content.ReadFromJsonAsync<AntiforgeryTokenDto>();
            return token?.RequestToken ?? throw new InvalidOperationException("Antiforgery token was not returned.");
        }

        public async Task<BusinessCounts> BusinessCountsAsync()
        {
            await using var scope = Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<CoopDbContext>();
            return new BusinessCounts(
                await context.Members.CountAsync(),
                await context.LoanContracts.CountAsync(),
                await context.ImportBatches.CountAsync(),
                await context.ImportLoanRecords.CountAsync(),
                await context.PortfolioSnapshots.CountAsync(),
                await context.PortfolioSnapshotRecords.CountAsync(),
                await context.PortfolioSnapshotExclusions.CountAsync());
        }

        async ValueTask IAsyncDisposable.DisposeAsync()
        {
            await base.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed record BusinessCounts(
        int Members,
        int LoanContracts,
        int ImportBatches,
        int ImportLoanRecords,
        int PortfolioSnapshots,
        int PortfolioSnapshotRecords,
        int PortfolioSnapshotExclusions);
}
