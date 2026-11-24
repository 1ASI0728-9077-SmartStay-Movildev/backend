using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartStay.Bookings.Domain.Model.Aggregates;
using SmartStay.Bookings.Domain.Model.Commands;
using SmartStay.Bookings.Domain.Model.Entities;
using SmartStay.Bookings.Domain.Model.Exceptions;
using SmartStay.Bookings.Domain.Repositories;
using SmartStay.Bookings.Domain.Services;
using SmartStay.Bookings.Infrastructure.Clients;
using SmartStay.Contracts.Events.V1;

namespace SmartStay.Bookings.Application.Internal.CommandServices;

public class BookingCommandService(
    IBookingRepository bookingRepository,
    IBookingsUnitOfWork unitOfWork,
    IRoomCatalogClient roomCatalogClient,
    ILogger<BookingCommandService> logger) : IBookingCommandService
{
    public async Task<Booking?> Handle(CreateBookingCommand command)
    {
        if (command.CheckInDate >= command.CheckOutDate)
        {
            logger.LogWarning("Invalid booking dates: CheckIn {CheckIn} must be before CheckOut {CheckOut}", command.CheckInDate, command.CheckOutDate);
            return null;
        }

        var stayDuration = (command.CheckOutDate.Date - command.CheckInDate.Date).Days;
        if (stayDuration is < 1 or > 30)
        {
            logger.LogWarning("Booking duration must be between 1 and 30 nights. Provided: {Duration}", stayDuration);
            return null;
        }

        // 0. Si viene vinculado a una cotización de 2 minutos, validarla estrictamente
        BookingQuote? quote = null;
        if (!string.IsNullOrWhiteSpace(command.QuoteId))
        {
            quote = await bookingRepository.FindQuoteByIdAsync(command.QuoteId);
            if (quote is null)
            {
                logger.LogWarning("Quote {QuoteId} was not found.", command.QuoteId);
                return null;
            }

            if (quote.IsUsed)
            {
                logger.LogWarning("Quote {QuoteId} was already used.", command.QuoteId);
                return null;
            }

            if (quote.IsExpired)
            {
                logger.LogWarning("Quote {QuoteId} has expired (TTL 2 minutes exceeded).", command.QuoteId);
                return null;
            }

            if (quote.RoomId != command.RoomId)
            {
                logger.LogWarning("Quote RoomId {QuoteRoomId} does not match command RoomId {RoomId}.", quote.RoomId, command.RoomId);
                return null;
            }

            if (command.GuestUserId > 0 && quote.GuestUserId > 0 && quote.GuestUserId != command.GuestUserId)
            {
                logger.LogWarning("Quote GuestUserId {QuoteGuestUserId} does not match command GuestUserId {GuestUserId}.", quote.GuestUserId, command.GuestUserId);
                return null;
            }
        }

        // 1. Consultar catálogo de Accommodations por contrato interno
        var roomSnapshot = await roomCatalogClient.FindAsync(command.RoomId);
        if (roomSnapshot is null || !roomSnapshot.IsBookable)
        {
            logger.LogWarning("Room {RoomId} does not exist or is not available for booking in Accommodations catalog.", command.RoomId);
            return null;
        }

        var totalAmount = stayDuration * roomSnapshot.NightlyPrice;
        var stayDates = new List<DateOnly>();
        for (var i = 0; i < stayDuration; i++)
        {
            stayDates.Add(DateOnly.FromDateTime(command.CheckInDate.Date.AddDays(i)));
        }

        // 2. Pre-chequeo de disponibilidad para feedback rápido
        foreach (var date in stayDates)
        {
            if (await bookingRepository.HasConflictAsync(command.RoomId, date))
            {
                logger.LogInformation("Conflict detected on date {Date} for room {RoomId}.", date, command.RoomId);
                throw new RoomAlreadyBookedException(command.RoomId, date);
            }
        }

        // 3. Transacción atómica en MySQL para materializar reserva y bloqueos físicos
        await using var transaction = await unitOfWork.BeginTransactionAsync();
        try
        {
            var booking = new Booking(
                command.RoomId,
                roomSnapshot.HotelId,
                command.GuestName,
                command.GuestEmail,
                command.CheckInDate,
                command.CheckOutDate,
                roomSnapshot.NightlyPrice,
                totalAmount,
                roomSnapshot.Currency,
                15,
                command.GuestUserId,
                command.PurchaseIntentId ?? quote?.PurchaseIntentId
            );

            await bookingRepository.AddAsync(booking);
            if (quote != null)
            {
                quote.MarkAsUsed();
            }
            await unitOfWork.CompleteAsync();

            foreach (var stayDate in stayDates)
            {
                var nightLock = new RoomNightLock(booking.RoomId, stayDate, booking.Id);
                await bookingRepository.AddNightLockAsync(nightLock);
                booking.AddNightLock(nightLock);
            }

            await unitOfWork.CompleteAsync();
            await transaction.CommitAsync();

            logger.LogInformation("Booking #{BookingId} created successfully for Room #{RoomId} ({Nights} nights).", booking.Id, booking.RoomId, stayDuration);
            return booking;
        }
        catch (DbUpdateException ex)
        {
            await transaction.RollbackAsync();

            var isNightLockCollision = ex.InnerException?.Message.Contains("room_night_locks", StringComparison.OrdinalIgnoreCase) == true
                || ex.InnerException?.Message.Contains("Duplicate entry", StringComparison.OrdinalIgnoreCase) == true
                || ex.Message.Contains("room_night_locks", StringComparison.OrdinalIgnoreCase) == true;

            if (isNightLockCollision)
            {
                logger.LogWarning(ex, "Physical night collision: Room #{RoomId} was locked concurrently for the requested dates.", command.RoomId);
                throw new RoomAlreadyBookedException(command.RoomId, stayDates[0]);
            }

            logger.LogError(ex, "Non-collision database error creating booking for room {RoomId}.", command.RoomId);
            throw;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            logger.LogError(ex, "Unexpected error creating booking for room {RoomId}.", command.RoomId);
            throw;
        }
    }

    public async Task<BookingQuote?> Handle(CreateBookingQuoteCommand command)
    {
        if (command.CheckIn >= command.CheckOut)
        {
            logger.LogWarning("Invalid quote dates: CheckIn {CheckIn} must be before CheckOut {CheckOut}", command.CheckIn, command.CheckOut);
            throw new ArgumentException("CheckIn date must be before CheckOut date.");
        }

        var stayDuration = command.CheckOut.DayNumber - command.CheckIn.DayNumber;
        if (stayDuration is < 1 or > 30)
        {
            logger.LogWarning("Quote duration must be between 1 and 30 nights. Provided: {Duration}", stayDuration);
            throw new ArgumentException("Stay duration must be between 1 and 30 nights.");
        }

        var roomSnapshot = await roomCatalogClient.FindAsync(command.RoomId);
        if (roomSnapshot is null)
        {
            logger.LogWarning("Room {RoomId} does not exist in Accommodations catalog.", command.RoomId);
            return null;
        }

        if (!roomSnapshot.IsBookable)
        {
            logger.LogWarning("Room {RoomId} is not bookable.", command.RoomId);
            throw new RoomNotBookableException(command.RoomId);
        }

        if (command.Guests > roomSnapshot.Capacity)
        {
            logger.LogWarning("Requested {Guests} guests exceeds capacity {Capacity} for room {RoomId}.",
                command.Guests, roomSnapshot.Capacity, command.RoomId);
            throw new RoomCapacityExceededException(command.RoomId, command.Guests, roomSnapshot.Capacity);
        }

        var stayDates = new List<DateOnly>();
        for (var i = 0; i < stayDuration; i++)
        {
            stayDates.Add(command.CheckIn.AddDays(i));
        }

        // Comprobar ausencia de bloqueos físicos (quotes no adquieren cerrojos pero validan disponibilidad)
        foreach (var date in stayDates)
        {
            if (await bookingRepository.HasConflictAsync(command.RoomId, date))
            {
                logger.LogInformation("Conflict detected on date {Date} for room {RoomId} during quote generation.", date, command.RoomId);
                throw new RoomAlreadyBookedException(command.RoomId, date);
            }
        }

        var quoteId = $"qte_{Guid.NewGuid():N}";
        var stayTotal = stayDuration * roomSnapshot.NightlyPrice;

        var quote = new BookingQuote(
            quoteId,
            command.RoomId,
            roomSnapshot.HotelId,
            command.GuestUserId,
            command.PurchaseIntentId,
            command.CheckIn.ToDateTime(TimeOnly.MinValue),
            command.CheckOut.ToDateTime(TimeOnly.MinValue),
            command.Guests,
            roomSnapshot.NightlyPrice,
            stayTotal,
            roomSnapshot.Currency,
            ttlMinutes: 2
        );

        await bookingRepository.AddQuoteAsync(quote);
        await unitOfWork.CompleteAsync();

        logger.LogInformation("Generated 2-minute quote {QuoteId} for room #{RoomId} (StayTotal ${StayTotal} USD).",
            quote.QuoteId, quote.RoomId, quote.StayTotal);

        return quote;
    }

    public async Task<Booking?> Handle(ConfirmBookingCommand command)
    {
        await using var tx = await unitOfWork.BeginTransactionAsync();
        try
        {
            var booking = await bookingRepository.FindByIdAsync(command.BookingId);
            if (booking is null) return null;

            booking.Confirm();
            bookingRepository.Update(booking);
            await unitOfWork.CompleteAsync();
            await tx.CommitAsync();

            logger.LogInformation("Booking #{BookingId} confirmed.", booking.Id);
            return booking;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            await tx.RollbackAsync();
            logger.LogWarning(ex, "Concurrency conflict confirming booking #{BookingId}.", command.BookingId);
            throw;
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            logger.LogError(ex, "Error confirming booking #{BookingId}.", command.BookingId);
            throw;
        }
    }

    public async Task<Booking?> Handle(CancelBookingCommand command)
    {
        await using var tx = await unitOfWork.BeginTransactionAsync();
        try
        {
            var booking = await bookingRepository.FindByIdAsync(command.BookingId);
            if (booking is null) return null;

            var wasConfirmed = booking.Status == BookingStatus.Confirmed;
            var paymentId = booking.PaymentId;

            booking.Cancel();
            await bookingRepository.RemoveLocksByBookingIdAsync(booking.Id);
            bookingRepository.Update(booking);

            if (wasConfirmed)
            {
                var eventId = Guid.NewGuid();
                var refundEvt = new IntegrationEvent<PaymentRefundRequestedV1>(
                    EventId: eventId,
                    EventType: "bookings.payment-refund-requested",
                    SchemaVersion: 1,
                    OccurredAtUtc: DateTimeOffset.UtcNow,
                    CorrelationId: eventId.ToString(),
                    CausationId: null,
                    AggregateId: booking.Id.ToString(),
                    AggregateVersion: 2,
                    Data: new PaymentRefundRequestedV1(paymentId ?? 0, booking.Id, "Booking cancelled after confirmation.")
                );

                var payload = JsonSerializer.Serialize(refundEvt, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                var outboxMessage = new OutboxMessage(eventId, "bookings.payment-refund-requested", 1, booking.Id.ToString(), 2, payload);
                await bookingRepository.AddOutboxMessageAsync(outboxMessage);

                logger.LogInformation("Enqueued PaymentRefundRequestedV1 for cancelled booking #{BookingId} (Payment #{PaymentId}).",
                    booking.Id, paymentId);
            }

            await unitOfWork.CompleteAsync();
            await tx.CommitAsync();

            logger.LogInformation("Booking #{BookingId} cancelled and night locks released.", booking.Id);
            return booking;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            await tx.RollbackAsync();
            logger.LogWarning(ex, "Concurrency conflict cancelling booking #{BookingId}.", command.BookingId);
            throw;
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            logger.LogError(ex, "Error cancelling booking #{BookingId}.", command.BookingId);
            throw;
        }
    }

    public async Task<int> ExpirePendingBookingsAsync()
    {
        var expiredBookings = await bookingRepository.GetExpiredPendingBookingsAsync();
        if (expiredBookings.Count == 0) return 0;

        var count = 0;
        foreach (var booking in expiredBookings)
        {
            await using var tx = await unitOfWork.BeginTransactionAsync();
            try
            {
                booking.Expire();
                await bookingRepository.RemoveLocksByBookingIdAsync(booking.Id);
                bookingRepository.Update(booking);
                await unitOfWork.CompleteAsync();
                await tx.CommitAsync();
                count++;
                logger.LogInformation("Booking #{BookingId} expired and night locks released.", booking.Id);
            }
            catch (DbUpdateConcurrencyException)
            {
                await tx.RollbackAsync();
                logger.LogInformation("Booking #{BookingId} was updated or confirmed concurrently. Skipping expiration.", booking.Id);
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                logger.LogWarning(ex, "Failed to expire booking #{BookingId}.", booking.Id);
            }
        }

        return count;
    }
}
