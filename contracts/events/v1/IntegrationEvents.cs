namespace SmartStay.Contracts.Events.V1;

/// <summary>
/// Sobre estándar para eventos de integración en el sistema SmartStay.
/// </summary>
public sealed record IntegrationEvent<T>(
    Guid EventId,
    string EventType,
    int SchemaVersion,
    DateTimeOffset OccurredAtUtc,
    string CorrelationId,
    string? CausationId,
    string AggregateId,
    long AggregateVersion,
    T Data
);

public sealed record PaymentCompletedV1(
    int PaymentId,
    int BookingId,
    int HotelId,
    decimal Amount,
    string Currency
);

public sealed record BookingCreatedV1(
    int BookingId,
    int RoomId,
    int HotelId,
    string GuestEmail,
    DateTime CheckInDate,
    DateTime CheckOutDate
);

public sealed record BookingConfirmedV1(
    int BookingId,
    int RoomId,
    int HotelId,
    DateTime ConfirmedAtUtc
);

public sealed record BookingCancelledV1(
    int BookingId,
    int RoomId,
    string Reason
);

public sealed record BookingExpiredV1(
    int BookingId,
    int RoomId
);

public sealed record PaymentRefundRequestedV1(
    int PaymentId,
    int BookingId,
    string Reason
);

public sealed record PaymentRefundedV1(
    int PaymentId,
    int BookingId,
    decimal Amount
);
