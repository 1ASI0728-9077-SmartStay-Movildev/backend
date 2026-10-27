using SmartStay.Bookings.Domain.Model.Aggregates;
using SmartStay.Bookings.Domain.Model.Queries;
using SmartStay.Bookings.Domain.Repositories;
using SmartStay.Bookings.Domain.Services;

namespace SmartStay.Bookings.Application.Internal.QueryServices;

public class BookingQueryService(IBookingRepository bookingRepository) : IBookingQueryService
{
    public async Task<Booking?> Handle(GetBookingByIdQuery query)
    {
        return await bookingRepository.FindByIdAsync(query.BookingId);
    }

    public async Task<IEnumerable<Booking>> Handle(GetAllBookingsQuery query)
    {
        return await bookingRepository.ListAsync();
    }

    public async Task<IEnumerable<Booking>> Handle(GetBookingsByRoomIdQuery query)
    {
        return await bookingRepository.ListByRoomIdAsync(query.RoomId);
    }
}
