using Microsoft.EntityFrameworkCore;
using SmartStay.Analytics.Domain.Model.Aggregates;
using SmartStay.Analytics.Domain.Model.Entities;
using SmartStay.Analytics.Domain.Repositories;
using SmartStay.Analytics.Domain.Services.Ports;
using SmartStay.Analytics.Infrastructure.Persistence.EFC.Configuration;

namespace SmartStay.Analytics.Infrastructure.Persistence.EFC.Repositories;

public class AnalyticsRepository(
    AnalyticsDbContext context,
    IRevenueQueryPort revenueQueryPort,
    IBookingStatsQueryPort bookingStatsQueryPort,
    ICatalogQueryPort catalogQueryPort) : IAnalyticsRepository
{
    public async Task<PerformanceMetrics> GetMonthlyMetricsAsync()
    {
        var now = DateTime.UtcNow;
        var startOfMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var endOfMonthExclusive = startOfMonth.AddMonths(1);

        // 1. Ingresos totales netos en el intervalo exacto [inicioMes, inicioMesSiguiente)
        var totalRevenue = await revenueQueryPort.GetTotalRevenueAsync(startOfMonth, endOfMonthExclusive);

        // 2. Estadísticas de reservas del mes
        var (totalBookings, cancelledBookings, confirmedBookings) =
            await bookingStatsQueryPort.GetBookingStatsAsync(startOfMonth, endOfMonthExclusive);

        // 3. Tasa de ocupación
        var totalRooms = await catalogQueryPort.GetTotalRoomsCountAsync();

        double occupancyRate = 0;
        if (totalRooms > 0 && totalBookings > 0)
        {
            occupancyRate = ((double)confirmedBookings / totalRooms) * 100;
        }

        return new PerformanceMetrics
        {
            TotalRevenue = totalRevenue,
            TotalBookings = totalBookings,
            CancelledBookings = cancelledBookings,
            OccupancyRate = Math.Round(occupancyRate, 2)
        };
    }

    public async Task UpsertRevenueFactAsync(RevenueFact fact)
    {
        var existing = await context.RevenueFacts.FindAsync(fact.PaymentId);
        if (existing is null)
        {
            await context.RevenueFacts.AddAsync(fact);
        }
        else if (fact.IsRefunded)
        {
            existing.MarkRefunded(fact.RefundAmount);
            context.RevenueFacts.Update(existing);
        }
    }

    public async Task UpsertBookingFactAsync(BookingFact fact)
    {
        var existing = await context.BookingFacts.FindAsync(fact.BookingId);
        if (existing is null)
        {
            await context.BookingFacts.AddAsync(fact);
        }
        else
        {
            existing.UpdateStatus(fact.Status);
            context.BookingFacts.Update(existing);
        }
    }

    public async Task UpsertCatalogSnapshotAsync(CatalogSnapshot snapshot)
    {
        var existing = await context.CatalogSnapshots.FindAsync(snapshot.RoomId);
        if (existing is null)
        {
            await context.CatalogSnapshots.AddAsync(snapshot);
        }
        else
        {
            existing.Update(snapshot.IsBookable, snapshot.NightlyPrice);
            context.CatalogSnapshots.Update(existing);
        }
    }

    public async Task AddInboxMessageAsync(InboxMessage inboxMessage)
    {
        await context.InboxMessages.AddAsync(inboxMessage);
    }

    public async Task<bool> HasProcessedInboxEventAsync(string consumerName, Guid eventId, CancellationToken ct = default)
    {
        return await context.InboxMessages
            .AnyAsync(m => m.ConsumerName == consumerName && m.EventId == eventId, ct);
    }
}
