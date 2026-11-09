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

/// <summary>
/// Resultado de búsqueda de habitaciones para el catálogo y Concierge.
/// </summary>
public sealed record RoomSearchResult(
    int RoomId,
    int HotelId,
    string HotelName,
    string District,
    string City,
    string RoomTypeName,
    decimal NightlyPrice,
    string Currency,
    int Capacity,
    bool IsBookable,
    string? TourPath,
    IReadOnlyList<string> Amenities,
    double? AreaM2 = null,
    string? BedSummary = null,
    bool? HasVr = null
);
