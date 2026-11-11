using BackendAwSmartstay.API.Accommodations.Application.Internal.CommandServices;
using BackendAwSmartstay.API.Accommodations.Application.Internal.QueryServices;
using BackendAwSmartstay.API.Accommodations.Application.Internal.Services;
using BackendAwSmartstay.API.Accommodations.Domain.Repositories;
using BackendAwSmartstay.API.Accommodations.Domain.Services;
using BackendAwSmartstay.API.Accommodations.Infrastructure.Persistence.EFC.Repositories;
using Microsoft.EntityFrameworkCore;
using SmartStay.Accommodations.Infrastructure.Persistence.EFC.Configuration;
using SmartStay.Accommodations.Infrastructure.Security;
using SmartStay.Accommodations.Infrastructure.Web;

var builder = WebApplication.CreateBuilder(args);

// Controllers con convención kebab-case
builder.Services.AddControllers(options =>
{
    options.Conventions.Add(new KebabCaseRouteNamingConvention());
});

// Swagger / OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "SmartStay Accommodations API", Version = "v1" });
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
var connectionString = builder.Configuration.GetConnectionString("AccommodationsConnection")
    ?? builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Configure ConnectionStrings__AccommodationsConnection.");

if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("Configure the service database connection string.");

builder.Services.AddDbContext<AccommodationsDbContext>(options =>
{
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString),
        mysql => mysql.MigrationsHistoryTable("__EFMigrationsHistory_accommodations"));
});

// Unit of Work
builder.Services.AddScoped<IAccommodationsUnitOfWork, AccommodationsUnitOfWork>();

// Repositories
builder.Services.AddScoped<IHotelRepository, HotelRepository>();
builder.Services.AddScoped<IRoomRepository, RoomRepository>();
builder.Services.AddScoped<IRoomTypeRepository, RoomTypeRepository>();
builder.Services.AddScoped<IAccommodationOptionsRepository, AccommodationOptionsRepository>();

// Command Services
builder.Services.AddScoped<IHotelCommandService, HotelCommandService>();
builder.Services.AddScoped<IRoomCommandService, RoomCommandService>();
builder.Services.AddScoped<IRoomTypeCommandService, RoomTypeCommandService>();
builder.Services.AddScoped<IAccommodationOptionsCommandService, AccommodationOptionsCommandService>();

// Query Services
builder.Services.AddScoped<IHotelQueryService, HotelQueryService>();
builder.Services.AddScoped<IRoomQueryService, RoomQueryService>();
builder.Services.AddScoped<IRoomTypeQueryService, RoomTypeQueryService>();
builder.Services.AddScoped<IAccommodationOptionsQueryService, AccommodationOptionsQueryService>();

// Cliente de validación de identidad con IAM interno
var iamServiceUrl = builder.Configuration["Services:IamUrl"] ?? "http://legacy:8080";
builder.Services.AddHttpClient<IIdentityVerificationClient, IdentityVerificationClient>(client =>
{
    client.BaseAddress = new Uri(iamServiceUrl);
    client.Timeout = TimeSpan.FromSeconds(3);
});

// Health Checks
builder.Services.AddHealthChecks();

var app = builder.Build();

// Swagger UI
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "SmartStay Accommodations API v1");
    c.RoutePrefix = string.Empty;
});

app.UseCors("AllowAll");

// Archivos estáticos para assets de recorridos virtuales 360
app.UseStaticFiles();

// Middleware de verificación de identidad compatible
app.UseMiddleware<AccommodationsAuthenticationMiddleware>();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
