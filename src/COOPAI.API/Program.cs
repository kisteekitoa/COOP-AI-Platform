using COOPAI.API.Data;
using COOPAI.API.Models.Auth;
using COOPAI.API.Security;
using COOPAI.API.Services.Auth;
using COOPAI.API.Services.Dashboard;
using COOPAI.API.Services.Import;
using COOPAI.API.Services.PortfolioSnapshots;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

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
    options.AddPolicy(CoopPolicies.ManagerOnly, policy =>
        policy.RequireAuthenticatedUser().RequireRole(CoopRoles.Manager));
});

// Import Engine
builder.Services.AddScoped<ExcelReader>();
builder.Services.AddScoped<ImportValidator>();
builder.Services.AddScoped<MemberImporter>();
builder.Services.AddScoped<LoanImporter>();
builder.Services.AddScoped<IExcelImportService, ExcelImportService>();

// Dashboard
builder.Services.AddScoped<IDashboardService, DashboardService>();

// Portfolio Snapshot review workflow (publishing remains disabled)
builder.Services.AddScoped<IPortfolioSnapshotSource, ExcelPortfolioSnapshotSource>();
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

#endregion

var app = builder.Build();

#region Middleware

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors("AllowFrontend");

app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();

app.MapHealthChecks("/health");

#endregion

app.Run();

public partial class Program;
