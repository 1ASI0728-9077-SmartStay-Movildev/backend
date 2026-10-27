using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using SmartStay.Bookings.Application.Internal.CommandServices;
using SmartStay.Bookings.Domain.Model.Aggregates;
using SmartStay.Bookings.Domain.Model.Commands;
using SmartStay.Bookings.Domain.Model.Entities;
using SmartStay.Bookings.Domain.Model.Exceptions;
using SmartStay.Bookings.Infrastructure.Clients;
using SmartStay.Bookings.Infrastructure.Persistence.EFC.Configuration;
using SmartStay.Bookings.Infrastructure.Persistence.EFC.Repositories;
using SmartStay.Contracts.Http;
using Xunit;

namespace SmartStay.Bookings.Tests;

public class BookingConcurrencyAndLockTests
{
    private BookingsDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<BookingsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new BookingsDbContext(options);
    }

    [Fact]
    [Trait("Service", "Bookings")]
    [Trait("Category", "Domain")]
    public void New_Booking_Initializes_With_PendingPayment_And_Calculated_Amounts()
    {
        var checkIn = new DateTime(2026, 11, 1, 14, 0, 0, DateTimeKind.Utc);
        var checkOut = new DateTime(2026, 11, 4, 11, 0, 0, DateTimeKind.Utc);

        var booking = new Booking(
            roomId: 10,
            hotelId: 1,
            guestName: "Ana Silva",
            guestEmail: "ana@example.com",
            checkInDate: checkIn,
            checkOutDate: checkOut,
            nightlyPrice: 150.00m,
            totalAmount: 450.00m,
            currency: "USD"
        );

        booking.TotalAmount.Should().Be(450.00m);
        booking.Status.Should().Be(BookingStatus.PendingPayment);
        booking.RoomId.Should().Be(10);
        booking.HotelId.Should().Be(1);
        booking.NightlyPrice.Should().Be(150.00m);
        booking.Currency.Should().Be("USD");
        booking.ExpiresAtUtc.Should().NotBeNull();
    }

    [Fact]
    [Trait("Service", "Bookings")]
    [Trait("Category", "Concurrency")]
    public async Task Overlapping_Booking_Throws_RoomAlreadyBookedException()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var uow = new BookingsUnitOfWork(context);
        var repo = new BookingRepository(context);
        var mockLogger = new Mock<ILogger<BookingCommandService>>();

        var mockCatalog = new Mock<IRoomCatalogClient>();
        mockCatalog.Setup(c => c.FindAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoomSnapshot(10, 1, true, 100.00m, "USD"));

        var service = new BookingCommandService(repo, uow, mockCatalog.Object, mockLogger.Object);

        var checkIn1 = new DateTime(2026, 12, 1, 12, 0, 0, DateTimeKind.Utc);
        var checkOut1 = new DateTime(2026, 12, 5, 12, 0, 0, DateTimeKind.Utc);
        var cmd1 = new CreateBookingCommand(10, "Guest One", "one@example.com", checkIn1, checkOut1);
        var booking1 = await service.Handle(cmd1);
        booking1.Should().NotBeNull();

        // Act & Assert - second booking overlaps on Dec 3-7
        var checkIn2 = new DateTime(2026, 12, 3, 12, 0, 0, DateTimeKind.Utc);
        var checkOut2 = new DateTime(2026, 12, 7, 12, 0, 0, DateTimeKind.Utc);
        var cmd2 = new CreateBookingCommand(10, "Guest Two", "two@example.com", checkIn2, checkOut2);

        var act = async () => await service.Handle(cmd2);
        await act.Should().ThrowAsync<RoomAlreadyBookedException>();
    }

    [Fact]
    [Trait("Service", "Bookings")]
    [Trait("Category", "Concurrency")]
    public async Task Adjacent_NonOverlapping_Bookings_Succeed()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var uow = new BookingsUnitOfWork(context);
        var repo = new BookingRepository(context);
        var mockLogger = new Mock<ILogger<BookingCommandService>>();

        var mockCatalog = new Mock<IRoomCatalogClient>();
        mockCatalog.Setup(c => c.FindAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoomSnapshot(10, 1, true, 100.00m, "USD"));

        var service = new BookingCommandService(repo, uow, mockCatalog.Object, mockLogger.Object);

        // Booking 1: Dec 1 to Dec 3 (nights: Dec 1, Dec 2)
        var cmd1 = new CreateBookingCommand(10, "Guest One", "one@example.com",
            new DateTime(2026, 12, 1), new DateTime(2026, 12, 3));
        var result1 = await service.Handle(cmd1);
        result1.Should().NotBeNull();

        // Booking 2: Dec 3 to Dec 5 (nights: Dec 3, Dec 4) - checkout matches checkin
        var cmd2 = new CreateBookingCommand(10, "Guest Two", "two@example.com",
            new DateTime(2026, 12, 3), new DateTime(2026, 12, 5));
        var result2 = await service.Handle(cmd2);
        result2.Should().NotBeNull();

        result1!.Id.Should().NotBe(result2!.Id);
    }

    [Fact]
    [Trait("Service", "Bookings")]
    [Trait("Category", "Lifecycle")]
    public async Task Cancelling_Booking_Releases_Room_Night_Locks()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var uow = new BookingsUnitOfWork(context);
        var repo = new BookingRepository(context);
        var mockLogger = new Mock<ILogger<BookingCommandService>>();

        var mockCatalog = new Mock<IRoomCatalogClient>();
        mockCatalog.Setup(c => c.FindAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoomSnapshot(10, 1, true, 100.00m, "USD"));

        var service = new BookingCommandService(repo, uow, mockCatalog.Object, mockLogger.Object);

        var cmd = new CreateBookingCommand(10, "Guest One", "one@example.com",
            new DateTime(2026, 12, 10), new DateTime(2026, 12, 13));
        var created = await service.Handle(cmd);
        created.Should().NotBeNull();

        // Verify locks exist
        var locksBefore = await context.RoomNightLocks.Where(l => l.BookingId == created!.Id).ToListAsync();
        locksBefore.Should().HaveCount(3);

        // Act
        var cancelled = await service.Handle(new CancelBookingCommand(created!.Id));
        cancelled.Should().NotBeNull();
        cancelled!.Status.Should().Be(BookingStatus.Cancelled);

        // Assert - locks are deleted
        var locksAfter = await context.RoomNightLocks.Where(l => l.BookingId == created.Id).ToListAsync();
        locksAfter.Should().BeEmpty();
    }
}
