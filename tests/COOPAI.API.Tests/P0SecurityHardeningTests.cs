using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using COOPAI.API.Data;
using COOPAI.API.DTOs.Auth;
using COOPAI.API.Models.Auth;
using COOPAI.API.Models.Import;
using COOPAI.API.Security;
using COOPAI.API.Services.Auth;
using COOPAI.API.Services.Import;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace COOPAI.API.Tests;

public sealed class P0SecurityHardeningTests
{
    [Fact]
    public async Task DefaultDeny_DashboardAndOperationalEndpointsRejectAnonymous()
    {
        await using var app = await P0Application.CreateAsync();
        using var client = app.CreateCookieClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/dashboard/summary")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/import/preview")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/import/import", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/portfolio-snapshots/1/review")).StatusCode);
    }

    [Fact]
    public async Task Dashboard_AllAuthenticatedRolesAreAllowed()
    {
        await using var app = await P0Application.CreateAsync();
        foreach (var role in CoopRoles.All)
        {
            using var client = app.CreateCookieClient();
            var password = NewPassword();
            await app.CreateUserAsync($"dashboard.{role}", password, role);
            Assert.Equal(HttpStatusCode.OK, (await app.LoginAsync(client, $"dashboard.{role}", password)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/dashboard/summary")).StatusCode);
        }
    }

    [Fact]
    public async Task Policies_UseExplicitMinimumPrivilegeRoles()
    {
        await using var app = await P0Application.CreateAsync();
        var authorization = app.Services.GetRequiredService<IAuthorizationService>();

        Assert.True((await authorization.AuthorizeAsync(Principal(CoopRoles.Viewer), null, CoopPolicies.PortfolioRead)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(Principal(CoopRoles.Viewer), null, CoopPolicies.PortfolioReview)).Succeeded);
        Assert.True((await authorization.AuthorizeAsync(Principal(CoopRoles.LoanOfficer), null, CoopPolicies.PortfolioReview)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(Principal(CoopRoles.LoanOfficer), null, CoopPolicies.PortfolioManage)).Succeeded);
        Assert.True((await authorization.AuthorizeAsync(Principal(CoopRoles.Manager), null, CoopPolicies.PortfolioManage)).Succeeded);
        Assert.True((await authorization.AuthorizeAsync(Principal(CoopRoles.LoanOfficer), null, CoopPolicies.ImportExecute)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(Principal(CoopRoles.Manager), null, CoopPolicies.ImportExecute)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(Principal(CoopRoles.Admin), null, CoopPolicies.ManagerOnly)).Succeeded);
        Assert.True((await authorization.AuthorizeAsync(Principal(CoopRoles.Admin), null, CoopPolicies.AdminOnly)).Succeeded);
    }

    [Fact]
    public async Task ImportWrite_ViewerIsForbidden_AndLoanOfficerStillRequiresCsrf()
    {
        await using var app = await P0Application.CreateAsync();
        using var viewer = await app.AuthenticatedClientAsync("import.viewer", CoopRoles.Viewer);
        using var officer = await app.AuthenticatedClientAsync("import.officer", CoopRoles.LoanOfficer);

        Assert.Equal(HttpStatusCode.Forbidden, (await app.SendWorkbookAsync(viewer, "/api/import/import", csrf: true)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await app.SendWorkbookAsync(officer, "/api/import/import", csrf: false)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await app.SendWorkbookAsync(officer, "/api/import/import", csrf: true, invalidCsrf: true)).StatusCode);
    }

    [Fact]
    public async Task ImportValidate_AuthorizedCsrfRequestSucceedsWithoutPathLeakAndCleansFile()
    {
        await using var app = await P0Application.CreateAsync();
        using var client = await app.AuthenticatedClientAsync("validate.officer", CoopRoles.LoanOfficer);

        var response = await app.SendWorkbookAsync(client, "/api/import/validate", csrf: true);
        var responseBody = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(app.ImportService.SawExistingFile);
        Assert.NotNull(app.ImportService.LastPath);
        Assert.False(File.Exists(app.ImportService.LastPath));
        Assert.DoesNotContain(@"D:\", responseBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"C:\", responseBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ImportPreview_IsSanitizedAndDoesNotInspectServerWorkstationPaths()
    {
        await using var app = await P0Application.CreateAsync();
        using var client = await app.AuthenticatedClientAsync("preview.officer", CoopRoles.LoanOfficer);

        var body = await (await client.GetAsync("/api/import/preview")).Content.ReadAsStringAsync();

        Assert.DoesNotContain(@"D:\", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"C:\", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("temporary file path", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ImportValidationFailureAndExceptionBothCleanOwnedTempFile()
    {
        await using var app = await P0Application.CreateAsync();
        using var client = await app.AuthenticatedClientAsync("cleanup.officer", CoopRoles.LoanOfficer);
        app.ImportService.ValidationResult = new ImportResult { Success = false, FailedRows = 1 };

        Assert.Equal(HttpStatusCode.OK, (await app.SendWorkbookAsync(client, "/api/import/validate", csrf: true)).StatusCode);
        Assert.False(File.Exists(app.ImportService.LastPath));

        app.ImportService.ThrowOnValidate = true;
        try
        {
            _ = await app.SendWorkbookAsync(client, "/api/import/validate", csrf: true);
        }
        catch (InvalidOperationException)
        {
            // TestServer can propagate an unhandled service failure to the client.
        }
        Assert.False(File.Exists(app.ImportService.LastPath));
    }

    [Fact]
    public async Task ImportStore_RejectsUnsafeUploadsAndNeverDeletesUnrelatedFiles()
    {
        var root = NewTempDirectory();
        try
        {
            var options = Options.Create(new ImportUploadOptions
            {
                TempDirectory = root,
                MaxUploadBytes = 8,
                RetentionHours = 1
            });
            var store = new ImportTempFileStore(options);
            var unrelated = Path.Combine(root, "operator-file.xlsx");
            await File.WriteAllTextAsync(unrelated, "keep");

            await Assert.ThrowsAsync<ImportUploadException>(() => store.SaveAsync(FormFile([1], "malware.exe"), default));
            await Assert.ThrowsAsync<ImportUploadException>(() => store.SaveAsync(FormFile(new byte[9], "large.xlsx"), default));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(FormFile([1, 2], "cancel.xlsx"), cancellation.Token));

            await using (var lease = await store.SaveAsync(FormFile([1, 2], @"..\traversal.xlsx"), default))
            {
                Assert.Equal(root, Path.GetDirectoryName(lease.FilePath), ignoreCase: true);
                Assert.StartsWith("coopai-import-", Path.GetFileName(lease.FilePath), StringComparison.Ordinal);
            }

            Assert.True(File.Exists(unrelated));
            Assert.Single(Directory.GetFiles(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ImportEndpoint_RejectsUnsupportedAndOversizedUploads()
    {
        await using var app = await P0Application.CreateAsync(maxUploadBytes: 8);
        using var client = await app.AuthenticatedClientAsync("limits.officer", CoopRoles.LoanOfficer);

        var unsupported = await app.SendWorkbookAsync(client, "/api/import/validate", true, fileName: "bad.exe", bytes: [1]);
        var oversized = await app.SendWorkbookAsync(client, "/api/import/validate", true, fileName: "big.xlsx", bytes: new byte[9]);

        Assert.Equal(HttpStatusCode.BadRequest, unsupported.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
    }

    [Fact]
    public async Task SnapshotMutation_RequiresManagerAndCsrf()
    {
        await using var app = await P0Application.CreateAsync();
        using var officer = await app.AuthenticatedClientAsync("snapshot.officer", CoopRoles.LoanOfficer);
        using var manager = await app.AuthenticatedClientAsync("snapshot.manager", CoopRoles.Manager);

        Assert.Equal(HttpStatusCode.Forbidden, (await officer.PostAsync("/api/portfolio-snapshots/1/validate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsync("/api/portfolio-snapshots/1/validate", null)).StatusCode);
    }

    [Fact]
    public async Task Bootstrap_IsIdempotentRoleSafeAndEnforcesIdentityPasswordRules()
    {
        await using var app = await P0Application.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IBootstrapUserService>();

        var weak = await service.BootstrapAsync(new("weak.admin", null, CoopRoles.Admin, "weak"));
        var admin = await service.BootstrapAsync(new("first.admin", "Initial Admin", CoopRoles.Admin, NewPassword()));
        var manager = await service.BootstrapAsync(new("first.manager", null, CoopRoles.Manager, NewPassword()));
        var duplicate = await service.BootstrapAsync(new("FIRST.ADMIN", null, CoopRoles.Admin, NewPassword()));

        Assert.False(weak.Succeeded);
        Assert.True(admin.Succeeded);
        Assert.True(manager.Succeeded);
        Assert.False(duplicate.Succeeded);
        foreach (var role in CoopRoles.All)
            Assert.True(await app.RoleExistsAsync(role));
        Assert.Equal([CoopRoles.Admin], await app.UserRolesAsync("first.admin"));
        Assert.Equal([CoopRoles.Manager], await app.UserRolesAsync("first.manager"));
        var safeOutput = JsonSerializer.Serialize(admin);
        Assert.DoesNotContain("password", safeOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hash", safeOutput, StringComparison.OrdinalIgnoreCase);
        Assert.True(admin.UserId > 0);
    }

    [Fact]
    public async Task DisabledUserAndSecurityStampChangeInvalidateExistingSessions()
    {
        await using var app = await P0Application.CreateAsync();
        var disabled = await app.CreateUserWithClientAsync("disable.session", CoopRoles.Viewer);
        var stamped = await app.CreateUserWithClientAsync("stamp.session", CoopRoles.Viewer);

        await app.DisableUserAsync("disable.session");
        await app.UpdateSecurityStampAsync("stamp.session");

        Assert.Equal(HttpStatusCode.Unauthorized, (await disabled.Client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await stamped.Client.GetAsync("/api/auth/me")).StatusCode);
        disabled.Client.Dispose();
        stamped.Client.Dispose();
    }

    [Fact]
    public async Task ManagerRoleRemovalRefreshesCookieAndPreventsPublishAuthorization()
    {
        await using var app = await P0Application.CreateAsync(publishingEnabled: true);
        var authenticated = await app.CreateUserWithClientAsync("revoked.manager", CoopRoles.Manager);
        var token = await app.GetAntiforgeryTokenAsync(authenticated.Client);
        await app.RemoveRoleAsync("revoked.manager", CoopRoles.Manager);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/portfolio-snapshots/999/publish")
        {
            Content = JsonContent.Create(new { expectedSnapshotContentHash = new string('A', 64), confirmed = true })
        };
        request.Headers.Add("X-CSRF-TOKEN", token);

        var response = await authenticated.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        authenticated.Client.Dispose();
    }

    [Fact]
    public void ProductionConfiguration_RequiresDurableDataProtectionAndTrustedHosts()
    {
        Assert.False(ProductionSecurityConfiguration.ValidateDataProtection(new(), true, out _));
        Assert.True(ProductionSecurityConfiguration.ValidateDataProtection(new CoopDataProtectionOptions
        {
            ApplicationName = "COOP-AI",
            KeyRingPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "coopai-key-test")),
            ProtectionMode = "Certificate",
            CertificateThumbprint = "PLACEHOLDER"
        }, true, out _));
        Assert.False(ProductionSecurityConfiguration.ValidateDataProtection(new CoopDataProtectionOptions
        {
            ApplicationName = "COOP-AI",
            KeyRingPath = Path.Combine(AppContext.BaseDirectory, "keys"),
            ProtectionMode = "Certificate",
            CertificateThumbprint = "PLACEHOLDER"
        }, true, AppContext.BaseDirectory, out _));
        Assert.True(ProductionSecurityConfiguration.ValidateDataProtection(new(), false, out _));

        Assert.False(ProductionSecurityConfiguration.ValidateReverseProxy(new ReverseProxyTrustOptions { Enabled = true }, out _));
        Assert.False(ProductionSecurityConfiguration.ValidateReverseProxy(new ReverseProxyTrustOptions
        {
            Enabled = true,
            KnownNetworks = ["0.0.0.0/invalid"]
        }, out _));
        Assert.True(ProductionSecurityConfiguration.ValidateReverseProxy(new ReverseProxyTrustOptions
        {
            Enabled = true,
            KnownProxies = ["127.0.0.1"]
        }, out _));

        var wildcard = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "*",
            ["Cors:AllowedOrigins:0"] = "*"
        }).Build();
        Assert.Throws<InvalidOperationException>(() =>
            ProductionSecurityConfiguration.ValidateProductionHostAndCors(new TestEnvironment(), wildcard));

        var sameOrigin = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "internal-hostname.example"
        }).Build();
        ProductionSecurityConfiguration.ValidateProductionHostAndCors(new TestEnvironment(), sameOrigin);
    }

    private static FormFile FormFile(byte[] bytes, string fileName) =>
        new(new MemoryStream(bytes), 0, bytes.Length, "File", fileName);

    private static string NewPassword() => $"T!9a{Guid.NewGuid():N}";

    private static string NewTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"coopai-p0-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static System.Security.Claims.ClaimsPrincipal Principal(string role) => new(
        new System.Security.Claims.ClaimsIdentity(
        [
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "42"),
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, role)
        ], "Test"));

    private sealed class FakeImportService : IExcelImportService
    {
        public string? LastPath { get; private set; }
        public bool SawExistingFile { get; private set; }
        public bool ThrowOnValidate { get; set; }
        public ImportResult ValidationResult { get; set; } = new() { Success = true, FileHash = new string('A', 64) };

        public ImportResult Preview(string filePath) => new();

        public Task<ImportResult> ValidateOnlyAsync(string filePath)
        {
            LastPath = filePath;
            SawExistingFile = File.Exists(filePath);
            if (ThrowOnValidate)
                throw new InvalidOperationException("Synthetic import validation failure.");
            return Task.FromResult(ValidationResult);
        }

        public Task<ImportResult> ImportAsync(string filePath, ImportExecutionOptions? options = null)
        {
            LastPath = filePath;
            SawExistingFile = File.Exists(filePath);
            return Task.FromResult(new ImportResult { Success = true, FileHash = new string('B', 64) });
        }
    }

    private sealed class P0Application : WebApplicationFactory<Program>, IAsyncDisposable
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        private readonly string _tempRoot = NewTempDirectory();
        private readonly long _maxUploadBytes;
        private readonly bool _publishingEnabled;

        private P0Application(long maxUploadBytes, bool publishingEnabled)
        {
            _maxUploadBytes = maxUploadBytes;
            _publishingEnabled = publishingEnabled;
        }

        public FakeImportService ImportService { get; } = new();

        public static async Task<P0Application> CreateAsync(
            long maxUploadBytes = ImportUploadOptions.DefaultMaxUploadBytes,
            bool publishingEnabled = false)
        {
            var app = new P0Application(maxUploadBytes, publishingEnabled);
            await app._connection.OpenAsync();
            _ = app.Services;
            await using var scope = app.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<CoopDbContext>().Database.EnsureCreatedAsync();
            return app;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(Environments.Development);
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Authentication:SecurityStampValidationMinutes"] = "0",
                    ["ImportUpload:TempDirectory"] = _tempRoot,
                    ["ImportUpload:MaxUploadBytes"] = _maxUploadBytes.ToString(),
                    ["PortfolioSnapshots:PublishingEnabled"] = _publishingEnabled.ToString()
                }));
            builder.ConfigureServices(services =>
            {
                foreach (var registration in services.Where(descriptor =>
                             descriptor.ServiceType == typeof(DbContextOptions<CoopDbContext>) ||
                             descriptor.ServiceType.GenericTypeArguments.Contains(typeof(CoopDbContext))).ToArray())
                    services.Remove(registration);
                services.AddDbContext<CoopDbContext>(options => options.UseSqlite(_connection));
                services.AddSingleton<IExcelImportService>(ImportService);
            });
        }

        public HttpClient CreateCookieClient() => CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true
        });

        public async Task<HttpClient> AuthenticatedClientAsync(string userName, string role)
        {
            var result = await CreateUserWithClientAsync(userName, role);
            return result.Client;
        }

        public async Task<(HttpClient Client, CoopUser User)> CreateUserWithClientAsync(string userName, string role)
        {
            var password = NewPassword();
            var user = await CreateUserAsync(userName, password, role);
            var client = CreateCookieClient();
            Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, userName, password)).StatusCode);
            return (client, user);
        }

        public async Task<CoopUser> CreateUserAsync(string userName, string password, string role)
        {
            await using var scope = Services.CreateAsyncScope();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<int>>>();
            if (!await roleManager.RoleExistsAsync(role))
                Assert.True((await roleManager.CreateAsync(new IdentityRole<int>(role))).Succeeded);
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<CoopUser>>();
            var user = new CoopUser { UserName = userName, DisplayName = userName, IsEnabled = true };
            Assert.True((await userManager.CreateAsync(user, password)).Succeeded);
            Assert.True((await userManager.AddToRoleAsync(user, role)).Succeeded);
            return user;
        }

        public async Task<HttpResponseMessage> LoginAsync(HttpClient client, string userName, string password)
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
            return (await response.Content.ReadFromJsonAsync<AntiforgeryTokenDto>())?.RequestToken
                   ?? throw new InvalidOperationException("Missing antiforgery token.");
        }

        public async Task<HttpResponseMessage> SendWorkbookAsync(
            HttpClient client,
            string path,
            bool csrf,
            bool invalidCsrf = false,
            string fileName = "Loan.xlsx",
            byte[]? bytes = null)
        {
            using var form = new MultipartFormDataContent();
            form.Add(new ByteArrayContent(bytes ?? [1, 2, 3]), "File", fileName);
            using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = form };
            if (csrf)
                request.Headers.Add("X-CSRF-TOKEN", invalidCsrf ? "invalid" : await GetAntiforgeryTokenAsync(client));
            return await client.SendAsync(request);
        }

        public async Task DisableUserAsync(string userName) => await MutateUserAsync(userName, async manager =>
        {
            var user = await manager.FindByNameAsync(userName) ?? throw new InvalidOperationException();
            user.IsEnabled = false;
            Assert.True((await manager.UpdateAsync(user)).Succeeded);
        });

        public async Task UpdateSecurityStampAsync(string userName) => await MutateUserAsync(userName, async manager =>
        {
            var user = await manager.FindByNameAsync(userName) ?? throw new InvalidOperationException();
            Assert.True((await manager.UpdateSecurityStampAsync(user)).Succeeded);
        });

        public async Task RemoveRoleAsync(string userName, string role) => await MutateUserAsync(userName, async manager =>
        {
            var user = await manager.FindByNameAsync(userName) ?? throw new InvalidOperationException();
            Assert.True((await manager.RemoveFromRoleAsync(user, role)).Succeeded);
        });

        public async Task<bool> RoleExistsAsync(string role)
        {
            await using var scope = Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<int>>>().RoleExistsAsync(role);
        }

        public async Task<IList<string>> UserRolesAsync(string userName)
        {
            await using var scope = Services.CreateAsyncScope();
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<CoopUser>>();
            var user = await manager.FindByNameAsync(userName) ?? throw new InvalidOperationException();
            return await manager.GetRolesAsync(user);
        }

        private async Task MutateUserAsync(string userName, Func<UserManager<CoopUser>, Task> mutation)
        {
            await using var scope = Services.CreateAsyncScope();
            await mutation(scope.ServiceProvider.GetRequiredService<UserManager<CoopUser>>());
        }

        async ValueTask IAsyncDisposable.DisposeAsync()
        {
            await base.DisposeAsync();
            await _connection.DisposeAsync();
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "COOPAI.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
