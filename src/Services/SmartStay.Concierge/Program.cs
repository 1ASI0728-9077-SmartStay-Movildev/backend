using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SmartStay.Concierge.Application;
using SmartStay.Concierge.Application.Ports;
using SmartStay.Concierge.Infrastructure.Clients;
using SmartStay.Concierge.Infrastructure.DeepSeek;
using SmartStay.Concierge.Infrastructure.Http;
using SmartStay.Concierge.Infrastructure.Persistence;
using SmartStay.Concierge.Infrastructure.Security;
using SmartStay.Concierge.Infrastructure.Workers;

var builder = WebApplication.CreateBuilder(args);

// DbContext
var connectionString = builder.Configuration.GetConnectionString("ConciergeConnection")
    ?? builder.Configuration.GetConnectionString("DefaultConnection")
    ?? Environment.GetEnvironmentVariable("MYSQL_CONNECTION_STRING")
    ?? "Server=localhost;Port=3306;Database=smartstay_legacy;User ID=smartstay_user;Password=smartstay_password;SslMode=None;AllowPublicKeyRetrieval=true;";

builder.Services.AddDbContext<ConciergeDbContext>(options =>
{
    ServerVersion serverVersion;
    try { serverVersion = ServerVersion.AutoDetect(connectionString); }
    catch { serverVersion = new MySqlServerVersion(new Version(8, 0, 36)); }

    options.UseMySql(connectionString, serverVersion,
        mysql => mysql.MigrationsHistoryTable("__EFMigrationsHistory_concierge"));
});

// Controllers
builder.Services.AddControllers();

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "SmartStay Concierge API", Version = "v1" });
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

// Rate Limiting (Permissive enough for test runs while protecting endpoints)
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("concierge-policy", opt =>
    {
        opt.PermitLimit = 100;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        opt.QueueLimit = 0;
    });
});

// Security & Token generation
builder.Services.AddSingleton<ServiceTokenGenerator>();

// Candidate Evaluator
builder.Services.AddScoped<ICandidateEvaluator, CandidateEvaluator>();

// Http Clients
builder.Services.AddHttpClient<ILodgingSearch, LodgingSearchClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(5);
});

builder.Services.AddHttpClient<IConciergeModel, DeepSeekClient>(client =>
{
    client.BaseAddress = new Uri("https://api.deepseek.com/");
    client.Timeout = TimeSpan.FromSeconds(12);
});

var iamServiceUrl = builder.Configuration["Services:IamUrl"] ?? "http://iam:8080";
builder.Services.AddHttpClient<IIdentityVerificationClient, IdentityVerificationClient>(client =>
{
    client.BaseAddress = new Uri(iamServiceUrl);
    client.Timeout = TimeSpan.FromSeconds(3);
});

builder.Services.AddHttpClient<IPaymentsValidationClient, PaymentsValidationClient>(client =>
{
    var paymentsUrl = builder.Configuration["Services:PaymentsUrl"] ?? "http://payments:8080";
    client.BaseAddress = new Uri(paymentsUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(5);
});

builder.Services.AddHttpClient<IBookingsClient, BookingsClient>(client =>
{
    var bookingsUrl = builder.Configuration["Services:BookingsUrl"] ?? "http://bookings:8080";
    client.BaseAddress = new Uri(bookingsUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(5);
});

builder.Services.AddHttpClient<IPaymentsClient, PaymentsClient>(client =>
{
    var paymentsUrl = builder.Configuration["Services:PaymentsUrl"] ?? "http://payments:8080";
    client.BaseAddress = new Uri(paymentsUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(5);
});

// Background Worker
builder.Services.AddHostedService<ConciergePurchaseIntentWorker>();

// Use Case
builder.Services.AddScoped<RecommendRooms>();

// Health Checks
builder.Services.AddHealthChecks();

var app = builder.Build();

// Swagger UI
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "SmartStay Concierge API v1");
    c.RoutePrefix = string.Empty;
});

app.UseCors("AllowAll");
app.UseRateLimiter();

// Middleware de verificación de identidad
app.UseMiddleware<ConciergeAuthenticationMiddleware>();

app.MapControllers().RequireRateLimiting("concierge-policy");
app.MapHealthChecks("/health");

app.Run();
