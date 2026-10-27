using SmartStay.Bookings.Domain.Model.Aggregates;
using SmartStay.Bookings.Domain.Model.Commands;
using SmartStay.Bookings.Interfaces.REST.Resources;

namespace SmartStay.Bookings.Interfaces.REST.Transform;

public static class BookingAssemblers
{
    public static BookingResource ToResource(Booking booking)
    {
        return new BookingResource(
            booking.Id,
            booking.RoomId,
            booking.HotelId,
            booking.GuestName,
            booking.GuestEmail,
            booking.CheckInDate,
            booking.CheckOutDate,
            booking.TotalAmount,
            booking.Currency,
            booking.Status.ToString()
        );
    }

    public static CreateBookingCommand ToCommand(CreateBookingResource resource)
    {
        return new CreateBookingCommand(
            resource.RoomId,
            resource.GuestName,
            resource.GuestEmail,
            resource.CheckInDate,
            resource.CheckOutDate
        );
    }
}
