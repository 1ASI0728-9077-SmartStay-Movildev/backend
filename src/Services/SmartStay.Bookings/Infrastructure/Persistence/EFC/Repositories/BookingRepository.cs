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
}
