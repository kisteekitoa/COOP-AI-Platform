using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using COOPAI.API.Data;
using COOPAI.API.DTOs.Auth;
using COOPAI.API.DTOs.PortfolioSnapshots;
using COOPAI.API.Models.Auth;
using COOPAI.API.Models.Portfolio;
using COOPAI.API.Security;
using COOPAI.API.Services.PortfolioSnapshots;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace COOPAI.API.Tests;

public sealed class PortfolioSnapshotPublishAuthorizationTests
{
    [Fact]
    public async Task Publish_Anonymous_IsUnauthorized()
    {
        await using var app = await PublishApplication.CreateAsync();
        using var client = app.CreateCookieClient();

        var response = await client.PostAsJsonAsync(
            $"/api/portfolio-snapshots/{app.SnapshotId}/publish",
            new { expectedSnapshotContentHash = app.SnapshotHash, confirmed = true });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(CoopRoles.Viewer)]
    [InlineData(CoopRoles.LoanOfficer)]
    [InlineData(CoopRoles.Admin)]
    public async Task Publish_ExplicitNonManagerRoles_AreForbidden(string role)
    {
        await using var app = await PublishApplication.CreateAsync();
        var password = NewPassword();
        await app.CreateUserAsync($"{role}.account", password, role);
        using var client = app.CreateCookieClient();
        Assert.Equal(HttpStatusCode.OK,
            (await app.LoginAsync(client, $"{role}.account", password)).StatusCode);

        var response = await app.PublishAsync(client);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(PortfolioSnapshotStatus.Validated, await app.SnapshotStatusAsync());
    }

    [Fact]
    public async Task Publish_ManagerRequiresAntiforgeryWhenEnabled()
    {
        await using var app = await PublishApplication.CreateAsync();
        var password = NewPassword();
        await app.CreateUserAsync("csrf.manager", password, CoopRoles.Manager);
        using var client = app.CreateCookieClient();
        Assert.Equal(HttpStatusCode.OK,
            (await app.LoginAsync(client, "csrf.manager", password)).StatusCode);

        var response = await client.PostAsJsonAsync(
            $"/api/portfolio-snapshots/{app.SnapshotId}/publish",
            new { expectedSnapshotContentHash = app.SnapshotHash, confirmed = true });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("InvalidAntiforgeryToken", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Publish_ManagerRequiresExplicitConfirmation()
    {
        await using var app = await PublishApplication.CreateAsync();
        var password = NewPassword();
        await app.CreateUserAsync("confirm.manager", password, CoopRoles.Manager);
        using var client = app.CreateCookieClient();
        Assert.Equal(HttpStatusCode.OK,
            (await app.LoginAsync(client, "confirm.manager", password)).StatusCode);

        var response = await app.PublishAsync(client, confirmed: false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("PublishConfirmationRequired", await response.Content.ReadAsStringAsync());
        Assert.Equal(PortfolioSnapshotStatus.Validated, await app.SnapshotStatusAsync());
    }

    [Fact]
    public async Task Publish_ManagerGetsExplicitNotFoundAndStaleHashConflicts()
    {
        await using var app = await PublishApplication.CreateAsync();
        var password = NewPassword();
        await app.CreateUserAsync("outcome.manager", password, CoopRoles.Manager);
        using var client = app.CreateCookieClient();
        Assert.Equal(HttpStatusCode.OK,
            (await app.LoginAsync(client, "outcome.manager", password)).StatusCode);

        var notFound = await app.PublishAsync(client, snapshotId: app.SnapshotId + 999);
        var staleHash = await app.PublishAsync(client, hash: new string('F', 64));

        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
        Assert.Contains("SnapshotNotFound", await notFound.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict, staleHash.StatusCode);
        Assert.Contains("SnapshotContentHashMismatch", await staleHash.Content.ReadAsStringAsync());
        Assert.Equal(PortfolioSnapshotStatus.Validated, await app.SnapshotStatusAsync());
    }

    [Fact]
    public async Task Publish_ManagerUsesPrincipalIdentityAndIgnoresSpoofedUserId()
    {
        await using var app = await PublishApplication.CreateAsync();
        var password = NewPassword();
        var manager = await app.CreateUserAsync("actual.manager", password, CoopRoles.Manager);
        using var client = app.CreateCookieClient();
        Assert.Equal(HttpStatusCode.OK,
            (await app.LoginAsync(client, "actual.manager", password)).StatusCode);

        var response = await app.PublishAsync(client, spoofedPublishedByUserId: manager.Id + 999);
        var result = await response.Content.ReadFromJsonAsync<PortfolioSnapshotPublishDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(manager.Id, result.PublishedByUserId);
        Assert.NotEqual(manager.Id + 999, result.PublishedByUserId);
        Assert.Equal(manager.Id, await app.PublisherUserIdAsync());
    }

    private static string NewPassword() => $"T!9a{Guid.NewGuid():N}";

    private sealed class PublishApplication : WebApplicationFactory<Program>, IAsyncDisposable
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");

        public int SnapshotId { get; private set; }
        public string SnapshotHash { get; private set; } = string.Empty;

        public static async Task<PublishApplication> CreateAsync()
        {
            var app = new PublishApplication();
            await app._connection.OpenAsync();
            _ = app.Services;
            await using var scope = app.Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<CoopDbContext>();
            await context.Database.EnsureCreatedAsync();
            var snapshot = ValidSnapshot();
            context.PortfolioSnapshots.Add(snapshot);
            await context.SaveChangesAsync();
            app.SnapshotId = snapshot.Id;
            app.SnapshotHash = snapshot.SnapshotContentHash;
            return app;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddConsole();
            });
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["PortfolioSnapshots:PublishingEnabled"] = "true"
                }));
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

        public async Task<CoopUser> CreateUserAsync(string userName, string password, string role)
        {
            await using var scope = Services.CreateAsyncScope();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<int>>>();
            if (!await roleManager.RoleExistsAsync(role))
                Assert.True((await roleManager.CreateAsync(new IdentityRole<int>(role))).Succeeded);
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<CoopUser>>();
            var user = new CoopUser
            {
                UserName = userName,
                DisplayName = $"Test {userName}",
                IsEnabled = true
            };
            Assert.True((await userManager.CreateAsync(user, password)).Succeeded);
            Assert.True((await userManager.AddToRoleAsync(user, role)).Succeeded);
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

        public async Task<HttpResponseMessage> PublishAsync(
            HttpClient client,
            int? spoofedPublishedByUserId = null,
            bool confirmed = true,
            int? snapshotId = null,
            string? hash = null)
        {
            var token = await GetAntiforgeryTokenAsync(client);
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"/api/portfolio-snapshots/{snapshotId ?? SnapshotId}/publish")
            {
                Content = JsonContent.Create(new
                {
                    expectedSnapshotContentHash = hash ?? SnapshotHash,
                    confirmed,
                    publishedByUserId = spoofedPublishedByUserId
                })
            };
            request.Headers.Add("X-CSRF-TOKEN", token);
            return await client.SendAsync(request);
        }

        public async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
        {
            var response = await client.GetAsync("/api/auth/csrf");
            response.EnsureSuccessStatusCode();
            var token = await response.Content.ReadFromJsonAsync<AntiforgeryTokenDto>();
            return token?.RequestToken ?? throw new InvalidOperationException("Missing antiforgery token.");
        }

        public async Task<PortfolioSnapshotStatus> SnapshotStatusAsync()
        {
            await using var scope = Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<CoopDbContext>()
                .PortfolioSnapshots
                .Where(x => x.Id == SnapshotId)
                .Select(x => x.Status)
                .SingleAsync();
        }

        public async Task<int?> PublisherUserIdAsync()
        {
            await using var scope = Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<CoopDbContext>()
                .PortfolioSnapshots
                .Where(x => x.Id == SnapshotId)
                .Select(x => x.PublishedByUserId)
                .SingleAsync();
        }

        async ValueTask IAsyncDisposable.DisposeAsync()
        {
            await base.DisposeAsync();
            await _connection.DisposeAsync();
        }

        private static PortfolioSnapshot ValidSnapshot()
        {
            var asOfDate = new DateOnly(2026, 6, 30);
            var record = new PortfolioSnapshotRecord
            {
                SourceRowNumber = 7,
                SourceRecordKey = "row:7",
                NormalizedContractNo = "สม-2569-000001",
                ContractDate = new DateOnly(2025, 1, 1),
                ExpireDate = asOfDate,
                SourceRowKind = PortfolioSourceRowKind.Contract,
                OpeningSide = PortfolioOpeningSide.Previous,
                TermStatus = PortfolioTermStatus.InTerm,
                BalanceStatus = PortfolioBalanceStatus.Outstanding,
                CanonicalMatchStatus = PortfolioCanonicalMatchStatus.Missing,
                MemberMatchStatus = PortfolioMemberMatchStatus.Missing,
                InclusionStatus = PortfolioSnapshotInclusionStatus.IncludedWithWarning,
                LoanTypePrefix = "สม",
                WarningCodesJson = JsonSerializer.Serialize(new[] { PortfolioSnapshotCodes.Negative }),
                PrincipalOpening = 80m,
                ProfitOpening = 20m,
                TotalOpening = 100m,
                PrincipalOutstanding = 80m,
                ProfitOutstanding = 20m,
                TotalOutstanding = 100m
            };
            var snapshot = new PortfolioSnapshot
            {
                AsOfDate = asOfDate,
                Status = PortfolioSnapshotStatus.Validated,
                SourceFileName = "publish.xlsx",
                SourceFileHash = new string('A', 64),
                SourceFileSizeBytes = 1,
                SourceRetrievedAt = DateTime.UtcNow,
                TotalSourceRows = 1,
                TotalContractCount = 1,
                MissingCanonicalCount = 1,
                UnresolvedMemberContractCount = 1,
                WarningRecordCount = 1,
                InTermContractCount = 1,
                OutstandingContractCount = 1,
                InTermOutstandingContractCount = 1,
                PrincipalOpening = 80m,
                ProfitOpening = 20m,
                TotalOpening = 100m,
                PrincipalOutstanding = 80m,
                ProfitOutstanding = 20m,
                TotalOutstanding = 100m,
                ValidatedAt = DateTime.UtcNow,
                Records = [record]
            };
            snapshot.SnapshotContentHash = new PortfolioSnapshotContentHasher().Compute(snapshot);
            return snapshot;
        }
    }
}
