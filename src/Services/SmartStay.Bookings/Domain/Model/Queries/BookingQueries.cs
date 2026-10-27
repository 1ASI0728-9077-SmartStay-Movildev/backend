namespace SmartStay.Bookings.Domain.Model.Queries;

public record GetBookingByIdQuery(int BookingId);

public record GetAllBookingsQuery;

public record GetBookingsByRoomIdQuery(int RoomId);
