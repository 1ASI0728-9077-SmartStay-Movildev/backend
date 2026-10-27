using BackendAwSmartstay.API.Analytics.Application.Internal.QueryServices;
using BackendAwSmartstay.API.Analytics.Domain.Repositories;
using BackendAwSmartstay.API.Analytics.Domain.Services;
using BackendAwSmartstay.API.Analytics.Domain.Services.Ports;
using BackendAwSmartstay.API.Analytics.Infrastructure.Adapters.Local;
using BackendAwSmartstay.API.Analytics.Infrastructure.Persistence.EFC.Repositories;

namespace BackendAwSmartstay.API.Analytics.Infrastructure.Interfaces.ASP.Configuration.Extensions;

public static class WebApplicationBuilderExtensions
{
    public static void AddAnalyticsContextServices(this WebApplicationBuilder builder)
    {
        // Analytics Query Ports & Adapters (desacoplamiento de tablas directas)
        builder.Services.AddScoped<IRevenueQueryPort, LocalRevenueQueryAdapter>();
        builder.Services.AddScoped<IBookingStatsQueryPort, LocalBookingStatsQueryAdapter>();
        builder.Services.AddScoped<ICatalogQueryPort, LocalCatalogQueryAdapter>();

        // Repositories
        builder.Services.AddScoped<IAnalyticsRepository, AnalyticsRepository>();

        // Query Services
        builder.Services.AddScoped<IAnalyticsQueryService, AnalyticsQueryService>();
        
        // Note: Commands are not implemented yet as Analytics is currently Read-Only
    }
}
