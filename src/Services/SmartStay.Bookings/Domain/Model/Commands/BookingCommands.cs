namespace SmartStay.Bookings.Domain.Model.Commands;

public record CreateBookingCommand(
    int RoomId,
    string GuestName,
    string GuestEmail,
    DateTime CheckInDate,
    DateTime CheckOutDate,
    int GuestUserId = 0,
    string? QuoteId = null,
    string? PurchaseIntentId = null
);

public record CreateBookingQuoteCommand(
    int RoomId,
    int GuestUserId,
    string PurchaseIntentId,
    DateOnly CheckIn,
    DateOnly CheckOut,
    int Guests
);

public record ConfirmBookingCommand(int BookingId);

public record CancelBookingCommand(int BookingId);
