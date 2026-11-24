using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SmartStay.Bookings.Application.Internal.CommandServices;
using SmartStay.Bookings.Domain.Model.Aggregates;
using SmartStay.Bookings.Domain.Model.Commands;
using SmartStay.Bookings.Domain.Model.Entities;
using SmartStay.Bookings.Domain.Model.Exceptions;
using SmartStay.Bookings.Domain.Repositories;
using SmartStay.Bookings.Infrastructure.Clients;
using SmartStay.Bookings.Infrastructure.Persistence.EFC.Configuration;
using SmartStay.Bookings.Infrastructure.Persistence.EFC.Repositories;
using SmartStay.Contracts.Http;
using Xunit;

namespace SmartStay.Bookings.Tests;

public class BookingQuotesTests
{
    private static BookingsDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<BookingsDbContext>()
            .UseInMemoryDatabase(databaseName: $"BookingsQuoteTestDb_{Guid.NewGuid()}")
            .Options;
        return new BookingsDbContext(options);
    }

    [Fact]
    [Trait("Service", "Bookings")]
    [Trait("Feature", "F14")]
    public void BookingQuote_Creation_Has_2_Minute_Ttl_And_Correct_Defaults()
    {
        var checkIn = DateTime.UtcNow.Date.AddDays(2);
        var checkOut = checkIn.AddDays(3);
        var quote = new BookingQuote(
            quoteId: "qte_test_01",
            roomId: 102,
            hotelId: 1,
            guestUserId: 2,
            purchaseIntentId: "cpi_sample_101",
            checkInDate: checkIn,
            checkOutDate: checkOut,
            guests: 2,
            nightlyPrice: 100m,
            stayTotal: 300m,
            currency: "USD",
            ttlMinutes: 2
        );

        quote.QuoteId.Should().Be("qte_test_01");
        quote.RoomId.Should().Be(102);
        quote.GuestUserId.Should().Be(2);
        quote.PurchaseIntentId.Should().Be("cpi_sample_101");
        quote.Guests.Should().Be(2);
        quote.StayTotal.Should().Be(300m);
        quote.Currency.Should().Be("USD");
        quote.IsUsed.Should().BeFalse();
        quote.IsExpired.Should().BeFalse();
        (quote.ExpiresAtUtc - quote.CreatedAtUtc).TotalMinutes.Should().BeApproximately(2, 0.1);
    }

    [Fact]
    [Trait("Service", "Bookings")]
    [Trait("Feature", "F14")]
    public void BookingQuote_IsExpired_Returns_True_When_Past_Ttl()
    {
        var checkIn = DateTime.UtcNow.Date.AddDays(2);
        var checkOut = checkIn.AddDays(3);
        var quote = new BookingQuote(
            quoteId: "qte_expired",
            roomId: 102,
            hotelId: 1,
            guestUserId: 2,
            purchaseIntentId: "cpi_sample_102",
            checkInDate: checkIn,
            checkOutDate: checkOut,
            guests: 2,
            nightlyPrice: 100m,
            stayTotal: 300m,
            currency: "USD",
            ttlMinutes: -1 // Expired 1 minute ago
        );

        quote.IsExpired.Should().BeTrue();
    }

    [Fact]
    [Trait("Service", "Bookings")]
    [Trait("Feature", "F14")]
    public void BookingQuote_MarkAsUsed_Sets_IsUsed_True()
    {
        var quote = new BookingQuote(
            quoteId: "qte_used",
            roomId: 102,
            hotelId: 1,
            guestUserId: 2,
            purchaseIntentId: "cpi_sample_103",
            checkInDate: DateTime.UtcNow.Date.AddDays(1),
            checkOutDate: DateTime.UtcNow.Date.AddDays(2),
            guests: 2,
            nightlyPrice: 100m,
            stayTotal: 100m
        );

        quote.IsUsed.Should().BeFalse();
        quote.MarkAsUsed();
        quote.IsUsed.Should().BeTrue();
    }

    [Fact]
    [Trait("Service", "Bookings")]
    [Trait("Feature", "F14")]
    public void Booking_Constructed_With_PurchaseIntentId_Stores_Association()
    {
        var checkIn = DateTime.UtcNow.Date.AddDays(1);
        var checkOut = checkIn.AddDays(2);
        var booking = new Booking(
            roomId: 102,
            hotelId: 1,
            guestName: "Guest User",
            guestEmail: "guest@example.com",
            checkInDate: checkIn,
            checkOutDate: checkOut,
            nightlyPrice: 100m,
            totalAmount: 100m,
            currency: "USD",
            ttlMinutes: 15,
            guestUserId: 2,
            purchaseIntentId: "cpi_mandate_99"
        );

        booking.PurchaseIntentId.Should().Be("cpi_mandate_99");
    }

    [Fact]
    [Trait("Service", "Bookings")]
    [Trait("Feature", "F14")]
    public async Task BookingCommandService_CreateQuote_Does_Not_Acquire_Physical_Locks()
    {
        var db = CreateInMemoryDbContext();
        var repo = new BookingRepository(db);
        var uowMock = new Mock<IBookingsUnitOfWork>();
        uowMock.Setup(u => u.CompleteAsync()).Returns(() => db.SaveChangesAsync());

        var catalogClientMock = new Mock<IRoomCatalogClient>();
        catalogClientMock.Setup(c => c.FindAsync(102, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoomSnapshot(102, 1, true, 120m, "USD", Capacity: 2));

        var service = new BookingCommandService(repo, uowMock.Object, catalogClientMock.Object, NullLogger<BookingCommandService>.Instance);

        var checkIn = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(2));
        var checkOut = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(4));

        var quote = await service.Handle(new CreateBookingQuoteCommand(
            RoomId: 102,
            GuestUserId: 2,
            PurchaseIntentId: "cpi_lock_test",
            CheckIn: checkIn,
            CheckOut: checkOut,
            Guests: 2
        ));

        quote.Should().NotBeNull();
        quote!.RoomId.Should().Be(102);
        quote.StayTotal.Should().Be(240m); // 2 nights * $120

        // CRITICAL SPEC REQUIREMENT: Quotes do NOT acquire physical locks in room_night_locks!
        var physicalLocksCount = await db.RoomNightLocks.CountAsync();
        physicalLocksCount.Should().Be(0, "Generating quotes must not lock physical room inventory");
    }

    [Fact]
    [Trait("Service", "Bookings")]
    [Trait("Feature", "F14")]
    public async Task BookingCommandService_CreateQuote_Throws_When_Capacity_Exceeded()
    {
        var db = CreateInMemoryDbContext();
        var repo = new BookingRepository(db);
        var uowMock = new Mock<IBookingsUnitOfWork>();

        var catalogClientMock = new Mock<IRoomCatalogClient>();
        catalogClientMock.Setup(c => c.FindAsync(102, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoomSnapshot(102, 1, true, 100m, "USD", Capacity: 2));

        var service = new BookingCommandService(repo, uowMock.Object, catalogClientMock.Object, NullLogger<BookingCommandService>.Instance);

        var act = async () => await service.Handle(new CreateBookingQuoteCommand(
            RoomId: 102,
            GuestUserId: 2,
            PurchaseIntentId: "cpi_cap_test",
            CheckIn: DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(2)),
            CheckOut: DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(4)),
            Guests: 20 // Exceeds capacity 2
        ));

        await act.Should().ThrowAsync<RoomCapacityExceededException>()
            .WithMessage("*exceeds capacity of 2*");
    }

    [Fact]
    [Trait("Service", "Bookings")]
    [Trait("Feature", "F14")]
    public async Task BookingCommandService_Rejects_Reservation_When_Quote_Is_Expired()
    {
        var db = CreateInMemoryDbContext();
        var repo = new BookingRepository(db);
        var uowMock = new Mock<IBookingsUnitOfWork>();

        var catalogClientMock = new Mock<IRoomCatalogClient>();
        catalogClientMock.Setup(c => c.FindAsync(102, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoomSnapshot(102, 1, true, 100m, "USD", Capacity: 2));

        var expiredQuote = new BookingQuote(
            quoteId: "qte_expired_01",
            roomId: 102,
            hotelId: 1,
            guestUserId: 2,
            purchaseIntentId: "cpi_exp_01",
            checkInDate: DateTime.UtcNow.Date.AddDays(2),
            checkOutDate: DateTime.UtcNow.Date.AddDays(4),
            guests: 2,
            nightlyPrice: 100m,
            stayTotal: 200m,
            currency: "USD",
            ttlMinutes: -2 // Expired
        );
        await db.BookingQuotes.AddAsync(expiredQuote);
        await db.SaveChangesAsync();

        var service = new BookingCommandService(repo, uowMock.Object, catalogClientMock.Object, NullLogger<BookingCommandService>.Instance);

        var booking = await service.Handle(new CreateBookingCommand(
            RoomId: 102,
            GuestName: "Guest",
            GuestEmail: "guest@example.com",
            CheckInDate: DateTime.UtcNow.Date.AddDays(2),
            CheckOutDate: DateTime.UtcNow.Date.AddDays(4),
            GuestUserId: 2,
            QuoteId: "qte_expired_01"
        ));

        booking.Should().BeNull("Cannot create booking with an expired 2-minute quote");
    }

    [Fact]
    [Trait("Service", "Bookings")]
    [Trait("Feature", "F14")]
    public async Task BookingRepository_FindByPurchaseIntentId_Returns_Matching_Booking()
    {
        var db = CreateInMemoryDbContext();
        var repo = new BookingRepository(db);

        var booking = new Booking(
            roomId: 102,
            hotelId: 1,
            guestName: "Guest",
            guestEmail: "guest@example.com",
            checkInDate: DateTime.UtcNow.Date.AddDays(2),
            checkOutDate: DateTime.UtcNow.Date.AddDays(4),
            nightlyPrice: 100m,
            totalAmount: 200m,
            currency: "USD",
            ttlMinutes: 15,
            guestUserId: 2,
            purchaseIntentId: "cpi_recovery_test_99"
        );

        await db.Bookings.AddAsync(booking);
        await db.SaveChangesAsync();

        var recovered = await repo.FindByPurchaseIntentIdAsync("cpi_recovery_test_99");
        recovered.Should().NotBeNull();
        recovered!.Id.Should().Be(booking.Id);
        recovered.PurchaseIntentId.Should().Be("cpi_recovery_test_99");
    }
}
