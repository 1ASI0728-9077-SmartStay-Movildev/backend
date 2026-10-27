namespace SmartStay.Bookings.Domain.Model.Commands;

public record CreateBookingCommand(
    int RoomId,
    string GuestName,
    string GuestEmail,
    DateTime CheckInDate,
    DateTime CheckOutDate
);

public record ConfirmBookingCommand(int BookingId);

public record CancelBookingCommand(int BookingId);
