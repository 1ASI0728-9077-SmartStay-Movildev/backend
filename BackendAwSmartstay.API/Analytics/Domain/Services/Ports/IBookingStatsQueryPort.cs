namespace BackendAwSmartstay.API.Analytics.Domain.Services.Ports;

/// <summary>
/// Puerto de consulta para métricas de reservas, desacoplado de la persistencia directa de Bookings.
/// </summary>
public interface IBookingStatsQueryPort
{
    Task<(int TotalBookings, int CancelledBookings, int ConfirmedBookings)> GetBookingStatsAsync(DateTime startUtc, DateTime endUtcExclusive);
}
