using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using SmartStay.Analytics.Application.Internal.QueryServices;
using SmartStay.Analytics.Domain.Model.Entities;
using SmartStay.Analytics.Domain.Model.Queries;
using SmartStay.Analytics.Infrastructure.Adapters;
using SmartStay.Analytics.Infrastructure.Messaging;
using SmartStay.Analytics.Infrastructure.Persistence.EFC.Configuration;
using SmartStay.Analytics.Infrastructure.Persistence.EFC.Repositories;
using Xunit;

namespace SmartStay.Analytics.Tests;

public class AnalyticsProjectionTests
{
    private AnalyticsDbContext CreateInMemoryContext(string? dbName = null)
    {
        var options = new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseInMemoryDatabase(databaseName: dbName ?? Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AnalyticsDbContext(options);
    }

    [Fact]
    [Trait("Service", "Analytics")]
    [Trait("Category", "Projections")]
    public async Task Monthly_Metrics_Calculates_Net_Revenue_And_Occupancy_Rate()
    {
        using var context = CreateInMemoryContext();
        var now = DateTime.UtcNow;
        var startOfMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        // Arrange Facts:
        // 1. Two payments: $200 and $300, with $50 refunded on the first
        var payment1 = new RevenueFact(1, 10, 1, 200m, "USD", startOfMonth.AddDays(2));
        payment1.MarkRefunded(50m);
        var payment2 = new RevenueFact(2, 11, 1, 300m, "USD", startOfMonth.AddDays(5));

        // One payment outside of current month (last month)
        var paymentOld = new RevenueFact(3, 9, 1, 1000m, "USD", startOfMonth.AddDays(-5));

        await context.RevenueFacts.AddRangeAsync(payment1, payment2, paymentOld);

        // 2. Bookings in current month: 3 bookings (2 Confirmed, 1 Cancelled)
        var b1 = new BookingFact(10, 101, 1, "Confirmed", startOfMonth.AddDays(2), startOfMonth.AddDays(4), startOfMonth.AddDays(2));
        var b2 = new BookingFact(11, 102, 1, "Confirmed", startOfMonth.AddDays(5), startOfMonth.AddDays(7), startOfMonth.AddDays(5));
        var b3 = new BookingFact(12, 103, 1, "Cancelled", startOfMonth.AddDays(1), startOfMonth.AddDays(3), startOfMonth.AddDays(1));
        await context.BookingFacts.AddRangeAsync(b1, b2, b3);

        // 3. Catalog: 10 bookable rooms
        for (int i = 1; i <= 10; i++)
        {
            await context.CatalogSnapshots.AddAsync(new CatalogSnapshot(100 + i, 1, true, 100m));
        }

        await context.SaveChangesAsync();

        // Query ports & repository
        var revenuePort = new ProjectionRevenueQueryAdapter(context);
        var bookingStatsPort = new ProjectionBookingStatsQueryAdapter(context);
        var catalogPort = new ProjectionCatalogQueryAdapter(context);
        var repository = new AnalyticsRepository(context, revenuePort, bookingStatsPort, catalogPort);
        var queryService = new AnalyticsQueryService(repository);

        // Act
        var metrics = await queryService.Handle(new GetMonthlyPerformanceQuery());

        // Assert
        // Net Revenue: (200 - 50) + 300 = 450
        metrics.TotalRevenue.Should().Be(450.00m);
        metrics.TotalBookings.Should().Be(3);
        metrics.CancelledBookings.Should().Be(1);
        // Occupancy Rate: 2 confirmed / 10 rooms = 20%
        metrics.OccupancyRate.Should().Be(20.0);
    }

    [Fact]
    [Trait("Service", "Analytics")]
    [Trait("Category", "Messaging")]
    public async Task ActiveMq_Consumer_Worker_Projects_Payment_And_Booking_Events_Idempotently()
    {
        var dbName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<AnalyticsDbContext>(opt =>
            opt.UseInMemoryDatabase(dbName)
               .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        services.AddScoped<Domain.Repositories.IAnalyticsUnitOfWork, AnalyticsUnitOfWork>();
        services.AddScoped<Domain.Services.Ports.IRevenueQueryPort, ProjectionRevenueQueryAdapter>();
        services.AddScoped<Domain.Services.Ports.IBookingStatsQueryPort, ProjectionBookingStatsQueryAdapter>();
        services.AddScoped<Domain.Services.Ports.ICatalogQueryPort, ProjectionCatalogQueryAdapter>();
        services.AddScoped<Domain.Repositories.IAnalyticsRepository, AnalyticsRepository>();

        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var mockLogger = new Mock<ILogger<AnalyticsActiveMqConsumerWorker>>();
        var mockConfig = new Mock<Microsoft.Extensions.Configuration.IConfiguration>();

        var worker = new AnalyticsActiveMqConsumerWorker(scopeFactory, mockConfig.Object, mockLogger.Object);

        var eventId = Guid.NewGuid();
        var paymentJson = $$"""
        {
            "eventId": "{{eventId}}",
            "eventType": "payments.payment-completed",
            "schemaVersion": 1,
            "occurredAtUtc": "{{DateTime.UtcNow:O}}",
            "correlationId": "corr-1",
            "aggregateId": "100",
            "aggregateVersion": 1,
            "data": {
                "paymentId": 100,
                "bookingId": 25,
                "hotelId": 1,
                "amount": 250.00,
                "currency": "USD"
            }
        }
        """;

        // Act 1: Process event
        await worker.ProcessEventMessageAsync(paymentJson, CancellationToken.None);

        using (var verifyContext = CreateInMemoryContext(dbName))
        {
            var fact = await verifyContext.RevenueFacts.FindAsync(100);
            fact.Should().NotBeNull();
            fact!.Amount.Should().Be(250.00m);
            (await verifyContext.InboxMessages.CountAsync()).Should().Be(1);
        }

        // Act 2: Process exact duplicate message
        await worker.ProcessEventMessageAsync(paymentJson, CancellationToken.None);

        // Assert: Idempotent - no duplicate revenue facts or extra inbox records
        using (var verifyContext = CreateInMemoryContext(dbName))
        {
            (await verifyContext.RevenueFacts.CountAsync()).Should().Be(1);
            (await verifyContext.InboxMessages.CountAsync()).Should().Be(1);
        }
    }
}
