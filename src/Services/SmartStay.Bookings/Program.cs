using Microsoft.EntityFrameworkCore;
using SmartStay.Bookings.Application.Internal.CommandServices;
using SmartStay.Bookings.Application.Internal.QueryServices;
using SmartStay.Bookings.Domain.Repositories;
using SmartStay.Bookings.Domain.Services;
using SmartStay.Bookings.Infrastructure.Clients;
using SmartStay.Bookings.Infrastructure.Persistence.EFC.Configuration;
using SmartStay.Bookings.Infrastructure.Persistence.EFC.Repositories;
using SmartStay.Bookings.Infrastructure.Security;
using SmartStay.Bookings.Infrastructure.Web;

var builder = WebApplication.CreateBuilder(args);

// Controllers
builder.Services.AddControllers(options =>
{
    options.Conventions.Add(new KebabCaseRouteNamingConvention());
});

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "SmartStay Bookings API", Version = "v1" });
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
var connectionString = builder.Configuration.GetConnectionString("BookingsConnection")
    ?? builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "server=localhost;port=3306;user=root;password=rootpassword;database=smartstay_bookings;";

builder.Services.AddDbContext<BookingsDbContext>(options =>
{
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
});

// Unit of Work
builder.Services.AddScoped<IBookingsUnitOfWork, BookingsUnitOfWork>();

// Repositories
builder.Services.AddScoped<IBookingRepository, BookingRepository>();

// Command & Query Services
builder.Services.AddScoped<IBookingCommandService, BookingCommandService>();
builder.Services.AddScoped<IBookingQueryService, BookingQueryService>();

// Catalog Client para Accommodations
var accommodationsUrl = builder.Configuration["Services:AccommodationsUrl"] ?? "http://accommodations:8080";
builder.Services.AddHttpClient<IRoomCatalogClient, RoomCatalogClient>(client =>
{
    client.BaseAddress = new Uri(accommodationsUrl);
    client.Timeout = TimeSpan.FromSeconds(3);
});

// Identity Client para IAM
var iamUrl = builder.Configuration["Services:IamUrl"] ?? "http://legacy:8080";
builder.Services.AddHttpClient<IIdentityVerificationClient, IdentityVerificationClient>(client =>
{
    client.BaseAddress = new Uri(iamUrl);
    client.Timeout = TimeSpan.FromSeconds(3);
});

// Health Checks
builder.Services.AddHealthChecks();

// Background Consumer Worker para pagos de ActiveMQ
builder.Services.AddHostedService<SmartStay.Bookings.Infrastructure.Messaging.BookingsPaymentConsumerWorker>();
// Background Worker para expiración de reservas y liberación de cerrojos
builder.Services.AddHostedService<SmartStay.Bookings.Infrastructure.Workers.BookingExpirationWorker>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "SmartStay Bookings API v1");
    c.RoutePrefix = string.Empty;
});

app.UseCors("AllowAll");

// Middleware de autenticación
app.UseMiddleware<BookingsAuthenticationMiddleware>();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
