namespace SmartStay.Bookings.Domain.Model.Queries;

public record GetBookingByIdQuery(int BookingId);

public record GetAllBookingsQuery;

public record GetBookingsByRoomIdQuery(int RoomId);

public record GetQuoteByIdQuery(string QuoteId);

public record GetBookingByPurchaseIntentIdQuery(string PurchaseIntentId);
