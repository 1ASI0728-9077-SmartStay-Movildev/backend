using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using SmartStay.Bookings.Application.Internal.CommandServices;
using SmartStay.Bookings.Domain.Model.Aggregates;
using SmartStay.Bookings.Domain.Model.Commands;
using SmartStay.Bookings.Domain.Model.Entities;
using SmartStay.Bookings.Domain.Repositories;
using SmartStay.Bookings.Infrastructure.Clients;
using SmartStay.Bookings.Infrastructure.Persistence.EFC.Configuration;
using SmartStay.Bookings.Infrastructure.Persistence.EFC.Repositories;
using Xunit;

namespace SmartStay.Bookings.Tests;

public class BookingCancellationRefundTests
{
    private (BookingsDbContext Context, BookingCommandService Service) CreateTestRig()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<BookingsDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var context = new BookingsDbContext(options);
        var repo = new BookingRepository(context);
        var uow = new BookingsUnitOfWork(context);

        var mockCatalog = new Mock<IRoomCatalogClient>();
        var mockLogger = new Mock<ILogger<BookingCommandService>>();

        var service = new BookingCommandService(repo, uow, mockCatalog.Object, mockLogger.Object);
        return (context, service);
    }

    [Fact]
    [Trait("Service", "Bookings")]
    [Trait("Category", "CancellationRefund")]
    public async Task Cancel_Confirmed_Booking_Enqueues_Outbox_Refund_And_Releases_Locks()
    {
        // Arrange
        var (context, service) = CreateTestRig();
        var checkIn = DateTime.UtcNow.Date.AddDays(1);
        var checkOut = checkIn.AddDays(2);

        var booking = new Booking(101, 1, "Guest One", "guest1@example.com", checkIn, checkOut, 150m, 150m, "USD");
        booking.AddNightLock(new RoomNightLock(101, DateOnly.FromDateTime(checkIn), booking.Id));
        booking.Confirm(paymentId: 99);

        context.Bookings.Add(booking);
        await context.SaveChangesAsync();

        // Act
        var result = await service.Handle(new CancelBookingCommand(booking.Id));

        // Assert
        result.Should().NotBeNull();
        result!.Status.Should().Be(BookingStatus.Cancelled);

        // Verify night locks are deleted from database
        var remainingLocks = await context.RoomNightLocks.Where(l => l.BookingId == booking.Id).ToListAsync();
        remainingLocks.Should().BeEmpty();

        // Verify outbox message
        var outbox = await context.OutboxMessages.FirstOrDefaultAsync();
        outbox.Should().NotBeNull();
        outbox!.EventType.Should().Be("bookings.payment-refund-requested");
        outbox.Payload.Should().Contain("\"paymentId\":99");
        outbox.Payload.Should().Contain($"\"bookingId\":{booking.Id}");
    }

    [Fact]
    [Trait("Service", "Bookings")]
    [Trait("Category", "CancellationRefund")]
    public async Task Cancel_PendingPayment_Booking_Does_Not_Enqueue_Outbox_Refund()
    {
        // Arrange
        var (context, service) = CreateTestRig();
        var checkIn = DateTime.UtcNow.Date.AddDays(1);
        var checkOut = checkIn.AddDays(2);

        var booking = new Booking(101, 1, "Guest One", "guest1@example.com", checkIn, checkOut, 150m, 150m, "USD");
        booking.AddNightLock(new RoomNightLock(101, DateOnly.FromDateTime(checkIn), booking.Id));

        context.Bookings.Add(booking);
        await context.SaveChangesAsync();

        // Act
        var result = await service.Handle(new CancelBookingCommand(booking.Id));

        // Assert
        result.Should().NotBeNull();
        result!.Status.Should().Be(BookingStatus.Cancelled);

        // Verify outbox has NO messages
        var outbox = await context.OutboxMessages.ToListAsync();
        outbox.Should().BeEmpty();
    }
}
