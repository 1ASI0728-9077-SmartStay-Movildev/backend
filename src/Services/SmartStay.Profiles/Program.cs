using Microsoft.EntityFrameworkCore;
using SmartStay.Profiles.Application.Internal.CommandServices;
using SmartStay.Profiles.Application.Internal.QueryServices;
using SmartStay.Profiles.Domain.Repositories;
using SmartStay.Profiles.Domain.Services;
using SmartStay.Profiles.Infrastructure.Persistence.EFC.Configuration;
using SmartStay.Profiles.Infrastructure.Persistence.EFC.Repositories;
using SmartStay.Profiles.Infrastructure.Security;
using SmartStay.Profiles.Infrastructure.Web;

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
    c.SwaggerDoc("v1", new() { Title = "SmartStay Profiles API", Version = "v1" });
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
var connectionString = builder.Configuration.GetConnectionString("ProfilesConnection")
    ?? builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "server=localhost;port=3306;user=root;password=rootpassword;database=smartstay_profiles;";

builder.Services.AddDbContext<ProfilesDbContext>(options =>
{
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
});

// Unit of Work
builder.Services.AddScoped<IProfilesUnitOfWork, ProfilesUnitOfWork>();

// Repositories
builder.Services.AddScoped<IProfileRepository, ProfileRepository>();

// Command & Query Services
builder.Services.AddScoped<IProfileCommandService, ProfileCommandService>();
builder.Services.AddScoped<IProfileQueryService, ProfileQueryService>();

// IAM Verification Client
var iamUrl = builder.Configuration["Services:IamUrl"] ?? "http://legacy:8080";
builder.Services.AddHttpClient<IIdentityVerificationClient, IdentityVerificationClient>(client =>
{
    client.BaseAddress = new Uri(iamUrl);
    client.Timeout = TimeSpan.FromSeconds(3);
});

// Health Checks
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "SmartStay Profiles API v1");
    c.RoutePrefix = string.Empty;
});

app.UseCors("AllowAll");

// Middleware de autenticación
app.UseMiddleware<ProfilesAuthenticationMiddleware>();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
