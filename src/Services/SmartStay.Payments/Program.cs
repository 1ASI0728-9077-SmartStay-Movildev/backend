using Microsoft.EntityFrameworkCore;
using SmartStay.Payments.Application.Internal.CommandServices;
using SmartStay.Payments.Application.Internal.QueryServices;
using SmartStay.Payments.Domain.Repositories;
using SmartStay.Payments.Domain.Services;
using SmartStay.Payments.Infrastructure.Clients;
using SmartStay.Payments.Infrastructure.Messaging;
using SmartStay.Payments.Infrastructure.Persistence.EFC.Configuration;
using SmartStay.Payments.Infrastructure.Persistence.EFC.Repositories;
using SmartStay.Payments.Infrastructure.Security;
using SmartStay.Payments.Infrastructure.Web;

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
    c.SwaggerDoc("v1", new() { Title = "SmartStay Payments API", Version = "v1" });
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
var connectionString = builder.Configuration.GetConnectionString("PaymentsConnection")
    ?? builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "server=localhost;port=3306;user=root;password=rootpassword;database=smartstay_payments;";

builder.Services.AddDbContext<PaymentsDbContext>(options =>
{
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
});

// Unit of Work
builder.Services.AddScoped<IPaymentsUnitOfWork, PaymentsUnitOfWork>();

// Repositories
builder.Services.AddScoped<IPaymentRepository, PaymentRepository>();

// Command & Query Services
builder.Services.AddScoped<IPaymentCommandService, PaymentCommandService>();
builder.Services.AddScoped<IPaymentQueryService, PaymentQueryService>();

// HTTP Client hacia Bookings para validación de elegibilidad
var bookingsUrl = builder.Configuration["Services:BookingsUrl"] ?? "http://bookings:8080";
builder.Services.AddHttpClient<IBookingEligibilityClient, BookingEligibilityClient>(client =>
{
    client.BaseAddress = new Uri(bookingsUrl);
    client.Timeout = TimeSpan.FromSeconds(3);
});

// HTTP Client hacia IAM para validación de identidad
var iamUrl = builder.Configuration["Services:IamUrl"] ?? "http://legacy:8080";
builder.Services.AddHttpClient<IIdentityVerificationClient, IdentityVerificationClient>(client =>
{
    client.BaseAddress = new Uri(iamUrl);
    client.Timeout = TimeSpan.FromSeconds(3);
});

// Workers de mensajería asíncrona
builder.Services.AddHostedService<OutboxPublisherWorker>();
builder.Services.AddHostedService<PaymentsCompensationConsumerWorker>();

// Health Checks
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "SmartStay Payments API v1");
    c.RoutePrefix = string.Empty;
});

app.UseCors("AllowAll");

// Middleware de autenticación
app.UseMiddleware<PaymentsAuthenticationMiddleware>();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
