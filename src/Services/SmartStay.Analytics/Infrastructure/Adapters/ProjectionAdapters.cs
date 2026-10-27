using Microsoft.EntityFrameworkCore;
using SmartStay.Analytics.Domain.Services.Ports;
using SmartStay.Analytics.Infrastructure.Persistence.EFC.Configuration;

namespace SmartStay.Analytics.Infrastructure.Adapters;

public class ProjectionRevenueQueryAdapter(AnalyticsDbContext context) : IRevenueQueryPort
{
    public async Task<decimal> GetTotalRevenueAsync(DateTime startUtc, DateTime endUtcExclusive)
    {
        return await context.RevenueFacts
            .Where(f => f.OccurredAtUtc >= startUtc && f.OccurredAtUtc < endUtcExclusive)
            .SumAsync(f => f.Amount - f.RefundAmount);
    }
}

public class ProjectionBookingStatsQueryAdapter(AnalyticsDbContext context) : IBookingStatsQueryPort
{
    public async Task<(int TotalBookings, int CancelledBookings, int ConfirmedBookings)> GetBookingStatsAsync(DateTime startUtc, DateTime endUtcExclusive)
    {
        var bookings = await context.BookingFacts
            .Where(b => b.OccurredAtUtc >= startUtc && b.OccurredAtUtc < endUtcExclusive)
            .ToListAsync();

        var total = bookings.Count;
        var cancelled = bookings.Count(b => b.Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase));
        var confirmed = bookings.Count(b => b.Status.Equals("Confirmed", StringComparison.OrdinalIgnoreCase));

        return (total, cancelled, confirmed);
    }
}

public class ProjectionCatalogQueryAdapter(AnalyticsDbContext context) : ICatalogQueryPort
{
    public async Task<int> GetTotalRoomsCountAsync()
    {
        return await context.CatalogSnapshots.CountAsync(c => c.IsBookable);
    }
}
