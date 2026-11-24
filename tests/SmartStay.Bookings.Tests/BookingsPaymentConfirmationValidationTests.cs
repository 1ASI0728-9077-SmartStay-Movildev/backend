using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using SmartStay.Bookings.Domain.Model.Aggregates;
using SmartStay.Bookings.Domain.Model.Entities;
using SmartStay.Bookings.Domain.Repositories;
using SmartStay.Bookings.Infrastructure.Messaging;
using SmartStay.Bookings.Infrastructure.Persistence.EFC.Configuration;
using SmartStay.Bookings.Infrastructure.Persistence.EFC.Repositories;
using Xunit;

namespace SmartStay.Bookings.Tests;

public class BookingsPaymentConfirmationValidationTests
{
    private (IServiceProvider Provider, BookingsDbContext Context, BookingsPaymentConsumerWorker Worker) CreateTestRig()
    {
        var dbName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();

        services.AddDbContext<BookingsDbContext>(opt =>
            opt.UseInMemoryDatabase(dbName)
               .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));

        services.AddScoped<IBookingRepository, BookingRepository>();
        services.AddScoped<IBookingsUnitOfWork, BookingsUnitOfWork>();

        var provider = services.BuildServiceProvider();
        var context = provider.GetRequiredService<BookingsDbContext>();

        var mockConfig = new Mock<IConfiguration>();
        var mockLogger = new Mock<ILogger<BookingsPaymentConsumerWorker>>();

        var worker = new BookingsPaymentConsumerWorker(provider, mockConfig.Object, mockLogger.Object);
        return (provider, context, worker);
    }

    private static async Task<Booking> SeedBookingAsync(BookingsDbContext context, Booking booking, int nights = 2)
    {
        context.Bookings.Add(booking);
        await context.SaveChangesAsync();

        for (int i = 0; i < nights; i++)
        {
            var lockDate = DateOnly.FromDateTime(booking.CheckInDate.AddDays(i));
            var nightLock = new RoomNightLock(booking.RoomId, lockDate, booking.Id);
            context.RoomNightLocks.Add(nightLock);
            booking.AddNightLock(nightLock);
        }
        await context.SaveChangesAsync();
        return booking;
    }

    private static string CreatePaymentCompletedJson(Guid eventId, int paymentId, int bookingId, decimal amount, string currency = "USD")
    {
        var payload = new
        {
            eventId = eventId,
            eventType = "payments.payment-completed",
            data = new
            {
                paymentId = paymentId,
                bookingId = bookingId,
                amount = amount,
                currency = currency,
                occurredAtUtc = DateTime.UtcNow
            }
        };
        return JsonSerializer.Serialize(payload);
    }

    [Fact]
    [Trait("Service", "Bookings")]
    [Trait("Category", "StrictConfirmation")]
    public async Task Payment_ValidMatch_Confirms_Booking_And_Saves_PaymentId()
    {
        // Arrange
        var (_, context, worker) = CreateTestRig();
        var checkIn = DateTime.UtcNow.Date.AddDays(1);
        var checkOut = checkIn.AddDays(2);
        var booking = new Booking(101, 1, "Guest One", "guest1@example.com", checkIn, checkOut, 150m, 300m, "USD");
        await SeedBookingAsync(context, booking, nights: 2); // 1 night: checkIn to checkIn+1 day = AddDays(2) is 1 night if checkOut is AddDays(2)? Wait: checkOut - checkIn = 1 day

        var eventId = Guid.NewGuid();
        var json = CreatePaymentCompletedJson(eventId, paymentId: 77, bookingId: booking.Id, amount: 300m, currency: "USD");

        // Act
        await worker.ProcessPaymentEventAsync(json, CancellationToken.None);

        // Assert
        var updated = await context.Bookings.AsNoTracking().FirstOrDefaultAsync(b => b.Id == booking.Id);
        updated.Should().NotBeNull();
        updated!.Status.Should().Be(BookingStatus.Confirmed);
        updated.PaymentId.Should().Be(77);
        updated.ExpiresAtUtc.Should().BeNull();

        var inbox = await context.InboxMessages.AsNoTracking().FirstOrDefaultAsync(m => m.EventId == eventId);
        inbox.Should().NotBeNull();
        inbox!.Status.Should().Be("Processed");

        var outbox = await context.OutboxMessages.ToListAsync();
        outbox.Should().BeEmpty();
    }

    [Fact]
    [Trait("Service", "Bookings")]
    [Trait("Category", "StrictConfirmation")]
    public async Task Payment_AmountMismatch_Does_Not_Confirm_And_Enqueues_Outbox_Refund()
    {
        // Arrange
        var (_, context, worker) = CreateTestRig();
        var checkIn = DateTime.UtcNow.Date.AddDays(1);
        var checkOut = checkIn.AddDays(2);
        var booking = new Booking(101, 1, "Guest One", "guest1@example.com", checkIn, checkOut, 150m, 300m, "USD");
        await SeedBookingAsync(context, booking, nights: 2);

        var eventId = Guid.NewGuid();
        // Payment has only 150m instead of 300m
        var json = CreatePaymentCompletedJson(eventId, paymentId: 78, bookingId: booking.Id, amount: 150m, currency: "USD");

        // Act
        await worker.ProcessPaymentEventAsync(json, CancellationToken.None);

        // Assert
        var updated = await context.Bookings.AsNoTracking().FirstOrDefaultAsync(b => b.Id == booking.Id);
        updated!.Status.Should().Be(BookingStatus.PendingPayment);
        updated.PaymentId.Should().BeNull();

        var outbox = await context.OutboxMessages.FirstOrDefaultAsync();
        outbox.Should().NotBeNull();
        outbox!.EventType.Should().Be("bookings.payment-refund-requested");
        outbox.Payload.Should().Contain("\"paymentId\":78");
        outbox.Payload.Should().Contain("mismatch");
    }

    [Fact]
    [Trait("Service", "Bookings")]
    [Trait("Category", "StrictConfirmation")]
    public async Task Payment_CurrencyMismatch_Does_Not_Confirm_And_Enqueues_Outbox_Refund()
    {
        // Arrange
        var (_, context, worker) = CreateTestRig();
        var checkIn = DateTime.UtcNow.Date.AddDays(1);
        var checkOut = checkIn.AddDays(2);
        var booking = new Booking(101, 1, "Guest One", "guest1@example.com", checkIn, checkOut, 150m, 300m, "USD");
        await SeedBookingAsync(context, booking, nights: 2);

        var eventId = Guid.NewGuid();
        var json = CreatePaymentCompletedJson(eventId, paymentId: 79, bookingId: booking.Id, amount: 300m, currency: "EUR");

        // Act
        await worker.ProcessPaymentEventAsync(json, CancellationToken.None);

        // Assert
        var updated = await context.Bookings.AsNoTracking().FirstOrDefaultAsync(b => b.Id == booking.Id);
        updated!.Status.Should().Be(BookingStatus.PendingPayment);

        var outbox = await context.OutboxMessages.FirstOrDefaultAsync();
        outbox.Should().NotBeNull();
        outbox!.EventType.Should().Be("bookings.payment-refund-requested");
        outbox.Payload.Should().Contain("\"paymentId\":79");
    }

    [Fact]
    [Trait("Service", "Bookings")]
    [Trait("Category", "StrictConfirmation")]
    public async Task Payment_ExpiredHold_Transitions_To_Expired_And_Enqueues_Outbox_Refund()
    {
        // Arrange
        var (_, context, worker) = CreateTestRig();
        var checkIn = DateTime.UtcNow.Date.AddDays(1);
        var checkOut = checkIn.AddDays(2);
        // Expired TTL (-10 minutes)
        var booking = new Booking(101, 1, "Guest One", "guest1@example.com", checkIn, checkOut, 150m, 300m, "USD", ttlMinutes: -10);
        await SeedBookingAsync(context, booking, nights: 1);

        var eventId = Guid.NewGuid();
        var json = CreatePaymentCompletedJson(eventId, paymentId: 80, bookingId: booking.Id, amount: 300m, currency: "USD");

        // Act
        await worker.ProcessPaymentEventAsync(json, CancellationToken.None);

        // Assert
        var updated = await context.Bookings.AsNoTracking().FirstOrDefaultAsync(b => b.Id == booking.Id);
        updated!.Status.Should().Be(BookingStatus.Expired);
        updated.PaymentId.Should().BeNull();

        var outbox = await context.OutboxMessages.FirstOrDefaultAsync();
        outbox.Should().NotBeNull();
        outbox!.EventType.Should().Be("bookings.payment-refund-requested");
        outbox.Payload.Should().Contain("\"paymentId\":80");
        outbox.Payload.Should().Contain("expired");
    }

    [Fact]
    [Trait("Service", "Bookings")]
    [Trait("Category", "StrictConfirmation")]
    public async Task Payment_MissingLocks_Transitions_To_Expired_And_Enqueues_Outbox_Refund()
    {
        // Arrange
        var (_, context, worker) = CreateTestRig();
        var checkIn = DateTime.UtcNow.Date.AddDays(1);
        var checkOut = checkIn.AddDays(3); // 2 nights
        var booking = new Booking(101, 1, "Guest One", "guest1@example.com", checkIn, checkOut, 150m, 300m, "USD");
        // Only 1 lock seeded instead of 2
        await SeedBookingAsync(context, booking, nights: 1);

        var eventId = Guid.NewGuid();
        var json = CreatePaymentCompletedJson(eventId, paymentId: 81, bookingId: booking.Id, amount: 300m, currency: "USD");

        // Act
        await worker.ProcessPaymentEventAsync(json, CancellationToken.None);

        // Assert
        var updated = await context.Bookings.AsNoTracking().FirstOrDefaultAsync(b => b.Id == booking.Id);
        updated!.Status.Should().Be(BookingStatus.Expired);

        var outbox = await context.OutboxMessages.FirstOrDefaultAsync();
        outbox.Should().NotBeNull();
        outbox!.EventType.Should().Be("bookings.payment-refund-requested");
        outbox.Payload.Should().Contain("locks invalid");
    }

    [Fact]
    [Trait("Service", "Bookings")]
    [Trait("Category", "StrictConfirmation")]
    public async Task Payment_BookingNotFound_Enqueues_Outbox_Refund()
    {
        // Arrange
        var (_, context, worker) = CreateTestRig();
        var eventId = Guid.NewGuid();
        var json = CreatePaymentCompletedJson(eventId, paymentId: 82, bookingId: 9999, amount: 300m, currency: "USD");

        // Act
        await worker.ProcessPaymentEventAsync(json, CancellationToken.None);

        // Assert
        var outbox = await context.OutboxMessages.FirstOrDefaultAsync();
        outbox.Should().NotBeNull();
        outbox!.EventType.Should().Be("bookings.payment-refund-requested");
        outbox.Payload.Should().Contain("\"paymentId\":82");
        outbox.Payload.Should().Contain("not found");
    }

    [Fact]
    [Trait("Service", "Bookings")]
    [Trait("Category", "Idempotency")]
    public async Task Duplicate_PaymentEvent_For_SamePayment_Is_Idempotent()
    {
        // Arrange
        var (_, context, worker) = CreateTestRig();
        var checkIn = DateTime.UtcNow.Date.AddDays(1);
        var checkOut = checkIn.AddDays(2);
        var booking = new Booking(101, 1, "Guest One", "guest1@example.com", checkIn, checkOut, 150m, 300m, "USD");
        await SeedBookingAsync(context, booking, nights: 2);

        var eventId1 = Guid.NewGuid();
        var json1 = CreatePaymentCompletedJson(eventId1, paymentId: 83, bookingId: booking.Id, amount: 300m);
        await worker.ProcessPaymentEventAsync(json1, CancellationToken.None);

        // Act: re-deliver with different eventId but same paymentId for already confirmed booking
        var eventId2 = Guid.NewGuid();
        var json2 = CreatePaymentCompletedJson(eventId2, paymentId: 83, bookingId: booking.Id, amount: 300m);
        await worker.ProcessPaymentEventAsync(json2, CancellationToken.None);

        // Assert
        var updated = await context.Bookings.AsNoTracking().FirstOrDefaultAsync(b => b.Id == booking.Id);
        updated!.Status.Should().Be(BookingStatus.Confirmed);
        updated.PaymentId.Should().Be(83);

        var outbox = await context.OutboxMessages.ToListAsync();
        outbox.Should().BeEmpty(); // No refunds enqueued
    }
}
