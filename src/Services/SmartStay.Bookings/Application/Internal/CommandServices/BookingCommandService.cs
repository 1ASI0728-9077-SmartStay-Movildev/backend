using Microsoft.EntityFrameworkCore;
using SmartStay.Bookings.Domain.Model.Aggregates;
using SmartStay.Bookings.Domain.Model.Commands;
using SmartStay.Bookings.Domain.Model.Entities;
using SmartStay.Bookings.Domain.Model.Exceptions;
using SmartStay.Bookings.Domain.Repositories;
using SmartStay.Bookings.Domain.Services;
using SmartStay.Bookings.Infrastructure.Clients;

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
        if (stayDuration < 1)
        {
            logger.LogWarning("Booking duration must be at least 1 night.");
            return null;
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
                command.GuestUserId
            );

            await bookingRepository.AddAsync(booking);
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
            logger.LogWarning(ex, "Concurrency collision: Room #{RoomId} was locked concurrently for the requested dates.", command.RoomId);
            throw new RoomAlreadyBookedException(command.RoomId, stayDates[0]);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            logger.LogError(ex, "Unexpected error creating booking for room {RoomId}.", command.RoomId);
            throw;
        }
    }

    public async Task<Booking?> Handle(ConfirmBookingCommand command)
    {
        var booking = await bookingRepository.FindByIdAsync(command.BookingId);
        if (booking is null) return null;

        booking.Confirm();
        bookingRepository.Update(booking);
        await unitOfWork.CompleteAsync();

        logger.LogInformation("Booking #{BookingId} confirmed.", booking.Id);
        return booking;
    }

    public async Task<Booking?> Handle(CancelBookingCommand command)
    {
        var booking = await bookingRepository.FindByIdAsync(command.BookingId);
        if (booking is null) return null;

        await using var tx = await unitOfWork.BeginTransactionAsync();
        try
        {
            booking.Cancel();
            await bookingRepository.RemoveLocksByBookingIdAsync(booking.Id);
            await unitOfWork.CompleteAsync();
            await tx.CommitAsync();

            logger.LogInformation("Booking #{BookingId} cancelled and night locks released.", booking.Id);
            return booking;
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
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                logger.LogWarning(ex, "Failed to expire booking #{BookingId}.", booking.Id);
            }
        }

        return count;
    }
}
