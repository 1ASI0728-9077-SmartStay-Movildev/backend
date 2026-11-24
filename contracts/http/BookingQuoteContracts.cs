namespace SmartStay.Contracts.Http;

/// <summary>
/// Solicitud interna de cotización temporal (2 minutos) emitida por Concierge hacia Bookings.
/// </summary>
public sealed record CreateBookingQuoteRequest(
    int RoomId,
    int GuestUserId,
    string PurchaseIntentId,
    string CheckIn,
    string CheckOut,
    int Guests
);

/// <summary>
/// Respuesta de cotización temporal con TTL de 2 minutos.
/// </summary>
public sealed record BookingQuoteResponse(
    string QuoteId,
    int RoomId,
    decimal StayTotal,
    string Currency,
    DateTime ExpiresAtUtc
);

/// <summary>
/// Respuesta de consulta de reserva por identificador de intención de compra para recuperación tras fallos.
/// </summary>
public sealed record BookingByIntentResponse(
    int BookingId,
    int RoomId,
    int HotelId,
    int GuestUserId,
    decimal TotalAmount,
    string Currency,
    string Status,
    DateTime? ExpiresAtUtc,
    int? PaymentId,
    DateTime CreatedAtUtc
);
