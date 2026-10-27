using SmartStay.Bookings.Domain.Model.Aggregates;
using SmartStay.Bookings.Domain.Model.Commands;
using SmartStay.Bookings.Domain.Model.Queries;
using SmartStay.Contracts.Http;

namespace SmartStay.Bookings.Domain.Services;

public interface IBookingCommandService
{
    Task<Booking?> Handle(CreateBookingCommand command);
    Task<Booking?> Handle(ConfirmBookingCommand command);
    Task<Booking?> Handle(CancelBookingCommand command);
    Task<int> ExpirePendingBookingsAsync();
}

public interface IBookingQueryService
{
    Task<Booking?> Handle(GetBookingByIdQuery query);
    Task<IEnumerable<Booking>> Handle(GetAllBookingsQuery query);
    Task<IEnumerable<Booking>> Handle(GetBookingsByRoomIdQuery query);
    Task<IEnumerable<Booking>> GetBookingsByGuestUserIdAsync(int guestUserId);
    Task<AvailabilityResponse> CheckAvailabilityAsync(AvailabilityRequest request);
}
