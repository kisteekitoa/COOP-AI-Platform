using COOPAI.API.Data;
using COOPAI.API.Services.Dashboard;
using COOPAI.API.Services.Import;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

#region Services

builder.Services.AddDbContext<CoopDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddControllers();

// Import Engine
builder.Services.AddScoped<ExcelReader>();
builder.Services.AddScoped<ImportValidator>();
builder.Services.AddScoped<MemberImporter>();
builder.Services.AddScoped<LoanImporter>();
builder.Services.AddScoped<IExcelImportService, ExcelImportService>();

// Dashboard
builder.Services.AddScoped<IDashboardService, DashboardService>();

builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy
            .AllowAnyOrigin()
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

app.MapControllers();

app.MapHealthChecks("/health");

#endregion

app.Run();