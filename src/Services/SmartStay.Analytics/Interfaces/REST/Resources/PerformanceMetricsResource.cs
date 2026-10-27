namespace SmartStay.Analytics.Interfaces.REST.Resources;

public record PerformanceMetricsResource(
    decimal TotalRevenue,
    int TotalBookings,
    double OccupancyRate,
    int CancelledBookings,
    string GeneratedAt
);
