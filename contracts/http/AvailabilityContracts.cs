namespace SmartStay.Contracts.Http;

/// <summary>
/// Solicitud de validación de disponibilidad orientativa para un intervalo de fechas y conjunto de habitaciones.
/// </summary>
public sealed record AvailabilityRequest(
    DateOnly CheckIn,
    DateOnly CheckOut,
    int[] RoomIds
);

/// <summary>
/// Respuesta con las habitaciones que no tienen cerrojos de noches en el intervalo solicitado.
/// </summary>
public sealed record AvailabilityResponse(
    int[] AvailableRoomIds
);
