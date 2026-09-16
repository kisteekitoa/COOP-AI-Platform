using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using COOPAI.API.Data;
using COOPAI.API.DTOs.Auth;
using COOPAI.API.DTOs.PortfolioSnapshots;
using COOPAI.API.Models.Auth;
using COOPAI.API.Security;
using COOPAI.API.Services.PortfolioSnapshots;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace COOPAI.API.Tests;

public sealed class PortfolioSnapshotDateBindingTests
{
    [Fact]
    public async Task MultipartDate_ThTh_ValidateAndCreateDraftUseExactGregorianDate()
    {
        await using var app = await DateBindingApplication.CreateAsync();
        using var client = await app.AuthenticatedManagerAsync();
        var expected = new DateOnly(2026, 6, 30);

        using var validate = await app.SendWorkbookAsync(
            client,
            "/api/portfolio-snapshots/validate",
            "2026-06-30");
        using var draft = await app.SendWorkbookAsync(
            client,
            "/api/portfolio-snapshots/drafts",
            "2026-06-30",
            includeExpectedHashes: true);

        Assert.Equal(HttpStatusCode.OK, validate.StatusCode);
        Assert.Equal(HttpStatusCode.Created, draft.StatusCode);
        Assert.Equal(expected, app.Workflow.ValidatedAsOfDate);
        Assert.Equal(expected, app.Workflow.DraftAsOfDate);
        Assert.NotEqual(new DateOnly(1483, 6, 30), app.Workflow.ValidatedAsOfDate);
        Assert.Equal("th-TH", app.Workflow.ValidateCulture);
        Assert.Equal("th-TH", app.Workflow.DraftCulture);
        Assert.Equal(expected, (await validate.Content.ReadFromJsonAsync<PortfolioSnapshotValidationDto>())?.AsOfDate);
        Assert.Equal(expected, (await draft.Content.ReadFromJsonAsync<PortfolioSnapshotDraftDto>())?.AsOfDate);
    }

    [Theory]
    [InlineData("2026-13-40")]
    [InlineData("2569-06-30")]
    [InlineData("30 มิถุนายน 2569")]
    public async Task MultipartDate_InvalidValuesAreRejectedBeforeValidateOrDraft(string value)
    {
        await using var app = await DateBindingApplication.CreateAsync();
        using var client = await app.AuthenticatedManagerAsync();

        using var validate = await app.SendWorkbookAsync(
            client,
            "/api/portfolio-snapshots/validate",
            value);
        using var draft = await app.SendWorkbookAsync(
            client,
            "/api/portfolio-snapshots/drafts",
            value,
            includeExpectedHashes: true);

        Assert.Equal(HttpStatusCode.BadRequest, validate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, draft.StatusCode);
        Assert.Equal("InvalidAsOfDate", (await validate.Content.ReadFromJsonAsync<PortfolioSnapshotErrorDto>())?.Code);
        Assert.Equal("InvalidAsOfDate", (await draft.Content.ReadFromJsonAsync<PortfolioSnapshotErrorDto>())?.Code);
        Assert.Equal(0, app.Workflow.ValidateCalls);
        Assert.Equal(0, app.Workflow.DraftCalls);
        Assert.Equal(0, await app.SnapshotCountAsync());
    }

    private sealed class DateBindingApplication : WebApplicationFactory<Program>, IAsyncDisposable
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");

        public CapturingWorkflow Workflow { get; } = new();

        public static async Task<DateBindingApplication> CreateAsync()
        {
            var app = new DateBindingApplication();
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
                    ["PortfolioSnapshots:PublishingEnabled"] = "false"
                }));
            builder.ConfigureTestServices(services =>
            {
                foreach (var registration in services.Where(descriptor =>
                             descriptor.ServiceType == typeof(DbContextOptions<CoopDbContext>) ||
                             descriptor.ServiceType.GenericTypeArguments.Contains(typeof(CoopDbContext))).ToArray())
                {
                    services.Remove(registration);
                }
                services.AddDbContext<CoopDbContext>(options => options.UseSqlite(_connection));

                foreach (var registration in services
                             .Where(descriptor => descriptor.ServiceType == typeof(IPortfolioSnapshotWorkflowService))
                             .ToArray())
                {
                    services.Remove(registration);
                }
                services.AddSingleton<IPortfolioSnapshotWorkflowService>(Workflow);
                services.AddSingleton<IStartupFilter>(new RequestCultureStartupFilter("th-TH"));
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
            });
        }

        public async Task<HttpClient> AuthenticatedManagerAsync()
        {
            const string userName = "date.binding.manager";
            var password = $"T!9a{Guid.NewGuid():N}";
            await using (var scope = Services.CreateAsyncScope())
            {
                var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<int>>>();
                if (!await roleManager.RoleExistsAsync(CoopRoles.Manager))
                    Assert.True((await roleManager.CreateAsync(new IdentityRole<int>(CoopRoles.Manager))).Succeeded);
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<CoopUser>>();
                var user = new CoopUser
                {
                    UserName = userName,
                    DisplayName = "Date Binding Manager",
                    IsEnabled = true
                };
                Assert.True((await userManager.CreateAsync(user, password)).Succeeded);
                Assert.True((await userManager.AddToRoleAsync(user, CoopRoles.Manager)).Succeeded);
            }

            var client = CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost"),
                HandleCookies = true
            });
            var token = await GetAntiforgeryTokenAsync(client);
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
            {
                Content = JsonContent.Create(new { userName, password })
            };
            request.Headers.Add("X-CSRF-TOKEN", token);
            using var login = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            return client;
        }

        public async Task<HttpResponseMessage> SendWorkbookAsync(
            HttpClient client,
            string path,
            string asOfDate,
            bool includeExpectedHashes = false)
        {
            var form = new MultipartFormDataContent();
            form.Add(new ByteArrayContent([1, 2, 3]), "file", "Loan.xlsx");
            form.Add(new StringContent(asOfDate), "asOfDate");
            if (includeExpectedHashes)
            {
                form.Add(new StringContent(new string('A', 64)), "expectedSourceFileHash");
                form.Add(new StringContent(new string('B', 64)), "expectedSnapshotContentHash");
            }
            var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = form };
            request.Headers.Add("X-CSRF-TOKEN", await GetAntiforgeryTokenAsync(client));
            var response = await client.SendAsync(request);
            request.Dispose();
            return response;
        }

        public async Task<int> SnapshotCountAsync()
        {
            await using var scope = Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<CoopDbContext>().PortfolioSnapshots.CountAsync();
        }

        private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
        {
            using var response = await client.GetAsync("/api/auth/csrf");
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<AntiforgeryTokenDto>())?.RequestToken
                   ?? throw new InvalidOperationException("Missing antiforgery token.");
        }

        async ValueTask IAsyncDisposable.DisposeAsync()
        {
            await base.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class RequestCultureStartupFilter(string cultureName) : IStartupFilter
    {
        private readonly CultureInfo _culture = CultureInfo.GetCultureInfo(cultureName);

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (_, nextMiddleware) =>
            {
                var previousCulture = CultureInfo.CurrentCulture;
                var previousUiCulture = CultureInfo.CurrentUICulture;
                try
                {
                    CultureInfo.CurrentCulture = _culture;
                    CultureInfo.CurrentUICulture = _culture;
                    await nextMiddleware();
                }
                finally
                {
                    CultureInfo.CurrentCulture = previousCulture;
                    CultureInfo.CurrentUICulture = previousUiCulture;
                }
            });
            next(app);
        };
    }

    private sealed class CapturingWorkflow : IPortfolioSnapshotWorkflowService
    {
        private const string SourceHash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        private const string ContentHash = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

        public int ValidateCalls { get; private set; }
        public int DraftCalls { get; private set; }
        public DateOnly? ValidatedAsOfDate { get; private set; }
        public DateOnly? DraftAsOfDate { get; private set; }
        public string? ValidateCulture { get; private set; }
        public string? DraftCulture { get; private set; }

        public Task<PortfolioSnapshotValidationDto> ValidateAsync(
            string controlledCopyPath,
            string sourceFileName,
            DateOnly asOfDate,
            int? definitionVersion = null,
            CancellationToken cancellationToken = default)
        {
            ValidateCalls++;
            ValidatedAsOfDate = asOfDate;
            ValidateCulture = CultureInfo.CurrentCulture.Name;
            return Task.FromResult(new PortfolioSnapshotValidationDto(
                true,
                SourceHash,
                ContentHash,
                asOfDate,
                2,
                new PortfolioSnapshotCountsDto(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
                new PortfolioSnapshotFinancialDto(0, 0, 0, 0),
                new PortfolioSnapshotQualityDto(0, [], 0, true)));
        }

        public Task<PortfolioSnapshotDraftDto> CreateDraftAsync(
            string controlledCopyPath,
            string sourceFileName,
            DateOnly asOfDate,
            string expectedSourceFileHash,
            string expectedSnapshotContentHash,
            int? definitionVersion = null,
            CancellationToken cancellationToken = default)
        {
            DraftCalls++;
            DraftAsOfDate = asOfDate;
            DraftCulture = CultureInfo.CurrentCulture.Name;
            return Task.FromResult(new PortfolioSnapshotDraftDto(
                1,
                "Draft",
                false,
                SourceHash,
                ContentHash,
                asOfDate,
                1));
        }

        public Task<PortfolioSnapshotLifecycleDto> ValidateDraftAsync(int id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PortfolioSnapshotLifecycleDto> RejectAsync(int id, string reason, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PortfolioSnapshotPublishDto> PublishAsync(int id, string expectedSnapshotContentHash, int publisherUserId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PortfolioSnapshotReviewDto?> GetReviewAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult<PortfolioSnapshotReviewDto?>(null);

        public Task<PortfolioSnapshotRecordPageDto?> GetRecordsAsync(
            int id,
            int page,
            int pageSize,
            string? termStatus,
            string? balanceStatus,
            string? inclusionStatus,
            string? warningCode,
            string? canonicalMatchStatus,
            string? memberMatchStatus,
            string? loanTypePrefix,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<PortfolioSnapshotRecordPageDto?>(null);

        public Task<IReadOnlyList<PortfolioSnapshotListItemDto>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PortfolioSnapshotListItemDto>>([]);
    }
}
