using SmartStay.Bookings.Domain.Model.Aggregates;
using SmartStay.Bookings.Domain.Model.Commands;
using SmartStay.Bookings.Domain.Model.Queries;

namespace SmartStay.Bookings.Domain.Services;

public interface IBookingCommandService
{
    Task<Booking?> Handle(CreateBookingCommand command);
    Task<Booking?> Handle(ConfirmBookingCommand command);
    Task<Booking?> Handle(CancelBookingCommand command);
}

public interface IBookingQueryService
{
    Task<Booking?> Handle(GetBookingByIdQuery query);
    Task<IEnumerable<Booking>> Handle(GetAllBookingsQuery query);
    Task<IEnumerable<Booking>> Handle(GetBookingsByRoomIdQuery query);
}
