using SmartStay.Bookings.Domain.Model.Aggregates;
using SmartStay.Bookings.Domain.Model.Queries;
using SmartStay.Bookings.Domain.Repositories;
using SmartStay.Bookings.Domain.Services;
using SmartStay.Contracts.Http;

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

    public async Task<IEnumerable<Booking>> GetBookingsByGuestUserIdAsync(int guestUserId)
    {
        return await bookingRepository.ListByGuestUserIdAsync(guestUserId);
    }

    public async Task<AvailabilityResponse> CheckAvailabilityAsync(AvailabilityRequest request)
    {
        if (request.RoomIds == null || request.RoomIds.Length == 0)
        {
            return new AvailabilityResponse([]);
        }

        // Limitar a máximo 50 RoomIds por especificación
        var targetRoomIds = request.RoomIds.Take(50).Distinct().ToArray();
        var lockedIds = await bookingRepository.GetLockedRoomIdsAsync(request.CheckIn, request.CheckOut, targetRoomIds);

        var available = targetRoomIds.Except(lockedIds).ToArray();
        return new AvailabilityResponse(available);
    }
}
