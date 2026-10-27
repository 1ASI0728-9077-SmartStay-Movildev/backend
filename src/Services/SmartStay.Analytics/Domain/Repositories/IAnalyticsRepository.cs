using Microsoft.EntityFrameworkCore.Storage;
using SmartStay.Analytics.Domain.Model.Aggregates;
using SmartStay.Analytics.Domain.Model.Entities;

namespace SmartStay.Analytics.Domain.Repositories;

public interface IAnalyticsRepository
{
    Task<PerformanceMetrics> GetMonthlyMetricsAsync();

    // Projections management
    Task UpsertRevenueFactAsync(RevenueFact fact);
    Task UpsertBookingFactAsync(BookingFact fact);
    Task UpsertCatalogSnapshotAsync(CatalogSnapshot snapshot);
    Task AddInboxMessageAsync(InboxMessage inboxMessage);
    Task<bool> HasProcessedInboxEventAsync(string consumerName, Guid eventId, CancellationToken ct = default);
}
