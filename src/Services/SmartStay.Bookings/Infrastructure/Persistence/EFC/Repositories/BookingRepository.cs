using Microsoft.EntityFrameworkCore;
using SmartStay.Bookings.Domain.Model.Aggregates;
using SmartStay.Bookings.Domain.Model.Entities;
using SmartStay.Bookings.Domain.Repositories;
using SmartStay.Bookings.Infrastructure.Persistence.EFC.Configuration;

namespace SmartStay.Bookings.Infrastructure.Persistence.EFC.Repositories;

public class BookingRepository(BookingsDbContext context) : IBookingRepository
{
    public async Task AddAsync(Booking booking)
    {
        await context.Bookings.AddAsync(booking);
    }

    public async Task<Booking?> FindByIdAsync(int id)
    {
        return await context.Bookings
            .Include(b => b.NightLocks)
            .FirstOrDefaultAsync(b => b.Id == id);
    }

    public async Task<IEnumerable<Booking>> ListAsync()
    {
        return await context.Bookings
            .Include(b => b.NightLocks)
            .ToListAsync();
    }

    public async Task<IEnumerable<Booking>> ListByRoomIdAsync(int roomId)
    {
        return await context.Bookings
            .Include(b => b.NightLocks)
            .Where(b => b.RoomId == roomId)
            .ToListAsync();
    }

    public async Task<IEnumerable<Booking>> ListByGuestUserIdAsync(int guestUserId)
    {
        return await context.Bookings
            .Include(b => b.NightLocks)
            .Where(b => b.GuestUserId == guestUserId)
            .OrderByDescending(b => b.CreatedAtUtc)
            .ToListAsync();
    }

    public void Update(Booking booking)
    {
        context.Bookings.Update(booking);
    }

    public async Task AddNightLockAsync(RoomNightLock nightLock)
    {
        await context.RoomNightLocks.AddAsync(nightLock);
    }

    public async Task RemoveLocksByBookingIdAsync(int bookingId)
    {
        var locks = await context.RoomNightLocks.Where(l => l.BookingId == bookingId).ToListAsync();
        context.RoomNightLocks.RemoveRange(locks);
    }

    public async Task<bool> HasConflictAsync(int roomId, DateOnly stayDate)
    {
        return await context.RoomNightLocks.AnyAsync(l => l.RoomId == roomId && l.StayDate == stayDate);
    }

    public async Task<List<int>> GetLockedRoomIdsAsync(DateOnly checkIn, DateOnly checkOut, int[] roomIds)
    {
        return await context.RoomNightLocks
            .Where(l => roomIds.Contains(l.RoomId) && l.StayDate >= checkIn && l.StayDate < checkOut)
            .Select(l => l.RoomId)
            .Distinct()
            .ToListAsync();
    }

    public async Task<List<Booking>> GetExpiredPendingBookingsAsync()
    {
        var now = DateTime.UtcNow;
        return await context.Bookings
            .Include(b => b.NightLocks)
            .Where(b => b.Status == BookingStatus.PendingPayment && b.ExpiresAtUtc != null && b.ExpiresAtUtc <= now)
            .ToListAsync();
    }

    public async Task AddQuoteAsync(BookingQuote quote)
    {
        await context.BookingQuotes.AddAsync(quote);
    }

    public async Task<BookingQuote?> FindQuoteByIdAsync(string quoteId)
    {
        return await context.BookingQuotes.FirstOrDefaultAsync(q => q.QuoteId == quoteId);
    }

    public async Task<Booking?> FindByPurchaseIntentIdAsync(string purchaseIntentId)
    {
        return await context.Bookings
            .Include(b => b.NightLocks)
            .FirstOrDefaultAsync(b => b.PurchaseIntentId == purchaseIntentId);
    }

    public async Task AddOutboxMessageAsync(OutboxMessage outboxMessage)
    {
        await context.OutboxMessages.AddAsync(outboxMessage);
    }

    public async Task<List<OutboxMessage>> GetPendingOutboxMessagesAsync(int batchSize, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return await context.OutboxMessages
            .Where(m => m.PublishedAtUtc == null && (m.NextAttemptAtUtc == null || m.NextAttemptAtUtc <= now))
            .OrderBy(m => m.OccurredAtUtc)
            .Take(batchSize)
            .ToListAsync(ct);
    }
}
