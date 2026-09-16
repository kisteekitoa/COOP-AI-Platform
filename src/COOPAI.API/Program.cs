using COOPAI.API.Data;
using COOPAI.API.Models.Auth;
using COOPAI.API.Security;
using COOPAI.API.Services.Auth;
using COOPAI.API.Services.Dashboard;
using COOPAI.API.Services.DebtSegmentation;
using COOPAI.API.Services.Import;
using COOPAI.API.Services.PortfolioSnapshots;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

ProductionSecurityConfiguration.ValidateProductionHostAndCors(builder.Environment, builder.Configuration);
ProductionSecurityConfiguration.AddFoundation(builder);

#region Services

builder.Services.AddDbContext<CoopDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddControllers();

// First-party COOP-AI authentication. No users or roles are seeded automatically.
builder.Services
    .AddIdentity<CoopUser, IdentityRole<int>>(options =>
    {
        options.User.RequireUniqueEmail = false;
        options.Password.RequiredLength = 12;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<CoopDbContext>();

builder.Services.AddScoped<ICoopAuthenticationService, CoopAuthenticationService>();
builder.Services.AddScoped<CoopCookieAuthenticationEvents>();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "COOPAI.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    options.EventsType = typeof(CoopCookieAuthenticationEvents);
});

builder.Services.Configure<SecurityStampValidatorOptions>(options =>
{
    var configuredMinutes = builder.Configuration.GetValue<double?>(
        "Authentication:SecurityStampValidationMinutes");
    options.ValidationInterval = TimeSpan.FromMinutes(configuredMinutes is >= 0 ? configuredMinutes.Value : 5);
});

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "COOPAI.Antiforgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
});

builder.Services.AddAuthorization(options =>
{
    var authenticatedUser = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
    options.DefaultPolicy = authenticatedUser;
    options.FallbackPolicy = authenticatedUser;
    options.AddPolicy(CoopPolicies.AuthenticatedUser, policy => policy.RequireAuthenticatedUser());
    options.AddPolicy(CoopPolicies.PortfolioRead, policy => policy
        .RequireAuthenticatedUser()
        .RequireRole(CoopRoles.Viewer, CoopRoles.LoanOfficer, CoopRoles.Manager));
    options.AddPolicy(CoopPolicies.PortfolioReview, policy => policy
        .RequireAuthenticatedUser()
        .RequireRole(CoopRoles.LoanOfficer, CoopRoles.Manager));
    options.AddPolicy(CoopPolicies.PortfolioManage, policy => policy
        .RequireAuthenticatedUser()
        .RequireRole(CoopRoles.Manager));
    options.AddPolicy(CoopPolicies.ImportRead, policy => policy
        .RequireAuthenticatedUser()
        .RequireRole(CoopRoles.LoanOfficer));
    options.AddPolicy(CoopPolicies.ImportExecute, policy => policy
        .RequireAuthenticatedUser()
        .RequireRole(CoopRoles.LoanOfficer));
    options.AddPolicy(CoopPolicies.ManagerOnly, policy =>
        policy.RequireAuthenticatedUser().RequireRole(CoopRoles.Manager));
    options.AddPolicy(CoopPolicies.AdminOnly, policy =>
        policy.RequireAuthenticatedUser().RequireRole(CoopRoles.Admin));
});

// Import Engine
builder.Services.AddScoped<ExcelReader>();
builder.Services.AddScoped<ImportValidator>();
builder.Services.AddScoped<MemberImporter>();
builder.Services.AddScoped<LoanImporter>();
builder.Services.AddScoped<IExcelImportService, ExcelImportService>();
builder.Services.AddScoped<IImportTempFileStore, ImportTempFileStore>();
builder.Services.AddOptions<ImportUploadOptions>()
    .Bind(builder.Configuration.GetSection(ImportUploadOptions.SectionName))
    .Validate(options => options.MaxUploadBytes > 0, "ImportUpload:MaxUploadBytes must be greater than zero.")
    .Validate(options => options.RetentionHours > 0, "ImportUpload:RetentionHours must be greater than zero.")
    .Validate(options => options.AllowedExtensions.Length > 0 &&
                         options.AllowedExtensions.All(extension => extension is ".xlsx" or ".xls"),
        "ImportUpload:AllowedExtensions may contain only .xlsx and .xls.")
    .ValidateOnStart();
var importUploadOptions = builder.Configuration
    .GetSection(ImportUploadOptions.SectionName)
    .Get<ImportUploadOptions>() ?? new ImportUploadOptions();
builder.Services.Configure<FormOptions>(options =>
    options.MultipartBodyLengthLimit = importUploadOptions.MaxUploadBytes + (1024 * 1024));

// Dashboard
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IBootstrapUserService, BootstrapUserService>();

// Read-only Manager rehearsal. It reads only the configured controlled workbook.
builder.Services.Configure<DebtSegmentationPreviewOptions>(
    builder.Configuration.GetSection(DebtSegmentationPreviewOptions.SectionName));
builder.Services.AddSingleton<PreviewSyncGate>();
builder.Services.AddSingleton<OperationalDebtWorkbookReader>();
builder.Services.AddSingleton<IDebtSegmentationPolicy, DebtSegmentationPolicyV1>();
builder.Services.AddSingleton<DebtSegmentationAnalyzer>();
builder.Services.AddSingleton<IDebtWorkbookLocator, DebtWorkbookLocator>();
builder.Services.AddSingleton<IDebtWorkbookAcquirer, DebtWorkbookAcquirer>();
builder.Services.AddSingleton<IDebtSnapshotStore, FileDebtSnapshotStore>();
builder.Services.AddSingleton<DebtSegmentationPreviewService>();
builder.Services.AddSingleton<IDebtSegmentationPreviewService>(serviceProvider =>
    serviceProvider.GetRequiredService<DebtSegmentationPreviewService>());
builder.Services.AddSingleton<IDebtAutoSyncPipeline>(serviceProvider =>
    serviceProvider.GetRequiredService<DebtSegmentationPreviewService>());
builder.Services.AddSingleton<IDebtAutoSyncSourceProbe, DebtAutoSyncSourceProbe>();
builder.Services.AddSingleton<DebtAutoSyncCoordinator>();
builder.Services.AddSingleton<IDebtAutoSyncStatus>(serviceProvider =>
    serviceProvider.GetRequiredService<DebtAutoSyncCoordinator>());
builder.Services.AddHostedService(serviceProvider =>
    serviceProvider.GetRequiredService<DebtAutoSyncCoordinator>());
builder.Services.Configure<InstallmentMasterOptions>(
    builder.Configuration.GetSection(InstallmentMasterOptions.SectionName));
builder.Services.AddSingleton<InstallmentMasterWorkbookReader>();
builder.Services.AddSingleton<IInstallmentMasterWorkbookAcquirer, InstallmentMasterWorkbookAcquirer>();
builder.Services.AddSingleton<IInstallmentMasterSnapshotStore, FileInstallmentMasterSnapshotStore>();
builder.Services.AddSingleton<InstallmentMasterSyncService>();
builder.Services.AddSingleton<IInstallmentMasterSyncService>(serviceProvider =>
    serviceProvider.GetRequiredService<InstallmentMasterSyncService>());
builder.Services.AddSingleton<IInstallmentMasterSyncPipeline>(serviceProvider =>
    serviceProvider.GetRequiredService<InstallmentMasterSyncService>());
builder.Services.AddSingleton<IInstallmentMasterAutoSyncSourceProbe, InstallmentMasterAutoSyncSourceProbe>();
builder.Services.AddSingleton<InstallmentMasterAutoSyncCoordinator>();
builder.Services.AddSingleton<IInstallmentMasterAutoSyncStatus>(serviceProvider =>
    serviceProvider.GetRequiredService<InstallmentMasterAutoSyncCoordinator>());
builder.Services.AddHostedService(serviceProvider =>
    serviceProvider.GetRequiredService<InstallmentMasterAutoSyncCoordinator>());
builder.Services.AddSingleton<IMonthlyAmountDueService, MonthlyAmountDueService>();
builder.Services.AddSingleton<IMonthlyPerformanceTrendService, MonthlyPerformanceTrendService>();
builder.Services.AddSingleton<IWorkQueueService, WorkQueueService>();

// Portfolio Snapshot review workflow (publishing remains disabled)
builder.Services.AddScoped<IPortfolioSnapshotSource, ExcelPortfolioSnapshotSource>();
builder.Services.Configure<PortfolioSnapshotOptions>(
    builder.Configuration.GetSection(PortfolioSnapshotOptions.SectionName));
builder.Services.AddScoped<IPortfolioSnapshotWorkflowService, PortfolioSnapshotWorkflowService>();

builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen();

var configuredFrontendOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? [];
var frontendOrigins = configuredFrontendOrigins.Length > 0
    ? configuredFrontendOrigins
    : builder.Environment.IsDevelopment()
        ? ["http://localhost:5173"]
        : [];

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        if (frontendOrigins.Length == 0)
        {
            policy.SetIsOriginAllowed(_ => false);
            return;
        }

        policy.WithOrigins(frontendOrigins)
            .AllowCredentials()
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddHealthChecks();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("Login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("Upload", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.Identity?.Name ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});

#endregion

var app = builder.Build();

// Force environment-sensitive configuration validation before accepting traffic.
_ = app.Services.GetRequiredService<IOptions<CoopDataProtectionOptions>>().Value;
_ = app.Services.GetRequiredService<IOptions<ReverseProxyTrustOptions>>().Value;
_ = app.Services.GetRequiredService<IOptions<ImportUploadOptions>>().Value;

if (BootstrapUserCommand.IsRequested(args))
{
    Environment.ExitCode = await BootstrapUserCommand.RunAsync(app.Services, args);
    return;
}

#region Middleware

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (app.Configuration.GetValue<bool>("ReverseProxy:Enabled"))
    app.UseForwardedHeaders();

app.UseHttpsRedirection();

app.UseCors("AllowFrontend");

app.UseAuthentication();

app.UseRateLimiter();

app.UseAuthorization();

app.MapControllers();

app.MapHealthChecks("/health").AllowAnonymous();

#endregion

app.Run();

public partial class Program;
