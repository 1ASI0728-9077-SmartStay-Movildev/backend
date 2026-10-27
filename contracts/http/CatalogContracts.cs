namespace SmartStay.Contracts.Http;

/// <summary>
/// Instantánea inmutable de catálogo de una habitación utilizada por Bookings.
/// </summary>
public sealed record RoomSnapshot(
    int RoomId,
    int HotelId,
    bool IsBookable,
    decimal NightlyPrice,
    string Currency = "USD"
);

/// <summary>
/// Contrato de elegibilidad de reserva consultado por Payments antes de procesar cobros.
/// </summary>
public sealed record BookingEligibilityResponse(
    int BookingId,
    int RoomId,
    int HotelId,
    decimal TotalAmount,
    string Currency,
    string Status,
    bool IsEligibleForPayment
);
