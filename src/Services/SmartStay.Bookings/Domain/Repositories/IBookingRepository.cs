using SmartStay.Bookings.Domain.Model.Aggregates;
using SmartStay.Bookings.Domain.Model.Entities;

namespace SmartStay.Bookings.Domain.Repositories;

public interface IBookingRepository
{
    Task AddAsync(Booking booking);
    Task<Booking?> FindByIdAsync(int id);
    Task<IEnumerable<Booking>> ListAsync();
    Task<IEnumerable<Booking>> ListByRoomIdAsync(int roomId);
    Task<IEnumerable<Booking>> ListByGuestUserIdAsync(int guestUserId);
    void Update(Booking booking);
    Task AddNightLockAsync(RoomNightLock nightLock);
    Task RemoveLocksByBookingIdAsync(int bookingId);
    Task<bool> HasConflictAsync(int roomId, DateOnly stayDate);
    Task<List<int>> GetLockedRoomIdsAsync(DateOnly checkIn, DateOnly checkOut, int[] roomIds);
    Task<List<Booking>> GetExpiredPendingBookingsAsync();
    Task AddQuoteAsync(BookingQuote quote);
    Task<BookingQuote?> FindQuoteByIdAsync(string quoteId);
    Task<Booking?> FindByPurchaseIntentIdAsync(string purchaseIntentId);
    Task AddOutboxMessageAsync(OutboxMessage outboxMessage);
    Task<List<OutboxMessage>> GetPendingOutboxMessagesAsync(int batchSize, CancellationToken ct = default);
}
