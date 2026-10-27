using SmartStay.Bookings.Domain.Model.Aggregates;
using SmartStay.Bookings.Domain.Model.Entities;

namespace SmartStay.Bookings.Domain.Repositories;

public interface IBookingRepository
{
    Task AddAsync(Booking booking);
    Task<Booking?> FindByIdAsync(int id);
    Task<IEnumerable<Booking>> ListAsync();
    Task<IEnumerable<Booking>> ListByRoomIdAsync(int roomId);
    void Update(Booking booking);
    Task AddNightLockAsync(RoomNightLock nightLock);
    Task RemoveLocksByBookingIdAsync(int bookingId);
    Task<bool> HasConflictAsync(int roomId, DateOnly stayDate);
}
