using Microsoft.EntityFrameworkCore;
using SmartStay.Analytics.Application.Internal.QueryServices;
using SmartStay.Analytics.Domain.Repositories;
using SmartStay.Analytics.Domain.Services;
using SmartStay.Analytics.Domain.Services.Ports;
using SmartStay.Analytics.Infrastructure.Adapters;
using SmartStay.Analytics.Infrastructure.Messaging;
using SmartStay.Analytics.Infrastructure.Persistence.EFC.Configuration;
using SmartStay.Analytics.Infrastructure.Persistence.EFC.Repositories;
using SmartStay.Analytics.Infrastructure.Security;
using SmartStay.Analytics.Infrastructure.Web;

var builder = WebApplication.CreateBuilder(args);

// Controllers con convención kebab-case
builder.Services.AddControllers(options =>
{
    options.Conventions.Add(new KebabCaseRouteNamingConvention());
});

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "SmartStay Analytics API", Version = "v1" });
    c.EnableAnnotations();
});

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    });
});

// Database
var connectionString = builder.Configuration.GetConnectionString("AnalyticsConnection")
    ?? builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "server=localhost;port=3306;user=root;password=rootpassword;database=smartstay_analytics;";

builder.Services.AddDbContext<AnalyticsDbContext>(options =>
{
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
});

// Unit of Work
builder.Services.AddScoped<IAnalyticsUnitOfWork, AnalyticsUnitOfWork>();

// Query Ports & Adapters para proyecciones locales de analítica
builder.Services.AddScoped<IRevenueQueryPort, ProjectionRevenueQueryAdapter>();
builder.Services.AddScoped<IBookingStatsQueryPort, ProjectionBookingStatsQueryAdapter>();
builder.Services.AddScoped<ICatalogQueryPort, ProjectionCatalogQueryAdapter>();

// Repositories & Services
builder.Services.AddScoped<IAnalyticsRepository, AnalyticsRepository>();
builder.Services.AddScoped<IAnalyticsQueryService, AnalyticsQueryService>();

// IAM Client
var iamUrl = builder.Configuration["Services:IamUrl"] ?? "http://iam:8080";
builder.Services.AddHttpClient<IIdentityVerificationClient, IdentityVerificationClient>(client =>
{
    client.BaseAddress = new Uri(iamUrl);
    client.Timeout = TimeSpan.FromSeconds(3);
});

// Worker proyector de ActiveMQ
builder.Services.AddHostedService<AnalyticsActiveMqConsumerWorker>();

// Health Checks
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "SmartStay Analytics API v1");
    c.RoutePrefix = string.Empty;
});

app.UseCors("AllowAll");

// Middleware de autenticación
app.UseMiddleware<AnalyticsAuthenticationMiddleware>();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
