using Microsoft.EntityFrameworkCore;
using SmartStay.IAM.Application.Internal.CommandServices;
using SmartStay.IAM.Application.Internal.QueryServices;
using SmartStay.IAM.Application.OutboundServices;
using SmartStay.IAM.Domain.Repositories;
using SmartStay.IAM.Domain.Services;
using SmartStay.IAM.Infrastructure.Hashing.BCrypt.Services;
using SmartStay.IAM.Infrastructure.Persistence.EFC.Configuration;
using SmartStay.IAM.Infrastructure.Persistence.EFC.Repositories;
using SmartStay.IAM.Infrastructure.Pipeline.Middleware.Components;
using SmartStay.IAM.Infrastructure.Seed;
using SmartStay.IAM.Infrastructure.Tokens.JWT.Configuration;
using SmartStay.IAM.Infrastructure.Tokens.JWT.Services;

var builder = WebApplication.CreateBuilder(args);

// Controllers
builder.Services.AddControllers(options =>
{
    options.Conventions.Add(new SmartStay.IAM.Infrastructure.Web.KebabCaseRouteNamingConvention());
});

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "SmartStay IAM API", Version = "v1" });
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
var connectionString = builder.Configuration.GetConnectionString("IamConnection")
    ?? builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Configure ConnectionStrings__IamConnection.");

if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("Configure the service database connection string.");

builder.Services.AddDbContext<IamDbContext>(options =>
{
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString),
        mysql => mysql.MigrationsHistoryTable("__EFMigrationsHistory_iam"));
});

// Settings
builder.Services.Configure<TokenSettings>(builder.Configuration.GetSection("TokenSettings"));

// Dependencies
builder.Services.AddScoped<IIamUnitOfWork, IamUnitOfWork>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IHashingService, HashingService>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IRoleAuthorizationService, RoleAuthorizationService>();
builder.Services.AddScoped<IUserScopeService, UserScopeService>();
builder.Services.AddScoped<IUserCommandService, UserCommandService>();
builder.Services.AddScoped<IUserQueryService, UserQueryService>();

// Health Checks
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "SmartStay IAM API v1");
    c.RoutePrefix = string.Empty;
});

app.UseCors("AllowAll");

// Middleware de autenticación y autorización interna
app.UseMiddleware<RequestAuthorizationMiddleware>();

app.MapControllers();
app.MapHealthChecks("/health");

// Sembrado opcional de administrador inicial
using (var scope = app.Services.CreateScope())
{
    try
    {
        await IamSeeder.SeedAsync(scope.ServiceProvider);
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Seeding initial admin user failed or skipped.");
    }
}

app.Run();
