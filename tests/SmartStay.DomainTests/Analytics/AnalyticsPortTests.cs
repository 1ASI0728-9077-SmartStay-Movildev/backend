using BackendAwSmartstay.API.Analytics.Domain.Services.Ports;
using BackendAwSmartstay.API.Analytics.Infrastructure.Persistence.EFC.Repositories;
using FluentAssertions;
using Xunit;

namespace SmartStay.DomainTests.Analytics;

public class AnalyticsPortTests
{
    private class FakeRevenueQueryPort(decimal revenue) : IRevenueQueryPort
    {
        public Task<decimal> GetTotalRevenueAsync(DateTime startUtc, DateTime endUtcExclusive)
            => Task.FromResult(revenue);
    }

    private class FakeBookingStatsQueryPort(int total, int cancelled, int confirmed) : IBookingStatsQueryPort
    {
        public Task<(int TotalBookings, int CancelledBookings, int ConfirmedBookings)> GetBookingStatsAsync(DateTime startUtc, DateTime endUtcExclusive)
            => Task.FromResult((total, cancelled, confirmed));
    }

    private class FakeCatalogQueryPort(int rooms) : ICatalogQueryPort
    {
        public Task<int> GetTotalRoomsCountAsync()
            => Task.FromResult(rooms);
    }

    [Fact]
    [Trait("Area", "Analytics")]
    [Trait("Risk", "Decoupling")]
    public async Task AnalyticsRepository_calculates_metrics_correctly_from_ports()
    {
        // Arrange: 10 rooms, 5 total bookings, 1 cancelled, 4 confirmed, $1200 revenue
        var revenuePort = new FakeRevenueQueryPort(1200.00m);
        var bookingPort = new FakeBookingStatsQueryPort(total: 5, cancelled: 1, confirmed: 4);
        var catalogPort = new FakeCatalogQueryPort(rooms: 10);

        var repository = new AnalyticsRepository(revenuePort, bookingPort, catalogPort);

        // Act
        var metrics = await repository.GetMonthlyMetricsAsync();

        // Assert
        metrics.TotalRevenue.Should().Be(1200.00m);
        metrics.TotalBookings.Should().Be(5);
        metrics.CancelledBookings.Should().Be(1);
        metrics.OccupancyRate.Should().Be(40.00); // (4 confirmed / 10 rooms) * 100
    }

    [Fact]
    [Trait("Area", "Analytics")]
    [Trait("Risk", "BoundaryConditions")]
    public async Task AnalyticsRepository_handles_zero_rooms_without_division_by_zero()
    {
        // Arrange
        var revenuePort = new FakeRevenueQueryPort(0m);
        var bookingPort = new FakeBookingStatsQueryPort(0, 0, 0);
        var catalogPort = new FakeCatalogQueryPort(rooms: 0);

        var repository = new AnalyticsRepository(revenuePort, bookingPort, catalogPort);

        // Act
        var metrics = await repository.GetMonthlyMetricsAsync();

        // Assert
        metrics.OccupancyRate.Should().Be(0);
        metrics.TotalRevenue.Should().Be(0);
    }
}
