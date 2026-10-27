using SmartStay.Concierge.Application;
using SmartStay.Concierge.Application.Ports;
using SmartStay.Concierge.Infrastructure.DeepSeek;
using SmartStay.Concierge.Infrastructure.Http;
using SmartStay.Concierge.Infrastructure.Security;

var builder = WebApplication.CreateBuilder(args);

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

// Middleware de verificación de identidad
app.UseMiddleware<ConciergeAuthenticationMiddleware>();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
