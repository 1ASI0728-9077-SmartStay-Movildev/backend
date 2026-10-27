using SmartStay.Analytics.Domain.Model.Aggregates;
using SmartStay.Analytics.Interfaces.REST.Resources;

namespace SmartStay.Analytics.Interfaces.REST.Transform;

public static class PerformanceMetricsAssembler
{
    public static PerformanceMetricsResource ToResourceFromEntity(PerformanceMetrics entity)
    {
        return new PerformanceMetricsResource(
            entity.TotalRevenue,
            entity.TotalBookings,
            entity.OccupancyRate,
            entity.CancelledBookings,
            entity.DateGenerated.ToString("yyyy-MM-ddTHH:mm:ssZ")
        );
    }
}
