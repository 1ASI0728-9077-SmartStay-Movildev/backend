namespace SmartStay.Concierge.Domain;

/// <summary>
/// Oferta de habitación evaluada determinísticamente con datos del servidor.
/// </summary>
public sealed record RoomOffer(
    int RoomId,
    int HotelId,
    string HotelName,
    string RoomTypeName,
    string District,
    decimal NightlyPrice,
    decimal StayTotal,
    string Currency,
    int Capacity,
    string? TourPath,
    IReadOnlyList<string> Amenities,
    string RecommendationReason = ""
);
