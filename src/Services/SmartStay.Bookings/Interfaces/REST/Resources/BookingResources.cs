namespace SmartStay.Bookings.Interfaces.REST.Resources;

public record BookingResource(
    int Id,
    int RoomId,
    int HotelId,
    string GuestName,
    string GuestEmail,
    DateTime CheckInDate,
    DateTime CheckOutDate,
    decimal TotalAmount,
    string Currency,
    string Status
);

public record CreateBookingResource(
    int RoomId,
    string GuestName,
    string GuestEmail,
    DateTime CheckInDate,
    DateTime CheckOutDate
);
